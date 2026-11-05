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

/// <summary>Временный код привилегии для экрана гостя.</summary>
public sealed record PrivilegeCode(string Code, DateTime ExpiresAt);

/// <summary>
/// Переходящие привилегии (п. 4.1): привязаны к МЕСТУ, а не к гостю — как только Б обгоняет А на 2-м месте,
/// «Безлимитный кофе» на проверке у официанта моментально переходит к Б. Место пересчитывается при каждой проверке.
/// Выдача подтверждается временным кодом с экрана гостя: номера карты для этого недостаточно.
/// </summary>
public class PrivilegeService
{
    private readonly AppDbContext _db;
    private readonly RatingService _rating;
    private readonly string _codeSecret;

    public PrivilegeService(AppDbContext db, RatingService rating, StaffAuthOptions options)
    {
        _db = db;
        _rating = rating;
        _codeSecret = string.IsNullOrEmpty(options.PinPepper) ? "diesel-room-pin" : options.PinPepper;
    }

    public PrivilegeCode GetCode(int userId, DateTime now)
    {
        var window = PrivilegeCodes.WindowOf(now);
        return new PrivilegeCode(PrivilegeCodes.Generate(_codeSecret, userId, window), PrivilegeCodes.WindowEnd(window));
    }

    /// <summary>Гости топ-50, которым сейчас соответствует код. Совпадение двух гостей почти невозможно, но официант увидит обоих с фото.</summary>
    public async Task<List<int>> FindByCodeAsync(string code, DateTime now, CancellationToken ct = default)
    {
        var top = await _rating.GetWeeklyTopAsync(PrivilegeCatalog.LastPrizeRank, ct);
        return top.Where(g => PrivilegeCodes.MatchWindow(_codeSecret, g.UserId, code, now) != null).Select(g => g.UserId).ToList();
    }

    public async Task<ActivePrivilege> GetActiveAsync(int userId, DateTime now, CancellationToken ct = default)
    {
        var rank = await _rating.GetWeeklyRankAsync(userId, ct);
        var tier = PrivilegeCatalog.TierForRank(rank);
        var info = PrivilegeCatalog.Get(tier);
        var weekStart = PermTime.WeekStart(now);

        // С запасом на самое длинное скользящее окно (14 суток)
        var periodStart = weekStart.AddDays(-21);
        var stamps = tier == PrivilegeTier.None
            ? []
            : await _db.PrivilegeRedemptions.AsNoTracking()
                .Where(r => r.UserId == userId && r.Tier == tier && r.WeekStart >= periodStart)
                .Select(r => new PrivilegeStamp(r.WeekStart, r.CreatedAt))
                .ToListAsync(ct);

        var availability = PrivilegeCatalog.CheckAvailability(tier, stamps, weekStart, now);
        var streak = rank == 1 ? await _rating.GetFirstPlaceStreakAsync(userId, now, ct) : 0;
        return new ActivePrivilege(rank, tier, info, availability, streak);
    }

    /// <summary>Официант подтверждает выдачу кодом с экрана гостя: проверка кода, лимита и запись в журнал — в одной транзакции.</summary>
    public async Task<OpResult<PrivilegeRedemption>> RedeemAsync(StaffIdentity staff, int userId, string? code, DateTime now, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        // Блокируем гостя: два официанта одновременно не выдадут кальян сверх лимита
        await _db.LockRowAsync("Users", userId, ct);

        var guest = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (guest == null) return OpResult<PrivilegeRedemption>.Fail("Гость не найден.", 404);
        if (staff.IsSelf(guest.VkId)) return OpResult<PrivilegeRedemption>.Fail(StaffIdentity.SelfServiceError, 403);

        var active = await GetActiveAsync(userId, now, ct);
        if (active.Info == null)
        {
            return OpResult<PrivilegeRedemption>.Fail(active.Availability.Reason ?? "У гостя нет активной привилегии.", 409);
        }
        if (!active.Availability.Allowed)
        {
            return OpResult<PrivilegeRedemption>.Fail(active.Availability.Reason ?? "Привилегия сейчас недоступна.", 409);
        }

        var normalized = Codes.NormalizePrivilegeCode(code, out _);
        var window = PrivilegeCodes.MatchWindow(_codeSecret, userId, normalized, now);
        if (window == null)
        {
            return OpResult<PrivilegeRedemption>.Fail("Код неверный или устарел: попросите гостя показать код с экрана ещё раз.", 409);
        }
        // Код одноразовый: иначе подсмотренным кодом можно было бы выдать безлимитный кофе ещё раз
        if (await _db.PrivilegeRedemptions.AnyAsync(r => r.UserId == userId && r.CodeWindow >= window, ct))
        {
            return OpResult<PrivilegeRedemption>.Fail("Этот код уже использован: дождитесь нового кода на экране гостя.", 409);
        }

        var redemption = new PrivilegeRedemption
        {
            UserId = userId,
            Tier = active.Tier,
            WeekStart = PermTime.WeekStart(now),
            RankAtRedemption = active.Rank ?? 0,
            StaffMemberId = staff.StaffMemberId,
            StaffName = staff.Name,
            CodeWindow = window,
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
