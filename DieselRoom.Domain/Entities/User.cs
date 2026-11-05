using DieselRoom.Domain.Enums;

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

    /// <summary>Лучшая цель, о приближении к которой гость уже предупреждён на этой неделе (обнуляется при сбросе).</summary>
    public PrivilegeTier? LastApproachTier { get; set; }
    /// <summary>Когда отправлено последнее уведомление «До приза осталось X очков».</summary>
    public DateTime? LastApproachAt { get; set; }

    /// <summary>Когда гость принял условия программы и согласие на обработку персональных данных (152-ФЗ).</summary>
    public DateTime? ConsentAcceptedAt { get; set; }
    /// <summary>Какую редакцию условий принял гость.</summary>
    public string? ConsentVersion { get; set; }
    /// <summary>Когда гость подтвердил, что ему исполнилось 18 лет.</summary>
    public DateTime? AdultConfirmedAt { get; set; }
    /// <summary>Гость сам заполнил профиль: имя из ВК больше не перезаписывает то, что он указал.</summary>
    public DateTime? ProfileCompletedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    public string DisplayName => string.IsNullOrWhiteSpace(LastName) ? FirstName : $"{FirstName} {LastName}";
}
