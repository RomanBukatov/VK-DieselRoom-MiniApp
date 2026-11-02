using DieselRoom.Domain.Entities;
using DieselRoom.Domain.Enums;
using DieselRoom.Domain.Services;
using DieselRoom.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DieselRoom.Infrastructure.Services;

public sealed record RankedGuest(int Rank, int UserId, string VkId, string Name, string? PhotoUrl, string CardNumber,
    int Rating, PrivilegeTier Tier);

public sealed record DisplacementNotice(int UserId, string VkId, int OldRank, int NewRank, int PointsToReturn, string Message);

/// <summary>
/// «Гонка недели» и «За всё время»: места, переходящие привилегии (п. 4.1), трекер удержания 1-го места,
/// уведомления о смещении из призового тира и еженедельный сброс (воскресенье 23:59 по Перми).
/// </summary>
public class RatingService
{
    public static readonly TimeSpan DisplacementNotifyCooldown = TimeSpan.FromHours(6);
    /// <summary>Сколько мест за пределами призовой зоны пересчитываем для уведомлений о смещении.</summary>
    private const int TrackedRanks = PrivilegeCatalog.LastPrizeRank + 30;

    private readonly AppDbContext _db;
    private readonly ShopService _shop;
    private readonly IGuestNotifier _notifier;
    private readonly SettingsService _settings;
    private readonly ILogger<RatingService> _logger;

