using DieselRoom.Domain.Entities;
using DieselRoom.Domain.Enums;
using DieselRoom.Domain.Services;
using DieselRoom.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DieselRoom.Infrastructure.Services;

/// <summary>Текущая привилегия гостя по месту в «Гонке недели» и её доступность прямо сейчас.</summary>
public sealed record ActivePrivilege(
    int? Rank,
    PrivilegeTier Tier,
    PrivilegeInfo? Info,
    PrivilegeAvailability Availability,
    int FirstPlaceStreak);

/// <summary>
/// Переходящие привилегии (п. 4.1): привязаны к МЕСТУ, а не к гостю — как только Б обгоняет А на 2-м месте,
/// «Безлимитный кофе» на проверке у официанта моментально переходит к Б. Место пересчитывается при каждой проверке.
/// </summary>
public class PrivilegeService
{
    private readonly AppDbContext _db;
    private readonly RatingService _rating;

    public PrivilegeService(AppDbContext db, RatingService rating)
    {
        _db = db;
        _rating = rating;
    }

    public async Task<ActivePrivilege> GetActiveAsync(int userId, DateTime now, CancellationToken ct = default)
    {
        var rank = await _rating.GetWeeklyRankAsync(userId, ct);
        var tier = PrivilegeCatalog.TierForRank(rank);
        var info = PrivilegeCatalog.Get(tier);
        var weekStart = PermTime.WeekStart(now);

        var periodStart = weekStart.AddDays(-14);
        var weeks = tier == PrivilegeTier.None
            ? []
            : await _db.PrivilegeRedemptions.AsNoTracking()
                .Where(r => r.UserId == userId && r.Tier == tier && r.WeekStart >= periodStart)
                .Select(r => r.WeekStart)
                .ToListAsync(ct);

        var availability = PrivilegeCatalog.CheckAvailability(tier, weeks, weekStart, PermTime.ToLocal(now));
        var streak = rank == 1 ? await _rating.GetFirstPlaceStreakAsync(userId, now, ct) : 0;
        return new ActivePrivilege(rank, tier, info, availability, streak);
    }

    /// <summary>Официант подтверждает выдачу привилегии: проверка лимита и запись в журнал — в одной транзакции.</summary>
    public async Task<OpResult<PrivilegeRedemption>> RedeemAsync(StaffIdentity staff, int userId, DateTime now, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        // Блокируем гостя: два официанта одновременно не выдадут кальян сверх лимита
        await _db.LockRowAsync("Users", userId, ct);

        var guest = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (guest == null) return OpResult<PrivilegeRedemption>.Fail("Гость не найден.", 404);

        var active = await GetActiveAsync(userId, now, ct);
        if (active.Info == null)
        {
            return OpResult<PrivilegeRedemption>.Fail(active.Availability.Reason ?? "У гостя нет активной привилегии.", 409);
        }
        if (!active.Availability.Allowed)
        {
            return OpResult<PrivilegeRedemption>.Fail(active.Availability.Reason ?? "Привилегия сейчас недоступна.", 409);
        }

        var redemption = new PrivilegeRedemption
        {
            UserId = userId,
            Tier = active.Tier,
            WeekStart = PermTime.WeekStart(now),
            RankAtRedemption = active.Rank ?? 0,
            StaffMemberId = staff.StaffMemberId,
            StaffName = staff.Name,
            CreatedAt = now,
        };
        _db.PrivilegeRedemptions.Add(redemption);

        var used = active.Availability.Used + 1;
        var limitText = active.Info.Limit.HasValue ? $" · {used} из {active.Info.Limit}" : " · без лимита";
        StaffAudit.Add(_db, staff, StaffAuditActions.PrivilegeIssued, guest,
            details: $"{active.Info.Label} (место #{active.Rank}){limitText}", now: now);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return OpResult<PrivilegeRedemption>.Success(redemption);
    }
}
