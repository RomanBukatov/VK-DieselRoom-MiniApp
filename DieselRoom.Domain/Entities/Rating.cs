using DieselRoom.Domain.Enums;

namespace DieselRoom.Domain.Entities;

/// <summary>Использование недельной привилегии ранга (журнал + проверка лимитов).</summary>
public class PrivilegeRedemption
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public PrivilegeTier Tier { get; set; }
    /// <summary>Понедельник недели гонки (по Перми), к которой относится выдача.</summary>
    public DateOnly WeekStart { get; set; }
    public int RankAtRedemption { get; set; }
    public int? StaffMemberId { get; set; }
    public string StaffName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Архив итогов недели: фиксируется перед обнулением WeeklyRating.</summary>
public class WeeklyRaceResult
{
    public int Id { get; set; }
    public DateOnly WeekStart { get; set; }
    public int UserId { get; set; }
    public int Rank { get; set; }
    public int WeeklyRating { get; set; }
    public PrivilegeTier Tier { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Лидер недельной гонки на конец дня (по Перми) — для трекера «Удержание 1-го места».</summary>
public class DailyLeaderSnapshot
{
    /// <summary>Местная дата (Пермь) — первичный ключ.</summary>
    public DateOnly Date { get; set; }
    public int UserId { get; set; }
    public int WeeklyRating { get; set; }
    /// <summary>Сколько дней подряд этот гость на 1-м месте (включая эту дату).</summary>
    public int StreakDays { get; set; }
    public bool StreakPrizeAwarded { get; set; }
}
