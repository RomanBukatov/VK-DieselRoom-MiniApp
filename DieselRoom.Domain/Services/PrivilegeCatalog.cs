using DieselRoom.Domain.Enums;

namespace DieselRoom.Domain.Services;

/// <summary>Описание привилегии тира (ТЗ п. 4.1).</summary>
/// <param name="TopRank">Лучшее место тира — до него нужно подняться, чтобы вернуть привилегию.</param>
/// <param name="BottomRank">Худшее место тира.</param>
/// <param name="Schedule">Когда и как часто выдаётся.</param>
/// <param name="Goal">Цель в родительном падеже для уведомления «До … осталось X очков».</param>
/// <param name="Limit">Сколько раз можно получить за период; null — без ограничений, пока гость на этом месте.</param>
/// <param name="CooldownDays">Скользящее окно от прошлой выдачи в сутках; null — лимит считается по неделе гонки.</param>
/// <param name="Composition">Что входит в привилегию.</param>
/// <param name="AgeRestricted">Только для совершеннолетних: официант проверяет паспорт.</param>
public sealed record PrivilegeInfo(
    PrivilegeTier Tier,
    int TopRank,
    int BottomRank,
    string Icon,
    string Title,
    string Schedule,
    string Goal,
    int? Limit,
    int? CooldownDays = null,
    DayOfWeek[]? AllowedDays = null,
    TimeOnly? From = null,
    TimeOnly? To = null,
    string? Composition = null,
    bool AgeRestricted = false)
{
    public string Label => Title;
    public string Conditions => Composition == null ? Schedule : $"{Composition} · {Schedule}";
}

/// <summary>Результат проверки «можно ли выдать привилегию сейчас».</summary>
public sealed record PrivilegeAvailability(bool Allowed, int Used, int? Limit, string? Reason);

/// <summary>Прошлая выдача привилегии: неделя гонки и точный момент.</summary>
public sealed record PrivilegeStamp(DateOnly WeekStart, DateTime CreatedAtUtc);

public static class PrivilegeCatalog
{
    private static readonly DayOfWeek[] Weekdays =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    private static readonly DayOfWeek[] PartyDays = [DayOfWeek.Friday, DayOfWeek.Saturday];

    public static readonly IReadOnlyList<PrivilegeInfo> All =
    [
        new(PrivilegeTier.BusinessLunch, 1, 1, "lunch", "Бесплатный бизнес-ланч",
            "до 3 раз в неделю, Пн–Пт 12:00–16:00", "бесплатного бизнес-ланча (1-е место)", 3,
            AllowedDays: Weekdays, From: new TimeOnly(12, 0), To: new TimeOnly(16, 0),
            Composition: "суп, горячее, напиток, хлебная корзина"),
        new(PrivilegeTier.UnlimitedCoffee, 2, 2, "coffee", "Безлимитный кофе",
            "в любой день, пока вы на 2-м месте", "безлимитного кофе (Топ-2)", null),
        new(PrivilegeTier.Hookah, 3, 3, "hookah", "Классический кальян",
            "1 раз в неделю, Пт/Сб", "бесплатного кальяна (Топ-3)", 1, AllowedDays: PartyDays, AgeRestricted: true),
        new(PrivilegeTier.Dessert, 4, 10, "dessert", "Десерт в подарок", "1 раз в неделю", "десерта в подарок (Топ-10)", 1),
        new(PrivilegeTier.BeerOrCoffee, 11, 20, "beer", "Бокал разливного пива или кофе",
            "1 раз в неделю", "бокала пива или кофе (Топ-20)", 1, AgeRestricted: true),
        new(PrivilegeTier.TeaPot, 21, 30, "tea", "Чайник чая на выбор", "1 раз в неделю", "чайника чая (Топ-30)", 1),
        new(PrivilegeTier.Cocktail, 31, 50, "cocktail", "Бесплатный коктейль",
            "1 раз в 14 дней, на вечеринках Пт/Сб", "бесплатного коктейля (Топ-50)", 1,
            CooldownDays: 14, AllowedDays: PartyDays, AgeRestricted: true),
    ];

    /// <summary>Призовые места заканчиваются на 50-м.</summary>
    public const int LastPrizeRank = 50;

    /// <summary>Спецприз за удержание 1-го места 7 дней подряд.</summary>
    public const int FirstPlaceStreakTarget = 7;
    public const string FirstPlaceStreakPrizeTitle = "Бутылка Хаски 0.5";
    public const string FirstPlaceStreakPrizeIcon = "bottle";

    public static PrivilegeTier TierForRank(int? rank)
    {
        if (rank is null or < 1) return PrivilegeTier.None;
        foreach (var info in All)
        {
            if (rank >= info.TopRank && rank <= info.BottomRank) return info.Tier;
        }
        return PrivilegeTier.None;
    }

    public static PrivilegeInfo? Get(PrivilegeTier tier) => All.FirstOrDefault(p => p.Tier == tier);

    /// <summary>Ближайшая призовая зона выше места <paramref name="rank"/>; null — выше только небо (1-е место).</summary>
    public static PrivilegeInfo? NextTierAbove(int rank) =>
        All.Where(p => p.BottomRank < rank).MaxBy(p => p.BottomRank);

    /// <summary>
    /// Проверяет лимит и окно времени. <paramref name="redemptions"/> — прошлые выдачи ЭТОЙ привилегии этому гостю;
    /// <paramref name="utcNow"/> — текущий момент, дни и часы сверяются по времени Перми.
    /// </summary>
    public static PrivilegeAvailability CheckAvailability(
        PrivilegeTier tier, IEnumerable<PrivilegeStamp> redemptions, DateOnly currentWeekStart, DateTime utcNow)
    {
        var info = Get(tier);
        if (info == null)
        {
            return new PrivilegeAvailability(false, 0, 0, "У гостя нет активной привилегии: он вне топ-50 недели.");
        }

        var stamps = redemptions.ToList();
        var localNow = PermTime.ToLocal(utcNow);
        var cooldown = info.CooldownDays is { } days ? TimeSpan.FromDays(days) : (TimeSpan?)null;
        var used = cooldown is { } window
            ? stamps.Count(s => utcNow - s.CreatedAtUtc < window)
            : stamps.Count(s => s.WeekStart == currentWeekStart);

        if (info.AllowedDays != null && !info.AllowedDays.Contains(localNow.DayOfWeek))
        {
            return new PrivilegeAvailability(false, used, info.Limit, $"{info.Label}: выдаётся только {info.Schedule}.");
        }

        if (info.From.HasValue && info.To.HasValue)
        {
            var time = TimeOnly.FromDateTime(localNow);
            if (time < info.From.Value || time >= info.To.Value)
            {
                return new PrivilegeAvailability(false, used, info.Limit, $"{info.Label}: выдаётся только {info.Schedule}.");
            }
        }

        if (info.Limit.HasValue && used >= info.Limit.Value)
        {
            if (cooldown is { } span)
            {
                var next = stamps.Max(s => s.CreatedAtUtc) + span;
                return new PrivilegeAvailability(false, used, info.Limit,
                    $"Лимит исчерпан: {info.Label.ToLowerInvariant()} снова доступен после {PermTime.ToLocal(next):dd.MM HH:mm}.");
            }
            return new PrivilegeAvailability(false, used, info.Limit, $"Лимит исчерпан: {used} из {info.Limit} на этой неделе.");
        }

        return new PrivilegeAvailability(true, used, info.Limit, null);
    }
}
