using DieselRoom.Domain.Enums;

namespace DieselRoom.Domain.Entities;

/// <summary>Верхний промо-баннер вкладки «Афиша».</summary>
public class PromoBanner
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? LinkUrl { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Кнопка-плитка блока «Меню» (кухня, бар, банкет).</summary>
public class MenuLink
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    /// <summary>Ключ векторной иконки плитки: utensils, martini, wine.</summary>
    public string Icon { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    /// <summary>После этого момента (UTC) кнопка автоматически скрывается.</summary>
    public DateTime? HiddenAfter { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public bool IsVisibleAt(DateTime utcNow) => IsActive && (HiddenAfter == null || utcNow <= HiddenAfter.Value);
}

/// <summary>Карточка мероприятия «Афиши».</summary>
public class AfishaEvent
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>Короткая подпись: «Спецпредложение · 5 500 ₽», «Каждые выходные».</summary>
    public string? Tagline { get; set; }
    public string? Description { get; set; }
    public string? ProductUrl { get; set; }
    /// <summary>Подпись кнопки-ссылки: «Меню бизнес-ланча». Пусто — «Подробнее».</summary>
    public string? ProductLabel { get; set; }
    public string? ImageUrl { get; set; }
    /// <summary>Дата события (по Перми); подставляется в форму брони. Пусто — регулярное событие.</summary>
    public DateOnly? EventDate { get; set; }
    /// <summary>Время начала (по Перми).</summary>
    public TimeOnly? EventTime { get; set; }
    /// <summary>Показывать кнопку «Забронировать».</summary>
    public bool AllowBooking { get; set; } = true;
    public BookingFormat DefaultFormat { get; set; } = BookingFormat.Hall;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Бронь стола / караоке / мероприятия. Дата и время — местные (Пермь).</summary>
public class Booking
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly Time { get; set; }
    public int GuestsCount { get; set; }
    public BookingFormat Format { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Wishes { get; set; }
    public int? AfishaEventId { get; set; }
    /// <summary>Снимок названия события: сотрудник увидит его, даже если карточку афиши потом удалят.</summary>
    public string? EventTitle { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public string? StaffComment { get; set; }
    public string? ProcessedByStaffName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
