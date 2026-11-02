using System.Globalization;
using DieselRoom.Api.DTOs;
using DieselRoom.Api.Extensions;
using DieselRoom.Api.Filters;
using DieselRoom.Api.Services;
using DieselRoom.Domain.Entities;
using DieselRoom.Domain.Enums;
using DieselRoom.Domain.Services;
using DieselRoom.Infrastructure.Data;
using DieselRoom.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DieselRoom.Api.Controllers;

/// <summary>Общая база: текущий гость (создаётся при первом обращении) и единые часы.</summary>
public abstract class GuestControllerBase : ControllerBase
{
    protected readonly AppDbContext Db;
    protected readonly UserService Users;
    protected readonly TimeProvider Clock;

    protected GuestControllerBase(AppDbContext db, UserService users, TimeProvider clock)
    {
        Db = db;
        Users = users;
        Clock = clock;
    }

    protected DateTime Now => Clock.GetUtcNow().UtcDateTime;

    protected async Task<User> CurrentUserAsync(CancellationToken ct)
    {
        var vkId = HttpContext.GetVkUserId() ?? throw new InvalidOperationException("Запрос без проверенного VK ID.");
        return await Users.FindByVkIdAsync(vkId, ct) ?? await Users.SyncAsync(vkId, new GuestSyncRequest(null, null, null, null), Now, ct);
    }

    protected ObjectResult Error(string? message, int status) => StatusCode(status, new { message });
}

[ApiController]
[Route("api/users")]
[VkAuthorize]
public class UsersController : GuestControllerBase
{
    private readonly ProfileBuilder _profiles;

    public UsersController(AppDbContext db, UserService users, TimeProvider clock, ProfileBuilder profiles) : base(db, users, clock)
    {
        _profiles = profiles;
    }

    /// <summary>Первое открытие / обновление имени и аватара; здесь же привязывается промокод друга (#ref=...).</summary>
    [HttpPost("me")]
    public async Task<ActionResult<ProfileDto>> Sync([FromBody] SyncProfileRequest? request, CancellationToken ct)
    {
        var vkId = HttpContext.GetVkUserId()!;
        var user = await Users.SyncAsync(vkId,
            new GuestSyncRequest(request?.FirstName, request?.LastName, request?.PhotoUrl, request?.ReferralCode), Now, ct);
        return await _profiles.BuildAsync(user, HttpContext.GetStaff(), Now, ct);
    }

    [HttpGet("me")]
    public async Task<ActionResult<ProfileDto>> Me(CancellationToken ct)
    {
        var user = await CurrentUserAsync(ct);
        return await _profiles.BuildAsync(user, HttpContext.GetStaff(), Now, ct);
    }

    [HttpGet("me/transactions")]
    public async Task<ActionResult<IEnumerable<PointsTransactionDto>>> Transactions(CancellationToken ct)
    {
        var user = await CurrentUserAsync(ct);
        return await Db.PointsTransactions.AsNoTracking()
            .Where(t => t.UserId == user.Id)
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Take(100)
            .Select(t => new PointsTransactionDto(t.Id, t.Coins, t.Rating, t.Type, t.Description, t.CreatedAt))
            .ToListAsync(ct);
    }

    /// <summary>Приглашённые друзья: имя и выплачен ли бонус (после первого визита друга).</summary>
    [HttpGet("me/referrals")]
    public async Task<ActionResult> Referrals(CancellationToken ct)
    {
        var user = await CurrentUserAsync(ct);
        var friends = await Db.Users.AsNoTracking()
            .Where(u => u.ReferredByUserId == user.Id)
            .OrderByDescending(u => u.CreatedAt)
            .Take(100)
            .Select(u => new { name = u.FirstName, rewardPaid = u.ReferralRewardPaid, joinedAt = u.CreatedAt })
            .ToListAsync(ct);
        return Ok(friends);
    }
}

