using DieselRoom.Domain.Entities;
using DieselRoom.Domain.Enums;
using DieselRoom.Domain.Services;
using DieselRoom.Infrastructure.Services;

namespace DieselRoom.Api.DTOs;

public sealed record PrivilegeDto(
    PrivilegeTier Tier, string? Icon, string? Title, string? Conditions, string? Label,
    int? Rank, bool Allowed, int Used, int? Limit, string? Reason, int FirstPlaceStreak, int StreakTarget)
{
    public static PrivilegeDto From(ActivePrivilege p) => new(
        p.Tier, p.Info?.Icon, p.Info?.Title, p.Info?.Conditions, p.Info?.Label,
        p.Rank, p.Availability.Allowed, p.Availability.Used, p.Availability.Limit, p.Availability.Reason,
        p.FirstPlaceStreak, PrivilegeCatalog.FirstPlaceStreakTarget);
}

public sealed record ReferralInfoDto(string Code, int InvitedCount, int RewardedCount, int RewardCoins, int RewardRating);

public sealed record ProfileDto(
    int Id, string VkId, string CardNumber, string FirstName, string LastName, string? PhotoUrl, string? Phone,
    int Coins, int TotalCoinsEarned, int WeeklyRating, int TotalRating, int? WeeklyRank, int? TotalRank, int VisitsCount,
    PrivilegeDto Privilege, IReadOnlyList<Achievement> Achievements, ReferralInfoDto Referral,
    StaffRole Role, string? StaffName, DateTime NextWeeklyResetAt);

public sealed record PointsTransactionDto(int Id, int Coins, int Rating, string Type, string Description, DateTime CreatedAt);

public sealed record LeaderboardRowDto(int Rank, int UserId, string Name, string? PhotoUrl, int Rating,
    PrivilegeTier Tier, string? PrivilegeLabel, bool IsMe);

public sealed record LeaderboardDto(string Kind, IReadOnlyList<LeaderboardRowDto> Rows, LeaderboardRowDto? Me,
    DateOnly WeekStart, DateTime NextResetAt, IReadOnlyList<PrivilegeInfoDto> Tiers);

public sealed record PrivilegeInfoDto(PrivilegeTier Tier, int TopRank, int BottomRank, string Icon, string Title, string Conditions)
{
    public static PrivilegeInfoDto From(PrivilegeInfo i) => new(i.Tier, i.TopRank, i.BottomRank, i.Icon, i.Title, i.Conditions);
}

public sealed record ShopItemDto(int Id, string Title, string? Description, string Icon, string? ImageUrl, int PriceCoins, int StockCount, bool IsActive, int SortOrder)
{
    public static ShopItemDto From(ShopItem i) => new(i.Id, i.Title, i.Description, i.Icon, i.ImageUrl, i.PriceCoins, i.StockCount, i.IsActive, i.SortOrder);
}

public sealed record ShopOrderDto(int Id, string Code, string Title, string Icon, string? ImageUrl, int PriceCoins, ShopOrderStatus Status,
    DateTime CreatedAt, DateTime ExpiresAt, DateTime? IssuedAt, DateTime? ClosedAt, bool IsPrize)
{
    public static ShopOrderDto From(ShopOrder o) => new(o.Id, o.Code, o.Title, o.Icon, o.ImageUrl, o.PriceCoins, o.Status,
        o.CreatedAt, o.ExpiresAt, o.IssuedAt, o.ClosedAt, o.ShopItemId == null);
}

public sealed record PurchaseResultDto(ShopOrderDto Order, int Coins);

public sealed record BannerDto(int Id, string Title, string? Subtitle, string? LinkUrl, string? ImageUrl, bool IsActive, int SortOrder)
{
    public static BannerDto From(PromoBanner b) => new(b.Id, b.Title, b.Subtitle, b.LinkUrl, b.ImageUrl, b.IsActive, b.SortOrder);
}

public sealed record MenuLinkDto(int Id, string Key, string Icon, string Title, string Url, DateTime? HiddenAfter, bool IsActive, int SortOrder)
{
    public static MenuLinkDto From(MenuLink m) => new(m.Id, m.Key, m.Icon, m.Title, m.Url, m.HiddenAfter, m.IsActive, m.SortOrder);
}

