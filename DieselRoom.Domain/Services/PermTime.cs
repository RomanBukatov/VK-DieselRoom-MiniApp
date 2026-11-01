namespace DieselRoom.Domain.Services;

/// <summary>
/// Время заведения: Пермь, UTC+5 круглый год (переходов на летнее время нет). Все «местные» правила —
/// бизнес-ланч Пн–Пт 12:00–16:00, кальян Пт/Сб, сброс недели в воскресенье 23:59 — считаются здесь.
/// </summary>
public static class PermTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(5);

    /// <summary>Сброс «Гонки недели»: воскресенье 23:59 по Перми (= 18:59 UTC).</summary>
    public static readonly TimeOnly WeeklyResetLocalTime = new(23, 59);

    public static DateTime ToLocal(DateTime utc) => DateTime.SpecifyKind(AsUtc(utc) + Offset, DateTimeKind.Unspecified);

    public static DateTime ToUtc(DateTime local) => DateTime.SpecifyKind(DateTime.SpecifyKind(local, DateTimeKind.Unspecified) - Offset, DateTimeKind.Utc);

    public static DateTime ToUtc(DateOnly date, TimeOnly time) => ToUtc(date.ToDateTime(time));

    public static DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));

    /// <summary>Понедельник недели гонки, к которой относится момент <paramref name="utc"/>.</summary>
    /// <remarks>Неделя гонки длится с понедельника 00:00 до воскресенья 23:59; последняя минута воскресенья
    /// (после сброса) уже относится к следующей неделе.</remarks>
    public static DateOnly WeekStart(DateTime utc)
    {
        var local = ToLocal(utc);
        var date = DateOnly.FromDateTime(local);
        if (date.DayOfWeek == DayOfWeek.Sunday && TimeOnly.FromDateTime(local) >= WeeklyResetLocalTime)
        {
            return date.AddDays(1);
        }

        var daysFromMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-daysFromMonday);
    }

    /// <summary>Момент сброса (UTC), который завершает неделю, начатую в <paramref name="weekStart"/>.</summary>
    public static DateTime ResetMomentForWeek(DateOnly weekStart) => ToUtc(weekStart.AddDays(6), WeeklyResetLocalTime);

    /// <summary>Ближайший сброс недели строго после <paramref name="utcNow"/>.</summary>
    public static DateTime NextWeeklyReset(DateTime utcNow)
    {
        var moment = ResetMomentForWeek(WeekStart(utcNow));
        return moment > AsUtc(utcNow) ? moment : ResetMomentForWeek(WeekStart(utcNow).AddDays(7));
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