    public RatingService(AppDbContext db, ShopService shop, IGuestNotifier notifier, SettingsService settings, ILogger<RatingService> logger)
    {
        _db = db;
        _shop = shop;
        _notifier = notifier;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Порядок недельной гонки: очки ↓, кто раньше набрал ↑, Id ↑. Без очков в гонке не участвуют.</summary>
    private IQueryable<User> WeeklyOrdered() => _db.Users.AsNoTracking()
        .Where(u => u.WeeklyRating > 0)
        .OrderByDescending(u => u.WeeklyRating)
        .ThenBy(u => u.WeeklyRatingUpdatedAt)
        .ThenBy(u => u.Id);

    public async Task<List<RankedGuest>> GetWeeklyTopAsync(int take, CancellationToken ct = default)
    {
        var users = await WeeklyOrdered().Take(take).ToListAsync(ct);
        return users.Select((u, i) => ToRanked(u, i + 1, u.WeeklyRating, PrivilegeCatalog.TierForRank(i + 1))).ToList();
    }

    public async Task<List<RankedGuest>> GetTotalTopAsync(int take, CancellationToken ct = default)
    {
        var users = await _db.Users.AsNoTracking()
            .Where(u => u.TotalRating > 0)
            .OrderByDescending(u => u.TotalRating)
            .ThenBy(u => u.Id)
            .Take(take)
            .ToListAsync(ct);
        return users.Select((u, i) => ToRanked(u, i + 1, u.TotalRating, PrivilegeTier.None)).ToList();
    }

    /// <summary>Текущее место гостя в недельной гонке (null — нет очков на этой неделе).</summary>
    public async Task<int?> GetWeeklyRankAsync(int userId, CancellationToken ct = default)
    {
        var me = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (me == null || me.WeeklyRating <= 0) return null;

        var at = me.WeeklyRatingUpdatedAt;
        var ahead = await _db.Users.CountAsync(u => u.WeeklyRating > 0 && (
            u.WeeklyRating > me.WeeklyRating ||
            (u.WeeklyRating == me.WeeklyRating && (
                (at != null && u.WeeklyRatingUpdatedAt < at) ||
                (u.WeeklyRatingUpdatedAt == at && u.Id < me.Id)))), ct);
        return ahead + 1;
    }

    public async Task<int?> GetTotalRankAsync(int userId, CancellationToken ct = default)
    {
        var me = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (me == null || me.TotalRating <= 0) return null;
        var ahead = await _db.Users.CountAsync(u => u.TotalRating > me.TotalRating || (u.TotalRating == me.TotalRating && u.Id < me.Id), ct);
        return ahead + 1;
    }

    /// <summary>
    /// «Удержание 1-го места: X / 7 дней подряд». Сегодняшний день засчитывается, пока гость держит 1-е место.
    /// </summary>
    public async Task<int> GetFirstPlaceStreakAsync(int userId, DateTime now, CancellationToken ct = default)
    {
        var leader = await WeeklyOrdered().Select(u => (int?)u.Id).FirstOrDefaultAsync(ct);
        if (leader != userId) return 0;

        var today = PermTime.LocalDate(now);
        var last = await _db.DailyLeaderSnapshots.AsNoTracking()
            .Where(s => s.Date <= today)
            .OrderByDescending(s => s.Date)
            .FirstOrDefaultAsync(ct);

        if (last == null || last.UserId != userId || last.StreakPrizeAwarded) return last?.Date == today ? 0 : 1;
        if (last.Date == today) return last.StreakDays;
        return last.Date == today.AddDays(-1) ? last.StreakDays + 1 : 1;
    }

    /// <summary>
    /// Фиксирует лидера дня (в 23:59 по Перми, до недельного сброса). На 7-й день подряд — спецприз
    /// «Бутылка Хаски 0.5» купоном, который гасит официант. Повторный вызов за ту же дату ничего не делает.
    /// </summary>
    public async Task<DailyLeaderSnapshot?> TakeDailySnapshotAsync(DateTime now, CancellationToken ct = default)
    {
        var today = PermTime.LocalDate(now);
        if (await _db.DailyLeaderSnapshots.AnyAsync(s => s.Date == today, ct)) return null;

        var leader = await WeeklyOrdered().FirstOrDefaultAsync(ct);
        if (leader == null) return null;

        var previous = await _db.DailyLeaderSnapshots.AsNoTracking().FirstOrDefaultAsync(s => s.Date == today.AddDays(-1), ct);
        var streak = previous != null && previous.UserId == leader.Id && !previous.StreakPrizeAwarded ? previous.StreakDays + 1 : 1;

        var snapshot = new DailyLeaderSnapshot
        {
            Date = today,
            UserId = leader.Id,
            WeeklyRating = leader.WeeklyRating,
            StreakDays = streak,
            StreakPrizeAwarded = streak >= PrivilegeCatalog.FirstPlaceStreakTarget,
        };

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        _db.DailyLeaderSnapshots.Add(snapshot);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();
            return null; // другой экземпляр воркера уже снял срез
        }

        ShopOrder? prize = null;
        if (snapshot.StreakPrizeAwarded)
        {
            prize = await _shop.CreatePrizeOrderAsync(leader.Id,
                PrivilegeCatalog.FirstPlaceStreakPrizeTitle, PrivilegeCatalog.FirstPlaceStreakPrizeIcon, now, ct);
        }
        await tx.CommitAsync(ct);

        if (prize != null)
        {
            _logger.LogInformation("[RATING_AUDIT] Action=StreakPrize UserId={UserId} Code={Code}", leader.Id, prize.Code);
            _notifier.Send(leader.VkId,
                $"Легенда! Вы удержали 1-е место в Гонке недели DIESEL ROOM {PrivilegeCatalog.FirstPlaceStreakTarget} дней подряд.\n" +
                $"Ваш спецприз «{PrivilegeCatalog.FirstPlaceStreakPrizeTitle}» ждёт в профиле → «Мои подарки» (купон #{prize.Code}).",
                "спецприз за 7 дней на 1-м месте");
        }

        return snapshot;
    }

