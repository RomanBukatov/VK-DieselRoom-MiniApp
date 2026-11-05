using System.Security.Cryptography;
using System.Text;

namespace DieselRoom.Domain.Services;

/// <summary>
/// Временный проверочный код привилегии: 6 цифр, HMAC-SHA256 от секрета сервера, гостя и номера минутного окна.
/// Код не связан с номером карты и реферальной ссылкой, поэтому чужую привилегию нельзя предъявить, просто зная номер.
/// </summary>
public static class PrivilegeCodes
{
    public const int WindowSeconds = 60;
    public const string QrPrefix = "P-";

    public static long WindowOf(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds() / WindowSeconds;

    public static DateTime WindowEnd(long window) =>
        DateTimeOffset.FromUnixTimeSeconds((window + 1) * WindowSeconds).UtcDateTime;

    public static string Generate(string secret, int userId, long window)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"privilege-code:{userId}:{window}"));
        return (BitConverter.ToUInt32(hash, 0) % 1_000_000).ToString("D6");
    }

    /// <summary>Окно, которому соответствует код: текущее или предыдущее (запас на то, пока официант набирает код).</summary>
    public static long? MatchWindow(string secret, int userId, string? code, DateTime utcNow)
    {
        if (string.IsNullOrEmpty(code)) return null;
        var current = WindowOf(utcNow);
        var actual = Encoding.UTF8.GetBytes(code);
        foreach (var window in new[] { current, current - 1 })
        {
            if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Generate(secret, userId, window)), actual)) return window;
        }
        return null;
    }
}
