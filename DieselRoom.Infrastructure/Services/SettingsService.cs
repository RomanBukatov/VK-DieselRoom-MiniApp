using System.Text.Json;
using DieselRoom.Domain.Entities;
using DieselRoom.Domain.Enums;
using DieselRoom.Domain.Services;
using DieselRoom.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DieselRoom.Infrastructure.Services;

/// <summary>Настройки заведения в таблице AppSettings (ставки начисления, служебные отметки воркеров).</summary>
public class SettingsService
{
    public const string RatesKey = "loyalty.rates";
    public const string LevelsKey = "loyalty.levels";
    public const string AchievementsKey = "loyalty.achievements";
    public const string LastClosedWeekKey = "race.lastClosedWeek";

    private readonly AppDbContext _db;

    public SettingsService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<LoyaltyRates> GetRatesAsync(CancellationToken ct = default)
    {
        var raw = await GetAsync(RatesKey, ct);
        if (string.IsNullOrWhiteSpace(raw)) return LoyaltyRates.Default;
        try
        {
            return JsonSerializer.Deserialize<LoyaltyRates>(raw) ?? LoyaltyRates.Default;
        }
        catch (JsonException)
        {
            return LoyaltyRates.Default;
        }
    }

    public Task SaveRatesAsync(LoyaltyRates rates, CancellationToken ct = default) =>
        SetAsync(RatesKey, JsonSerializer.Serialize(rates), ct);

    public async Task<IReadOnlyList<LevelRule>> GetLevelsAsync(CancellationToken ct = default)
    {
        var raw = await GetAsync(LevelsKey, ct);
        if (string.IsNullOrWhiteSpace(raw)) return GuestLevels.Default;
        try
        {
            var levels = JsonSerializer.Deserialize<List<LevelRule>>(raw);
            return levels != null && !GuestLevels.Validate(levels).Any() ? levels : GuestLevels.Default;
        }
        catch (JsonException)
        {
            return GuestLevels.Default;
        }
    }

    public async Task<IReadOnlyList<AchievementRule>> GetAchievementsAsync(CancellationToken ct = default)
    {
        var raw = await GetAsync(AchievementsKey, ct);
        if (string.IsNullOrWhiteSpace(raw)) return Achievements.Default;
        try
        {
            var rules = JsonSerializer.Deserialize<List<AchievementRule>>(raw);
            return rules != null && !Achievements.Validate(rules).Any() ? rules : Achievements.Default;
        }
        catch (JsonException)
        {
            return Achievements.Default;
        }
    }

    public Task SaveAchievementsAsync(IReadOnlyList<AchievementRule> rules, CancellationToken ct = default) =>
        SetAsync(AchievementsKey, JsonSerializer.Serialize(rules), ct);

    public Task SaveLevelsAsync(IReadOnlyList<LevelRule> levels, CancellationToken ct = default) =>
        SetAsync(LevelsKey, JsonSerializer.Serialize(levels), ct);

    public async Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        await _db.AppSettings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(ct);

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        var existing = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (existing == null)
        {
            _db.AppSettings.Add(new AppSetting { Key = key, Value = value, UpdatedAt = DateTime.UtcNow });
        }
        else
        {
            existing.Value = value;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }
}

/// <summary>Кто выполняет действие в панели персонала.</summary>
/// <param name="VkId">VK ID, с которого выполнен вход (на общем планшете — учётная запись планшета).</param>
/// <param name="LinkedVkId">VK ID, привязанный к сотруднику в админке: его личный профиль гостя.</param>
public sealed record StaffIdentity(StaffRole Role, int? StaffMemberId, string Name, string? VkId, string? LinkedVkId = null)
{
    public static readonly StaffIdentity Guest = new(StaffRole.Guest, null, string.Empty, null);
    public bool IsStaff => Role >= StaffRole.Staff;
    public bool IsAdmin => Role == StaffRole.Admin;

    /// <summary>Сотрудник не начисляет и не выдаёт ничего самому себе: ни со своего телефона, ни по своему ПИН-коду.</summary>
    public bool IsSameGuest(string? guestVkId) =>
        !string.IsNullOrWhiteSpace(guestVkId) && (guestVkId == VkId || guestVkId == LinkedVkId);

    /// <summary>
    /// Запрет самообслуживания касается сотрудников. Владелец и арт-директор (Admin) при приёмке проверяют программу
    /// на себе: тестовые чеки, подарки и привилегии им разрешены, а следы остаются в журнале аудита.
    /// </summary>
    public bool IsSelf(string? guestVkId) => !IsAdmin && IsSameGuest(guestVkId);

    public const string SelfServiceError = "Нельзя начислять и выдавать самому себе: попросите другого сотрудника.";
}

public static class StaffAudit
{
    /// <summary>Добавляет запись журнала в контекст (сохраняется вместе с операцией — в той же транзакции).</summary>
    public static void Add(AppDbContext db, StaffIdentity staff, string action, User? guest = null,
        string? receiptNumber = null, string? details = null, DateTime? now = null)
    {
        db.StaffAuditLogs.Add(new StaffAuditLog
        {
            StaffMemberId = staff.StaffMemberId,
            StaffName = Truncate(staff.Name, 100) ?? string.Empty,
            StaffVkId = staff.VkId,
            Action = action,
            GuestUserId = guest?.Id,
            GuestName = guest == null ? null : Truncate($"{guest.DisplayName} (карта {guest.CardNumber})", 200),
            ReceiptNumber = receiptNumber,
            Details = Truncate(details, 1000),
            CreatedAt = now ?? DateTime.UtcNow,
        });
    }

    private static string? Truncate(string? value, int max) =>
        value == null ? null : value.Length > max ? value[..max] : value;
}