[ApiController]
[Route("api/afisha")]
[VkAuthorize]
public class AfishaController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    public AfishaController(AppDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>Баннер, плитки меню (банкетное скрывается после 31.12.2026 23:59:59) и карточки мероприятий.</summary>
    [HttpGet]
    public async Task<ActionResult<AfishaDto>> Get(CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var banners = await _db.PromoBanners.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.SortOrder).ThenBy(b => b.Id).ToListAsync(ct);
        var menu = await _db.MenuLinks.AsNoTracking().OrderBy(m => m.SortOrder).ThenBy(m => m.Id).ToListAsync(ct);
        var events = await _db.AfishaEvents.AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.SortOrder).ThenBy(e => e.Id).ToListAsync(ct);

        return new AfishaDto(
            banners.Select(BannerDto.From).ToList(),
            menu.Where(m => m.IsVisibleAt(now)).Select(MenuLinkDto.From).ToList(),
            events.Select(AfishaEventDto.From).ToList());
    }
}

[ApiController]
[Route("api/bookings")]
[VkAuthorize]
public class BookingsController : GuestControllerBase
{
    private readonly BookingService _bookings;

    public BookingsController(AppDbContext db, UserService users, TimeProvider clock, BookingService bookings) : base(db, users, clock)
    {
        _bookings = bookings;
    }

