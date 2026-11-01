namespace DieselRoom.Domain.Entities;

public class User
{
    public int Id { get; set; }

    /// <summary>VK ID гостя (уникальный).</summary>
    public string VkId { get; set; } = string.Empty;

    /// <summary>Номер карты гостя (он же реферальный код в ссылке #ref=...). Уникальный.</summary>
    public string CardNumber { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public string? Phone { get; set; }

    /// <summary>Баллы кошелька: тратятся на подарки в магазине.</summary>
    public int DieselCoinsBalance { get; set; }
    public int TotalCoinsEarned { get; set; }

    /// <summary>Очки «Гонки недели»: обнуляются в воскресенье 23:59 по Перми. Покупки их НЕ уменьшают.</summary>
    public int WeeklyRating { get; set; }
    /// <summary>Когда изменились недельные очки — при равенстве очков выше тот, кто набрал их раньше.</summary>
    public DateTime? WeeklyRatingUpdatedAt { get; set; }

    /// <summary>Очки «За всё время»: не обнуляются и не уменьшаются при покупках.</summary>
    public int TotalRating { get; set; }

    /// <summary>Подтверждённые визиты по чекам (для достижений).</summary>
    public int VisitsCount { get; set; }

    /// <summary>Кто пригласил гостя (#ref=CardNumber).</summary>
    public int? ReferredByUserId { get; set; }
    /// <summary>Бонус пригласившему выплачен (после первого визита друга по чеку).</summary>
    public bool ReferralRewardPaid { get; set; }

    /// <summary>Последнее известное место в недельной гонке (для уведомлений о смещении).</summary>
    public int? LastKnownWeeklyRank { get; set; }
    /// <summary>Когда последний раз отправлено уведомление «Вас обошли» (не чаще 1 раза в 6 часов).</summary>
    public DateTime? LastRankNotifiedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    public string DisplayName => string.IsNullOrWhiteSpace(LastName) ? FirstName : $"{FirstName} {LastName}";
}
