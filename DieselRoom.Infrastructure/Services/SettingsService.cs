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
public sealed record StaffIdentity(StaffRole Role, int? StaffMemberId, string Name, string? VkId)
{
    public static readonly StaffIdentity Guest = new(StaffRole.Guest, null, string.Empty, null);
    public bool IsStaff => Role >= StaffRole.Staff;
    public bool IsAdmin => Role == StaffRole.Admin;
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