    [HttpPost]
    public async Task<ActionResult<BookingDto>> Create([FromBody] CreateBookingRequest request, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return Error("Укажите дату брони.", 400);
        if (!TimeOnly.TryParseExact(request.Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return Error("Укажите время брони.", 400);

        var user = await CurrentUserAsync(ct);
        var result = await _bookings.CreateAsync(user.Id,
            new BookingRequest(date, time, request.GuestsCount, request.Format, request.Name, request.Phone, request.Wishes, request.AfishaEventId),
            Now, ct);
        if (!result.Ok) return Error(result.Error, result.StatusCode);

        // Запоминаем телефон гостя — сотрудник найдёт его по номеру при начислении визита
        if (string.IsNullOrEmpty(user.Phone))
        {
            user.Phone = result.Value!.Phone;
            await Db.SaveChangesAsync(ct);
        }
        return BookingDto.From(result.Value!);
    }

    [HttpGet("my")]
    public async Task<ActionResult<IEnumerable<BookingDto>>> My(CancellationToken ct)
    {
        var user = await CurrentUserAsync(ct);
        var list = await Db.Bookings.AsNoTracking()
            .Where(b => b.UserId == user.Id)
            .OrderByDescending(b => b.Date).ThenByDescending(b => b.Time)
            .Take(50)
            .ToListAsync(ct);
        return list.Select(BookingDto.From).ToList();
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<BookingDto>> Cancel(int id, CancellationToken ct)
    {
        var user = await CurrentUserAsync(ct);
        var result = await _bookings.CancelByGuestAsync(user.Id, id, Now, ct);
        return result.Ok ? BookingDto.From(result.Value!) : Error(result.Error, result.StatusCode);
    }
}

[ApiController]
[Route("api/shop")]
[VkAuthorize]
public class ShopController : GuestControllerBase
{
    private readonly ShopService _shop;

    public ShopController(AppDbContext db, UserService users, TimeProvider clock, ShopService shop) : base(db, users, clock)
    {
        _shop = shop;
    }

    [HttpGet("items")]
    public async Task<ActionResult<IEnumerable<ShopItemDto>>> Items(CancellationToken ct)
    {
        var items = await Db.ShopItems.AsNoTracking().Where(i => i.IsActive).OrderBy(i => i.SortOrder).ThenBy(i => i.Id).ToListAsync(ct);
        return items.Select(ShopItemDto.From).ToList();
    }

    [HttpPost("items/{id:int}/purchase")]
    public async Task<ActionResult<PurchaseResultDto>> Purchase(int id, CancellationToken ct)
    {
        var user = await CurrentUserAsync(ct);
        var result = await _shop.PurchaseAsync(user.Id, id, Now, ct);
        if (!result.Ok) return Error(result.Error, result.StatusCode);
        var coins = await Db.Users.AsNoTracking().Where(u => u.Id == user.Id).Select(u => u.DieselCoinsBalance).FirstAsync(ct);
        return new PurchaseResultDto(ShopOrderDto.From(result.Value!), coins);
    }

    /// <summary>«Мои подарки»: к получению, полученные, отменённые / истёкшие (клиент раскладывает по вкладкам).</summary>
    [HttpGet("orders")]
    public async Task<ActionResult<IEnumerable<ShopOrderDto>>> Orders(CancellationToken ct)
    {
        var user = await CurrentUserAsync(ct);
        var now = Now;
        // Просроченные, но ещё не обработанные фоновой проверкой купоны закрываем сразу — гость видит возврат без задержки
        if (await Db.ShopOrders.AnyAsync(o => o.UserId == user.Id && o.Status == ShopOrderStatus.Pending && o.ExpiresAt < now, ct))
        {
            await _shop.ExpireOverdueAsync(now, ct);
        }

        var orders = await Db.ShopOrders.AsNoTracking()
            .Where(o => o.UserId == user.Id)
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Take(100)
            .ToListAsync(ct);
        return orders.Select(ShopOrderDto.From).ToList();
    }

    [HttpPost("orders/{id:int}/cancel")]
    public async Task<ActionResult<PurchaseResultDto>> Cancel(int id, CancellationToken ct)
    {
        var user = await CurrentUserAsync(ct);
        var result = await _shop.CancelByGuestAsync(user.Id, id, Now, ct);
        if (!result.Ok) return Error(result.Error, result.StatusCode);
        var coins = await Db.Users.AsNoTracking().Where(u => u.Id == user.Id).Select(u => u.DieselCoinsBalance).FirstAsync(ct);
        return new PurchaseResultDto(ShopOrderDto.From(result.Value!), coins);
    }
}

[ApiController]
[Route("api/ratings")]
[VkAuthorize]
public class RatingsController : GuestControllerBase
{
    public const int LeaderboardSize = 100;
    private readonly RatingService _rating;

    public RatingsController(AppDbContext db, UserService users, TimeProvider clock, RatingService rating) : base(db, users, clock)
    {
        _rating = rating;
    }

    /// <summary>Гонка недели: места с текущими привилегиями (п. 4.1).</summary>
    [HttpGet("weekly")]
    public async Task<ActionResult<LeaderboardDto>> Weekly(CancellationToken ct)
    {
        var user = await CurrentUserAsync(ct);
        var rows = await _rating.GetWeeklyTopAsync(LeaderboardSize, ct);
        LeaderboardRowDto? me = null;
        var mine = rows.FirstOrDefault(r => r.UserId == user.Id);
        if (mine != null)
        {
            me = ToRow(mine, user.Id);
        }
        else if (await _rating.GetWeeklyRankAsync(user.Id, ct) is { } rank)
        {
            var tier = PrivilegeCatalog.TierForRank(rank);
            me = new LeaderboardRowDto(rank, user.Id, user.DisplayName, user.PhotoUrl, user.WeeklyRating, tier, PrivilegeCatalog.Get(tier)?.Label, true);
        }

        return new LeaderboardDto("weekly", rows.Select(r => ToRow(r, user.Id)).ToList(), me,
            PermTime.WeekStart(Now), PermTime.NextWeeklyReset(Now), PrivilegeCatalog.All.Select(PrivilegeInfoDto.From).ToList());
    }

    /// <summary>За всё время.</summary>
    [HttpGet("total")]
    public async Task<ActionResult<LeaderboardDto>> Total(CancellationToken ct)
    {
        var user = await CurrentUserAsync(ct);
        var rows = await _rating.GetTotalTopAsync(LeaderboardSize, ct);
        LeaderboardRowDto? me = rows.FirstOrDefault(r => r.UserId == user.Id) is { } mine ? ToRow(mine, user.Id) : null;
        if (me == null && await _rating.GetTotalRankAsync(user.Id, ct) is { } rank)
        {
            me = new LeaderboardRowDto(rank, user.Id, user.DisplayName, user.PhotoUrl, user.TotalRating, PrivilegeTier.None, null, true);
        }

        return new LeaderboardDto("total", rows.Select(r => ToRow(r, user.Id)).ToList(), me,
            PermTime.WeekStart(Now), PermTime.NextWeeklyReset(Now), []);
    }

    private static LeaderboardRowDto ToRow(RankedGuest r, int myId) =>
        new(r.Rank, r.UserId, r.Name, r.PhotoUrl, r.Rating, r.Tier, PrivilegeCatalog.Get(r.Tier)?.Label, r.UserId == myId);
}
