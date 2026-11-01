using DieselRoom.Domain.Enums;

namespace DieselRoom.Domain.Services;

/// <summary>Описание привилегии тира (ТЗ п. 4.1).</summary>
/// <param name="TopRank">Лучшее место тира — до него нужно подняться, чтобы вернуть привилегию.</param>
/// <param name="BottomRank">Худшее место тира.</param>
/// <param name="Limit">Сколько раз можно получить за период; null — без ограничений, пока гость на этом месте.</param>
/// <param name="PeriodWeeks">Длина периода лимита в неделях гонки (1 или 2).</param>
public sealed record PrivilegeInfo(
    PrivilegeTier Tier,
    int TopRank,
    int BottomRank,
    string Emoji,
    string Title,
    string Conditions,
    int? Limit,
    int PeriodWeeks,
    DayOfWeek[]? AllowedDays = null,
    TimeOnly? From = null,
    TimeOnly? To = null)
{
    public string Label => $"{Emoji} {Title}";
}

/// <summary>Результат проверки «можно ли выдать привилегию сейчас».</summary>
public sealed record PrivilegeAvailability(bool Allowed, int Used, int? Limit, string? Reason);

public static class PrivilegeCatalog
{
    private static readonly DayOfWeek[] Weekdays =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    public static readonly IReadOnlyList<PrivilegeInfo> All =
    [
        new(PrivilegeTier.BusinessLunch, 1, 1, "🍱", "Бизнес-ланч бесплатно",
            "до 3 раз в неделю, Пн–Пт 12:00–16:00", 3, 1, Weekdays, new TimeOnly(12, 0), new TimeOnly(16, 0)),
        new(PrivilegeTier.UnlimitedCoffee, 2, 2, "☕", "Безлимитный кофе",
            "активен, пока вы на 2-м месте!", null, 1),
        new(PrivilegeTier.Hookah, 3, 3, "💨", "Бесплатный кальян",
            "1 раз в неделю, Пт/Сб", 1, 1, [DayOfWeek.Friday, DayOfWeek.Saturday]),
        new(PrivilegeTier.Dessert, 4, 10, "🍰", "Десерт от заведения", "1 раз в неделю", 1, 1),
        new(PrivilegeTier.BeerOrCoffee, 11, 20, "🍺", "Бокал пива 0.5 или кофе", "1 раз в неделю", 1, 1),
        new(PrivilegeTier.TeaPot, 21, 30, "🫖", "Чайник чая", "1 раз в неделю", 1, 1),
        new(PrivilegeTier.Cocktail, 31, 50, "🍹", "Коктейль от бара", "1 раз в 2 недели", 1, 2),
    ];

    /// <summary>Призовые места заканчиваются на 50-м.</summary>
    public const int LastPrizeRank = 50;

    /// <summary>Спецприз за удержание 1-го места 7 дней подряд.</summary>
    public const int FirstPlaceStreakTarget = 7;
    public const string FirstPlaceStreakPrizeTitle = "Бутылка Хаски 0.5";
    public const string FirstPlaceStreakPrizeIcon = "🥃";

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

    /// <summary>
    /// Проверяет лимит и окно времени. <paramref name="redemptionWeekStarts"/> — недели (понедельники) прошлых
    /// выдач ЭТОЙ привилегии этому гостю; <paramref name="localNow"/> — местное время Перми.
    /// </summary>
    public static PrivilegeAvailability CheckAvailability(
        PrivilegeTier tier, IEnumerable<DateOnly> redemptionWeekStarts, DateOnly currentWeekStart, DateTime localNow)
    {
        var info = Get(tier);
        if (info == null)
        {
            return new PrivilegeAvailability(false, 0, 0, "У гостя нет активной привилегии: он вне топ-50 недели.");
        }

        var periodStart = currentWeekStart.AddDays(-7 * (info.PeriodWeeks - 1));
        var used = redemptionWeekStarts.Count(w => w >= periodStart && w <= currentWeekStart);

        if (info.AllowedDays != null && !info.AllowedDays.Contains(localNow.DayOfWeek))
        {
            return new PrivilegeAvailability(false, used, info.Limit, $"{info.Label}: выдаётся только {info.Conditions}.");
        }

        if (info.From.HasValue && info.To.HasValue)
        {
            var time = TimeOnly.FromDateTime(localNow);
            if (time < info.From.Value || time >= info.To.Value)
            {
                return new PrivilegeAvailability(false, used, info.Limit, $"{info.Label}: выдаётся только {info.Conditions}.");
            }
        }

        if (info.Limit.HasValue && used >= info.Limit.Value)
        {
            var period = info.PeriodWeeks == 1 ? "на этой неделе" : "за последние 2 недели";
            return new PrivilegeAvailability(false, used, info.Limit, $"Лимит исчерпан: {used} из {info.Limit} {period}.");
        }

        return new PrivilegeAvailability(true, used, info.Limit, null);
    }
}
