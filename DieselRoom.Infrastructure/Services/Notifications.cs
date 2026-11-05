using System.Text.Json;
using DieselRoom.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DieselRoom.Infrastructure.Services;

/// <summary>
/// Сообщения персоналу о заявках на бронь: администраторы и сотрудники из окружения и из админки.
/// Сбой рассылки не должен ломать саму заявку, поэтому ошибки уходят только в лог.
/// </summary>
public class StaffAlertService
{
    private readonly AppDbContext _db;
    private readonly IGuestNotifier _notifier;
    private readonly StaffAuthOptions _options;
    private readonly ILogger<StaffAlertService> _logger;

    public StaffAlertService(AppDbContext db, IGuestNotifier notifier, StaffAuthOptions options, ILogger<StaffAlertService>? logger = null)
    {
        _db = db;
        _notifier = notifier;
        _options = options;
        _logger = logger ?? NullLogger<StaffAlertService>.Instance;
    }

    public async Task NotifyAsync(string message, string description, CancellationToken ct = default)
    {
        try
        {
            var fromAdmin = await _db.StaffMembers.AsNoTracking()
                .Where(s => s.IsActive && s.VkId != null)
                .Select(s => s.VkId!)
                .ToListAsync(ct);
            var recipients = _options.AdminVkIds.Concat(_options.StaffVkIds).Concat(fromAdmin)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(StaffAuthService.NormalizeVkId)
                .Distinct();
            foreach (var vkId in recipients)
            {
                _notifier.Send(vkId, message, description);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Не удалось разослать уведомление персоналу: {Description}", description);
        }
    }
}

/// <summary>Личные сообщения гостям от сообщества DIESEL ROOM в ВК (в фоне, после фиксации транзакции).</summary>
public interface IGuestNotifier
{
    void Send(string? vkId, string message, string description);
}

/// <summary>Проверка разовых заданий через API ВК. null — ответа нет (токен не задан или VK недоступен).</summary>
public interface IVkCommunityApi
{
    Task<bool?> IsMemberAsync(string vkId, CancellationToken ct = default);
    Task<bool?> AreMessagesAllowedAsync(string vkId, CancellationToken ct = default);
}

/// <summary>groups.isMember и messages.isMessagesFromGroupAllowed с токеном сообщества.</summary>
public sealed class VkCommunityApi : IVkCommunityApi
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    private readonly string? _token;
    private readonly long _groupId;
    private readonly ILogger<VkCommunityApi> _logger;

    public VkCommunityApi(string? token, long groupId, ILogger<VkCommunityApi> logger)
    {
        _token = token;
        _groupId = groupId;
        _logger = logger;
    }

    public async Task<bool?> IsMemberAsync(string vkId, CancellationToken ct = default)
    {
        using var doc = await CallAsync("groups.isMember", vkId, ct);
        return doc != null && doc.RootElement.TryGetProperty("response", out var r) && r.ValueKind == JsonValueKind.Number
            ? r.GetInt32() == 1
            : null;
    }

    public async Task<bool?> AreMessagesAllowedAsync(string vkId, CancellationToken ct = default)
    {
        using var doc = await CallAsync("messages.isMessagesFromGroupAllowed", vkId, ct);
        return doc != null && doc.RootElement.TryGetProperty("response", out var r) && r.TryGetProperty("is_allowed", out var allowed)
            ? allowed.GetInt32() == 1
            : null;
    }

    private async Task<JsonDocument?> CallAsync(string method, string vkId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_token) || _groupId <= 0) return null;
        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["group_id"] = _groupId.ToString(),
                ["user_id"] = vkId,
                ["access_token"] = _token.Trim(),
                ["v"] = "5.199",
            });
            using var response = await Http.PostAsync($"https://api.vk.com/method/{method}", content, ct);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "VK {Method} недоступен", method);
            return null;
        }
    }
}

/// <summary>Отправка через messages.send с токеном сообщества (группа 34613849). Ошибки — только в лог.</summary>
public sealed class VkGuestNotifier : IGuestNotifier
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly string? _communityToken;
    private readonly ILogger<VkGuestNotifier> _logger;

    public VkGuestNotifier(string? communityToken, ILogger<VkGuestNotifier> logger)
    {
        _communityToken = communityToken;
        _logger = logger;
    }

    public void Send(string? vkId, string message, string description)
    {
        var token = _communityToken;
        var clean = vkId?.Replace("id", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (string.IsNullOrWhiteSpace(token) || !long.TryParse(clean, out var userId) || userId <= 0)
        {
            return;
        }

        BackgroundNotifier.Run(async () =>
        {
            var parameters = new Dictionary<string, string>
            {
                ["user_id"] = userId.ToString(),
                ["peer_id"] = userId.ToString(),
                ["random_id"] = Random.Shared.Next(1, int.MaxValue).ToString(),
                ["message"] = message,
                ["access_token"] = token.Trim(),
                ["v"] = "5.199"
            };
            using var content = new FormUrlEncodedContent(parameters);
            var response = await Http.PostAsync("https://api.vk.com/method/messages.send", content);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode || body.Contains("\"error\""))
            {
                _logger.LogWarning("VK messages.send ({Description}) вернул ошибку: {Body}", description, body);
            }
        }, _logger, description);
    }
}
