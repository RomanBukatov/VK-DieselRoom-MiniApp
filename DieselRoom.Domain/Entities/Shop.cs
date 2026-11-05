using DieselRoom.Domain.Enums;

namespace DieselRoom.Domain.Entities;

public class ShopItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Ключ векторной иконки (lucide) для карточки без фото: coffee, beer, pizza, burger…</summary>
    public string Icon { get; set; } = "gift";
    /// <summary>Превью блюда (оптимизированный WebP из /prizes/); null — брендированная заглушка с иконкой.</summary>
    public string? ImageUrl { get; set; }
    public int PriceCoins { get; set; }
    /// <summary>Остаток на складе. Покупка резервирует −1, отмена / истечение купона возвращает +1.</summary>
    public int StockCount { get; set; }
    /// <summary>Подарок только для совершеннолетних: алкоголь или кальян. Официант проверяет паспорт при выдаче.</summary>
    public bool AgeRestricted { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Фото подарка, загруженное администратором. Отдаётся по адресу /api/media/{Id}.</summary>
public class MediaFile
{
    public int Id { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public byte[] Bytes { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Купон на подарок: код #DR-XXXX, QR рисует клиент, срок действия 7 дней.</summary>
public class ShopOrder
{
    public int Id { get; set; }
    public int UserId { get; set; }
    /// <summary>Товар магазина; null — спецприз (например, за 7 дней подряд на 1-м месте).</summary>
    public int? ShopItemId { get; set; }
    /// <summary>Код без «#»: DR-XXXX (уникальный).</summary>
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Icon { get; set; } = "gift";
    public string? ImageUrl { get; set; }
    public int PriceCoins { get; set; }
    /// <summary>Снимок признака 18+ на момент обмена: официант увидит напоминание, даже если товар потом изменят.</summary>
    public bool AgeRestricted { get; set; }
    public ShopOrderStatus Status { get; set; } = ShopOrderStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? IssuedAt { get; set; }
    public string? IssuedByStaffName { get; set; }
    public DateTime? ClosedAt { get; set; }
}

/// <summary>Подтверждённый визит по закрытому чеку. ReceiptNumber — УНИКАЛЬНЫЙ индекс: 1 чек = 1 раз.</summary>
public class Visit
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public VisitType Type { get; set; }
    public int CoinsAwarded { get; set; }
    public int RatingAwarded { get; set; }
    public int? StaffMemberId { get; set; }
    public string StaffName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
