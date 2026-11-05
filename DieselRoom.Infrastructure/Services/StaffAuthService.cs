using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using DieselRoom.Domain.Entities;
using DieselRoom.Domain.Enums;
using DieselRoom.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DieselRoom.Infrastructure.Services;

/// <summary>Конфигурация ролей персонала из окружения.</summary>
public sealed class StaffAuthOptions
{
    /// <summary>VK ID администраторов (владелец Стас, арт-директор Фёдор) — VK_ADMIN_IDS.</summary>
    public List<string> AdminVkIds { get; set; } = new();
    /// <summary>VK ID сотрудников из окружения (VK_STAFF_IDS) — в дополнение к списку в админке.</summary>
    public List<string> StaffVkIds { get; set; } = new();
    /// <summary>Мастер-ключ администратора (DIESEL_MASTER_KEY). Пусто или короче 12 символов — вход по нему выключен.</summary>
    public string MasterKey { get; set; } = string.Empty;
    /// <summary>Секрет («перец») для отпечатков ПИН-кодов (DIESEL_PIN_PEPPER).</summary>
    public string PinPepper { get; set; } = string.Empty;
}

/// <summary>
/// Разделение ролей «Сотрудник» / «Администратор». VK ID уже проверен по подписи параметров запуска;
/// мастер-ключ и ПИН-код приходят заголовками X-Admin-Key / X-Staff-Pin.
/// </summary>
public class StaffAuthService
{
    public const int MinMasterKeyLength = 12;
    public const int MaxPinFailures = 5;
    public static readonly TimeSpan PinLockout = TimeSpan.FromMinutes(15);

    // Подбор ПИН-кода: не больше 5 ошибок за 15 минут на VK ID (ключ — VK ID или IP)
    private static readonly ConcurrentDictionary<string, (int Failures, DateTime WindowStart)> PinFailures = new();

    private readonly AppDbContext _db;
    private readonly StaffAuthOptions _options;

    public StaffAuthService(AppDbContext db, StaffAuthOptions options)
    {
        _db = db;
        _options = options;
    }

    public static string NormalizeVkId(string id)
    {
        var trimmed = id.Trim();
        if (trimmed.StartsWith("id", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 2 && char.IsDigit(trimmed[2]))
        {
            return trimmed[2..];
        }
        return trimmed;
    }

    public bool IsConfiguredAdmin(string? vkUserId) => ContainsVkId(_options.AdminVkIds, vkUserId);

    public bool IsMasterKey(string? key)
    {
        var master = _options.MasterKey;
        if (string.IsNullOrWhiteSpace(master) || master.Length < MinMasterKeyLength || string.IsNullOrEmpty(key)) return false;
        var a = Encoding.UTF8.GetBytes(master);
        var b = Encoding.UTF8.GetBytes(key.Trim());
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>ПИН-код сотрудника: 4–8 цифр.</summary>
    public static bool IsValidPinFormat(string? pin) => pin is { Length: >= 4 and <= 8 } && pin.All(char.IsDigit);

    /// <summary>Отпечаток ПИН-кода: HMAC-SHA256 с секретом сервера. Хранится только он, сам ПИН — нигде.</summary>
    public string HashPin(string pin)
    {
        var pepper = string.IsNullOrEmpty(_options.PinPepper) ? "diesel-room-pin" : _options.PinPepper;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(pepper));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(pin.Trim())));
    }

    /// <summary>
    /// Определяет роль. Порядок: AdminVkIds → мастер-ключ → сотрудник по VK ID (админка / VK_STAFF_IDS) → ПИН-код.
    /// Если ПИН указан, но неверный — Guest (и счётчик ошибок растёт).
    /// </summary>
    public async Task<StaffIdentity> ResolveAsync(string vkUserId, string? masterKey, string? pin, string throttleKey,
        CancellationToken ct = default)
    {
        var vkId = NormalizeVkId(vkUserId);

        if (IsConfiguredAdmin(vkId))
        {
            var named = await FindByVkIdAsync(vkId, ct);
            return new StaffIdentity(StaffRole.Admin, named?.Id, named?.Name ?? $"Администратор (VK {vkId})", vkId, vkId);
        }

        if (!string.IsNullOrWhiteSpace(masterKey) && IsMasterKey(masterKey))
        {
            return new StaffIdentity(StaffRole.Admin, null, "Администратор (мастер-ключ)", vkId);
        }

        var byVk = await FindByVkIdAsync(vkId, ct);
        if (byVk != null)
        {
            return new StaffIdentity(byVk.Role, byVk.Id, byVk.Name, vkId, vkId);
        }

        if (ContainsVkId(_options.StaffVkIds, vkId))
        {
            return new StaffIdentity(StaffRole.Staff, null, $"Сотрудник (VK {vkId})", vkId, vkId);
        }

        if (!string.IsNullOrWhiteSpace(pin))
        {
            if (IsPinLocked(throttleKey)) return StaffIdentity.Guest;

            var byPin = IsValidPinFormat(pin.Trim()) ? await FindByPinAsync(pin, ct) : null;
            if (byPin == null)
            {
                RegisterPinFailure(throttleKey);
                return StaffIdentity.Guest;
            }

            PinFailures.TryRemove(throttleKey, out _);
            return new StaffIdentity(byPin.Role, byPin.Id, byPin.Name, vkId, byPin.VkId);
        }

        return StaffIdentity.Guest;
    }

    public static bool IsPinLocked(string throttleKey)
    {
        if (!PinFailures.TryGetValue(throttleKey, out var entry)) return false;
        if (DateTime.UtcNow - entry.WindowStart > PinLockout)
        {
            PinFailures.TryRemove(throttleKey, out _);
            return false;
        }
        return entry.Failures >= MaxPinFailures;
    }

    private static void RegisterPinFailure(string throttleKey)
    {
        var now = DateTime.UtcNow;
        PinFailures.AddOrUpdate(throttleKey, _ => (1, now), (_, e) =>
            now - e.WindowStart > PinLockout ? (1, now) : (e.Failures + 1, e.WindowStart));
    }

    /// <summary>Только для тестов.</summary>
    public static void ResetPinThrottleForTests() => PinFailures.Clear();

    private Task<StaffMember?> FindByVkIdAsync(string vkId, CancellationToken ct) =>
        _db.StaffMembers.AsNoTracking().FirstOrDefaultAsync(s => s.IsActive && s.VkId == vkId, ct);

    private Task<StaffMember?> FindByPinAsync(string pin, CancellationToken ct)
    {
        var hash = HashPin(pin);
        return _db.StaffMembers.AsNoTracking().FirstOrDefaultAsync(s => s.IsActive && s.PinHash == hash, ct);
    }

    private static bool ContainsVkId(IEnumerable<string>? list, string? vkUserId)
    {
        if (list == null || string.IsNullOrWhiteSpace(vkUserId)) return false;
        var target = NormalizeVkId(vkUserId);
        return list.Any(id => !string.IsNullOrWhiteSpace(id) && string.Equals(NormalizeVkId(id), target, StringComparison.OrdinalIgnoreCase));
    }
}