public sealed record AfishaEventDto(int Id, string Title, string? Tagline, string? Description, string? ProductUrl, string? ImageUrl,
    bool AllowBooking, BookingFormat DefaultFormat, bool IsActive, int SortOrder)
{
    public static AfishaEventDto From(AfishaEvent e) => new(e.Id, e.Title, e.Tagline, e.Description, e.ProductUrl, e.ImageUrl,
        e.AllowBooking, e.DefaultFormat, e.IsActive, e.SortOrder);
}

public sealed record AfishaDto(IReadOnlyList<BannerDto> Banners, IReadOnlyList<MenuLinkDto> Menu, IReadOnlyList<AfishaEventDto> Events);

public sealed record BookingDto(int Id, int UserId, DateOnly Date, TimeOnly Time, int GuestsCount, BookingFormat Format,
    string Name, string Phone, string? Wishes, int? AfishaEventId, BookingStatus Status, string StatusLabel,
    string? StaffComment, string? ProcessedByStaffName, DateTime CreatedAt)
{
    public static BookingDto From(Booking b) => new(b.Id, b.UserId, b.Date, b.Time, b.GuestsCount, b.Format, b.Name, b.Phone,
        b.Wishes, b.AfishaEventId, b.Status, BookingService.StatusLabel(b.Status), b.StaffComment, b.ProcessedByStaffName, b.CreatedAt);
}

public sealed record GuestCardDto(int UserId, string Name, string CardNumber, string? Phone, string? PhotoUrl,
    int Coins, int WeeklyRating, int VisitsCount, PrivilegeDto Privilege);

public sealed record StaffOrderDto(ShopOrderDto Order, int UserId, string GuestName, string CardNumber);

public sealed record StaffLookupDto(StaffOrderDto? Order, IReadOnlyList<GuestCardDto> Guests);

public sealed record VisitResultDto(int VisitId, string ReceiptNumber, int CoinsAwarded, int RatingAwarded,
    GuestCardDto Guest, bool ReferralRewardPaid, string? InviterName);

public sealed record StaffAuditDto(long Id, string StaffName, string Action, int? GuestUserId, string? GuestName,
    string? ReceiptNumber, string? Details, DateTime CreatedAt)
{
    public static StaffAuditDto From(StaffAuditLog a) => new(a.Id, a.StaffName, a.Action, a.GuestUserId, a.GuestName, a.ReceiptNumber, a.Details, a.CreatedAt);
}

public sealed record StaffMemberDto(int Id, string Name, StaffRole Role, string? VkId, bool HasPin, bool IsActive, DateTime CreatedAt)
{
    public static StaffMemberDto From(StaffMember s) => new(s.Id, s.Name, s.Role, s.VkId, s.PinHash != null, s.IsActive, s.CreatedAt);
}

public sealed record StaffSessionDto(StaffRole Role, string Name);

// ---------- Запросы ----------

public sealed record SyncProfileRequest(string? FirstName, string? LastName, string? PhotoUrl, string? ReferralCode);

public sealed record CreateBookingRequest(string? Date, string? Time, int GuestsCount, BookingFormat Format,
    string? Name, string? Phone, string? Wishes, int? AfishaEventId);

public sealed record StaffDecisionRequest(string? Comment);

public sealed record IssueCouponRequest(string? Code);

public sealed record RedeemPrivilegeRequest(int UserId);

public sealed record AccrueVisitRequest(int UserId, string? ReceiptNumber, VisitType Type);

public sealed record UpsertBannerRequest(string? Title, string? Subtitle, string? LinkUrl, string? ImageUrl, bool IsActive, int SortOrder);

public sealed record UpdateMenuLinkRequest(string? Icon, string? Title, string? Url, DateTime? HiddenAfter, bool IsActive, int SortOrder);

public sealed record UpsertAfishaEventRequest(string? Title, string? Tagline, string? Description, string? ProductUrl, string? ImageUrl,
    bool AllowBooking, BookingFormat DefaultFormat, bool IsActive, int SortOrder);

public sealed record UpsertShopItemRequest(string? Title, string? Description, string? Icon, string? ImageUrl, int PriceCoins, int StockCount, bool IsActive, int SortOrder);

public sealed record UpsertStaffMemberRequest(string? Name, StaffRole Role, string? VkId, string? Pin, bool IsActive);
