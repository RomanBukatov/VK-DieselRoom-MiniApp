using System.Security.Cryptography;
using System.Text;
using DieselRoom.Api.Models;
using Microsoft.Extensions.Options;

namespace DieselRoom.Api.Services;

public record VkAuthResult(bool IsValid, string? VkUserId, string? ErrorMessage);

public interface IVkAuthValidator
{
    VkAuthResult Validate(HttpContext httpContext);
}

/// <summary>
/// Проверка подписи параметров запуска VK Mini App (HMAC-SHA256 по vk_* параметрам, Base64Url).
/// Заголовок X-Test-Vk-Id принимается ТОЛЬКО в Development при RequireValidation = false.
/// </summary>
public class VkAuthValidator : IVkAuthValidator
{
    private readonly VkOptions _options;
    private readonly ILogger<VkAuthValidator> _logger;
    private readonly IWebHostEnvironment? _environment;

    public VkAuthValidator(IOptions<VkOptions> options, ILogger<VkAuthValidator> logger, IWebHostEnvironment? environment = null)
    {
        _options = options.Value;
        _logger = logger;
        _environment = environment;
    }

    private bool IsDevMode => _environment?.IsDevelopment() == true && !_options.RequireValidation;

    public VkAuthResult Validate(HttpContext httpContext)
    {
        var rawParams = ExtractRawLaunchParams(httpContext);

        if (string.IsNullOrWhiteSpace(rawParams))
        {
            if (IsDevMode)
            {
                var testVkId = httpContext.Request.Headers["X-Test-Vk-Id"].FirstOrDefault() ?? "0";
                return new VkAuthResult(true, testVkId, null);
            }
            return new VkAuthResult(false, null, "Параметры запуска VK отсутствуют.");
        }

        var query = ParseQueryString(rawParams);
        var rawVkUserId = query.GetValueOrDefault("vk_user_id");
        var vkUserId = !string.IsNullOrWhiteSpace(rawVkUserId) ? Uri.UnescapeDataString(rawVkUserId) : null;

        if (!query.TryGetValue("sign", out var sign) || string.IsNullOrWhiteSpace(sign))
        {
            return IsDevMode ? new VkAuthResult(true, vkUserId ?? "0", null) : new VkAuthResult(false, null, "Параметр подписи 'sign' не найден.");
        }

        var vkParams = query
            .Where(kvp => kvp.Key.StartsWith("vk_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .ToList();
        if (vkParams.Count == 0)
        {
            return IsDevMode ? new VkAuthResult(true, vkUserId ?? "0", null) : new VkAuthResult(false, null, "Параметры vk_* не найдены.");
        }

        if (string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            _logger.LogError("VK ClientSecret не настроен в конфигурации!");
            return IsDevMode ? new VkAuthResult(true, vkUserId ?? "0", null) : new VkAuthResult(false, null, "Ошибка конфигурации сервера.");
        }

        var keyBytes = Encoding.UTF8.GetBytes(_options.ClientSecret);
        var signBytes = Encoding.UTF8.GetBytes(sign);

        // 1. Сырые (URL-encoded) значения — по официальной спецификации VK; 2. декодированные — для совместимости
        var isValid = SignMatches(keyBytes, string.Join("&", vkParams.Select(kvp => $"{kvp.Key}={kvp.Value}")), signBytes) ||
                      SignMatches(keyBytes, string.Join("&", vkParams.Select(kvp => $"{kvp.Key}={Uri.UnescapeDataString(kvp.Value)}")), signBytes);

        if (!isValid)
        {
            _logger.LogWarning("Недействительная подпись VK Sign.");
            return IsDevMode ? new VkAuthResult(true, vkUserId ?? "0", null) : new VkAuthResult(false, null, "Недействительная подпись VK параметров.");
        }

        if (string.IsNullOrWhiteSpace(vkUserId))
        {
            return new VkAuthResult(false, null, "vk_user_id отсутствует в параметрах запуска.");
        }

        if (_options.AppId > 0 && query.TryGetValue("vk_app_id", out var appId) && appId != _options.AppId.ToString())
        {
            return new VkAuthResult(false, null, "Параметры запуска выданы другому приложению.");
        }

        return new VkAuthResult(true, vkUserId, null);
    }

    public static string ComputeSign(string secret, string paramString)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(paramString)))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static bool SignMatches(byte[] keyBytes, string paramString, byte[] signBytes)
    {
        using var hmac = new HMACSHA256(keyBytes);
        var calculated = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(paramString)))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var calculatedBytes = Encoding.UTF8.GetBytes(calculated);
        return signBytes.Length == calculatedBytes.Length && CryptographicOperations.FixedTimeEquals(signBytes, calculatedBytes);
    }

    private static string? ExtractRawLaunchParams(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-VK-Sign", out var headerSign) && !string.IsNullOrWhiteSpace(headerSign))
        {
            var raw = headerSign.ToString().Trim();
            return raw.StartsWith('?') ? raw[1..] : raw;
        }

        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var authStr = authHeader.ToString();
            if (authStr.StartsWith("VK ", StringComparison.OrdinalIgnoreCase))
            {
                var raw = authStr[3..].Trim();
                return raw.StartsWith('?') ? raw[1..] : raw;
            }
        }

        var qs = context.Request.QueryString.Value;
        if (!string.IsNullOrWhiteSpace(qs))
        {
            var raw = qs.StartsWith('?') ? qs[1..] : qs;
            if (raw.Contains("vk_user_id", StringComparison.OrdinalIgnoreCase) || raw.Contains("sign=", StringComparison.OrdinalIgnoreCase))
            {
                return raw;
            }
        }

        return null;
    }

    private static Dictionary<string, string> ParseQueryString(string queryString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in queryString.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            result[Uri.UnescapeDataString(parts[0])] = parts.Length > 1 ? parts[1] : string.Empty;
        }
        return result;
    }
}