    /// <summary>
    /// Сравнивает текущие места с последними известными и готовит уведомления тем, кто выпал из своего призового тира
    /// (не чаще 1 раза в 6 часов на гостя). Сохраняет новые места. Отправка — на вызывающем.
    /// </summary>
    public async Task<List<DisplacementNotice>> DetectDisplacementsAsync(DateTime now, CancellationToken ct = default)
    {
        var top = await GetWeeklyTopAsync(TrackedRanks, ct);
        var currentRank = top.ToDictionary(r => r.UserId, r => r.Rank);
        var topIds = top.Where(r => r.Rank <= PrivilegeCatalog.LastPrizeRank).Select(r => r.UserId).ToList();

        var tracked = await _db.Users.AsNoTracking()
            .Where(u => u.LastKnownWeeklyRank != null || topIds.Contains(u.Id))
            .Select(u => new { u.Id, u.VkId, u.WeeklyRating, u.LastKnownWeeklyRank, u.LastRankNotifiedAt })
            .ToListAsync(ct);

        var notices = new List<DisplacementNotice>();
        foreach (var guest in tracked)
        {
            int? newRank = currentRank.TryGetValue(guest.Id, out var r) ? r
                : guest.WeeklyRating > 0 ? await GetWeeklyRankAsync(guest.Id, ct) : null;
            var storedRank = newRank is <= PrivilegeCatalog.LastPrizeRank ? newRank : null;

            var oldTier = PrivilegeCatalog.TierForRank(guest.LastKnownWeeklyRank);
            var newTier = PrivilegeCatalog.TierForRank(newRank);
            var droppedOutOfTier = oldTier != PrivilegeTier.None && newRank.HasValue &&
                                   newRank > guest.LastKnownWeeklyRank && (newTier == PrivilegeTier.None || newTier > oldTier);
            var cooledDown = guest.LastRankNotifiedAt == null || now - guest.LastRankNotifiedAt.Value >= DisplacementNotifyCooldown;

            if (droppedOutOfTier && cooledDown)
            {
                // Чтобы вернуть привилегию, нужно обойти гостя, который сейчас стоит на последнем месте старого тира
                var targetRank = PrivilegeCatalog.Get(oldTier)!.BottomRank;
                var target = top.FirstOrDefault(t => t.Rank == targetRank);
                var diff = Math.Max(1, (target?.Rating ?? guest.WeeklyRating) - guest.WeeklyRating + 1);
                notices.Add(new DisplacementNotice(guest.Id, guest.VkId, guest.LastKnownWeeklyRank!.Value, newRank!.Value, diff,
                    BuildDisplacementMessage(newRank.Value, diff)));

                await _db.Users.Where(u => u.Id == guest.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.LastKnownWeeklyRank, storedRank)
                    .SetProperty(u => u.LastRankNotifiedAt, now), ct);
            }
            else if (guest.LastKnownWeeklyRank != storedRank)
            {
                await _db.Users.Where(u => u.Id == guest.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.LastKnownWeeklyRank, storedRank), ct);
            }
        }

        return notices;
    }

    public static string BuildDisplacementMessage(int newRank, int diff) =>
        $"Вас обошли в недельном рейтинге Diesel Room! Вы переместились на #{newRank} место. " +
        $"Наберите ещё {diff} очков, чтобы вернуть свою привилегию!";

    /// <summary>
    /// Еженедельный сброс: если прошедшая неделя ещё не закрыта — архивирует её итоги и обнуляет WeeklyRating
    /// (TotalRating и баланс баллов сохраняются; лимиты привилегий привязаны к неделе и обнуляются сами).
    /// При первом запуске на пустой базе просто запоминает текущую неделю. Возвращает true, если сброс выполнен.
    /// </summary>
    public async Task<bool> RunWeeklyResetIfDueAsync(DateTime now, CancellationToken ct = default)
    {
        var currentWeek = PermTime.WeekStart(now);
        var weekToClose = currentWeek.AddDays(-7);

        var raw = await _settings.GetAsync(SettingsService.LastClosedWeekKey, ct);
        if (!DateOnly.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var lastClosed))
        {
            await _settings.SetAsync(SettingsService.LastClosedWeekKey, weekToClose.ToString("yyyy-MM-dd"), ct);
            return false;
        }

        if (lastClosed >= weekToClose) return false;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var standings = await WeeklyOrdered().Select(u => new { u.Id, u.WeeklyRating }).ToListAsync(ct);
        for (var i = 0; i < standings.Count; i++)
        {
            _db.WeeklyRaceResults.Add(new WeeklyRaceResult
            {
                WeekStart = weekToClose,
                UserId = standings[i].Id,
                Rank = i + 1,
                WeeklyRating = standings[i].WeeklyRating,
                Tier = PrivilegeCatalog.TierForRank(i + 1),
                CreatedAt = now,
            });
        }
        await _db.SaveChangesAsync(ct);

        await _db.Users
            .Where(u => u.WeeklyRating != 0 || u.LastKnownWeeklyRank != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.WeeklyRating, 0)
                .SetProperty(u => u.WeeklyRatingUpdatedAt, (DateTime?)null)
                .SetProperty(u => u.LastKnownWeeklyRank, (int?)null), ct);

        await _settings.SetAsync(SettingsService.LastClosedWeekKey, weekToClose.ToString("yyyy-MM-dd"), ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("[RATING_AUDIT] Action=WeeklyReset Week={Week} Archived={Count}", weekToClose, standings.Count);
        return true;
    }

    private static RankedGuest ToRanked(User u, int rank, int rating, PrivilegeTier tier) =>
        new(rank, u.Id, u.VkId, u.DisplayName, u.PhotoUrl, u.CardNumber, rating, tier);
}
