using Microsoft.Extensions.Logging;

namespace DieselRoom.Infrastructure.Services;

/// <summary>Личные сообщения гостям от сообщества DIESEL ROOM в ВК (в фоне, после фиксации транзакции).</summary>
public interface IGuestNotifier
{
    void Send(string? vkId, string message, string description);
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
