namespace DieselRoom.Domain.Entities;

/// <summary>Движение баллов кошелька и очков рейтинга (история в профиле).</summary>
public class PointsTransaction
{
    public int Id { get; set; }
    public int UserId { get; set; }
    /// <summary>Изменение баллов кошелька (отрицательное — покупка подарка).</summary>
    public int Coins { get; set; }
    /// <summary>Изменение очков рейтинга (всегда ≥ 0: покупки рейтинг не уменьшают).</summary>
    public int Rating { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class PointsTransactionTypes
{
    public const string VkLike = "VkLike";
    public const string VkComment = "VkComment";
    public const string VkRepost = "VkRepost";
    public const string Visit = "Visit";
    public const string Referral = "Referral";
    public const string ShopPurchase = "ShopPurchase";
    public const string ShopRefund = "ShopRefund";
    public const string ShopExpiredRefund = "ShopExpiredRefund";

    public static readonly string[] SocialTypes = [VkLike, VkComment, VkRepost];
}

/// <summary>Начисление за активность на стене группы ВК (уникально по VkId + PostId + ActivityType).</summary>
public class VkActivityLog
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string VkId { get; set; } = string.Empty;
    public string ActivityType { get; set; } = string.Empty;
    public int PostId { get; set; }
    /// <summary>Нормализованный текст оплаченного комментария (анти-копипаст).</summary>
    public string? CommentText { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
