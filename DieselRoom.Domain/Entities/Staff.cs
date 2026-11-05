using DieselRoom.Domain.Enums;

namespace DieselRoom.Domain.Entities;

/// <summary>Сотрудник (официант / бармен / хостес) или администратор. Вход по VK ID или по ПИН-коду.</summary>
public class StaffMember
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public StaffRole Role { get; set; } = StaffRole.Staff;
    public string? VkId { get; set; }
    /// <summary>PBKDF2-хэш ПИН-кода (соль внутри строки). Сам ПИН не хранится.</summary>
    public string? PinHash { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Журнал аудита персонала: кто, когда, кому и по какому чеку начислил визит или выдал подарок.</summary>
public class StaffAuditLog
{
    public long Id { get; set; }
    public int? StaffMemberId { get; set; }
    public string StaffName { get; set; } = string.Empty;
    public string? StaffVkId { get; set; }
    public string Action { get; set; } = string.Empty;
    public int? GuestUserId { get; set; }
    public string? GuestName { get; set; }
    public string? ReceiptNumber { get; set; }
    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class StaffAuditActions
{
    public const string VisitAccrued = "VisitAccrued";
    public const string GiftIssued = "GiftIssued";
    public const string PrivilegeIssued = "PrivilegeIssued";
    public const string BookingConfirmed = "BookingConfirmed";
    public const string BookingRejected = "BookingRejected";
    public const string BookingCompleted = "BookingCompleted";
    public const string SettingsChanged = "SettingsChanged";
    public const string StaffChanged = "StaffChanged";
    public const string CatalogChanged = "CatalogChanged";
    public const string AfishaChanged = "AfishaChanged";
    public const string GuestAdjusted = "GuestAdjusted";
}
