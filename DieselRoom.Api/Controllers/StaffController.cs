using DieselRoom.Api.DTOs;
using DieselRoom.Api.Extensions;
using DieselRoom.Api.Filters;
using DieselRoom.Api.Services;
using DieselRoom.Domain.Enums;
using DieselRoom.Domain.Services;
using DieselRoom.Infrastructure.Data;
using DieselRoom.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DieselRoom.Api.Controllers;

/// <summary>
/// Рабочий экран сотрудника: выдача подарка / привилегии ранга, начисление за визит по чеку, заявки на бронь.
/// Администратору доступно всё то же самое.
/// </summary>
[ApiController]
[Route("api/staff")]
[VkAuthorize(MinRole = StaffRole.Staff)]
public class StaffController : ControllerBase
{
    private const int MaxGuestResults = 10;

    private readonly AppDbContext _db;
    private readonly ShopService _shop;
    private readonly PrivilegeService _privileges;
    private readonly VisitService _visits;
    private readonly BookingService _bookings;
    private readonly ProfileBuilder _profiles;
    private readonly TimeProvider _clock;

    public StaffController(AppDbContext db, ShopService shop, PrivilegeService privileges, VisitService visits,
        BookingService bookings, ProfileBuilder profiles, TimeProvider clock)
    {
        _db = db;
        _shop = shop;
        _privileges = privileges;
        _visits = visits;
        _bookings = bookings;
        _profiles = profiles;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;
    private StaffIdentity Staff => HttpContext.GetStaff();
    private ObjectResult Error(string? message, int status) => StatusCode(status, new { message });

    /// <summary>Проверка входа (по VK ID или ПИН-коду): роль и имя для шапки панели.</summary>
    [HttpGet("session")]
    public ActionResult<StaffSessionDto> Session() => new StaffSessionDto(Staff.Role, Staff.Name);

    /// <summary>
    /// Поиск по коду купона (#DR-XXXX), коду привилегии и/или гостю: номер карты, телефон, имя, VK ID. Для каждого
    /// гостя — текущая привилегия ранга с проверкой лимита.
    /// </summary>
    [HttpGet("lookup")]
    public async Task<ActionResult<StaffLookupDto>> Lookup([FromQuery] string? q, CancellationToken ct)
    {
        var query = q?.Trim() ?? string.Empty;
        if (query.Length < 2) return Error("Введите код купона, номер карты, телефон или имя гостя.", 400);

        StaffOrderDto? orderDto = null;
        var code = Codes.NormalizeOrderCode(query);
        if (code != null)
        {
            var order = await _db.ShopOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Code == code, ct);
            if (order != null)
            {
                if (order.Status == ShopOrderStatus.Pending && order.ExpiresAt < Now)
                {
                    await _shop.ExpireOverdueAsync(Now, ct);
                    order = await _db.ShopOrders.AsNoTracking().FirstAsync(o => o.Id == order.Id, ct);
                }
                var owner = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == order.UserId, ct);
                orderDto = new StaffOrderDto(ShopOrderDto.From(order), owner.Id, owner.DisplayName, owner.CardNumber, owner.PhotoUrl);
            }
        }

        // Код привилегии: код с экрана гостя (или из QR) сразу находит того, кому он выдан
        var privilegeCode = Codes.NormalizePrivilegeCode(query, out var fromQr);
        var codeOwners = privilegeCode == null ? [] : await _privileges.FindByCodeAsync(privilegeCode, Now, ct);
        if (fromQr)
        {
            var owners = await _db.Users.AsNoTracking().Where(u => codeOwners.Contains(u.Id)).ToListAsync(ct);
            var ownerCards = new List<GuestCardDto>();
            foreach (var owner in owners) ownerCards.Add(await _profiles.BuildGuestCardAsync(owner, Now, ct));
            return new StaffLookupDto(null, ownerCards, codeOwners.Count > 0 ? privilegeCode : null, codeOwners);
        }

        var digits = new string(query.Where(char.IsDigit).ToArray());
        var phone = Codes.NormalizePhone(query);
        var lowered = query.ToLowerInvariant();
        var guests = await _db.Users.AsNoTracking()
            .Where(u => codeOwners.Contains(u.Id) || u.CardNumber == query || u.VkId == digits ||
                        (phone != null && u.Phone == phone) ||
                        (digits.Length >= 4 && u.Phone != null && u.Phone.EndsWith(digits)) ||
                        (query.Length >= 3 && (u.FirstName.ToLower().Contains(lowered) || u.LastName.ToLower().Contains(lowered) ||
                                               (u.FirstName + " " + u.LastName).ToLower().Contains(lowered))))
            .OrderByDescending(u => u.LastSeenAt)
            .Take(MaxGuestResults)
            .ToListAsync(ct);

        var cards = new List<GuestCardDto>();
        foreach (var guest in guests)
        {
            cards.Add(await _profiles.BuildGuestCardAsync(guest, Now, ct));
        }
        return new StaffLookupDto(orderDto, cards.OrderByDescending(c => codeOwners.Contains(c.UserId)).ToList(),
            codeOwners.Count > 0 ? privilegeCode : null, codeOwners);
    }

    /// <summary>Подтвердить выдачу подарка по купону #DR-XXXX.</summary>
    [HttpPost("coupons/issue")]
    public async Task<ActionResult<ShopOrderDto>> IssueCoupon([FromBody] IssueCouponRequest request, CancellationToken ct)
    {
        var result = await _shop.IssueAsync(Staff, request.Code ?? string.Empty, Now, ct);
        return result.Ok ? ShopOrderDto.From(result.Value!) : Error(result.Error, result.StatusCode);
    }

    /// <summary>Подтвердить выдачу привилегии кодом с экрана гостя (код и лимиты проверяются в момент выдачи).</summary>
    [HttpPost("privileges/redeem")]
    public async Task<ActionResult<GuestCardDto>> RedeemPrivilege([FromBody] RedeemPrivilegeRequest request, CancellationToken ct)
    {
        var result = await _privileges.RedeemAsync(Staff, request.UserId, request.Code, Now, ct);
        if (!result.Ok) return Error(result.Error, result.StatusCode);
        var guest = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == request.UserId, ct);
        return await _profiles.BuildGuestCardAsync(guest, Now, ct);
    }

    /// <summary>Начисление за визит по номеру закрытого чека (1 чек = 1 раз).</summary>
    [HttpPost("visits")]
    public async Task<ActionResult<VisitResultDto>> AccrueVisit([FromBody] AccrueVisitRequest request, CancellationToken ct)
    {
        var result = await _visits.AccrueAsync(Staff, request.UserId, request.ReceiptNumber, request.Type, Now, ct);
        if (!result.Ok) return Error(result.Error, result.StatusCode);
        var v = result.Value!;
        return new VisitResultDto(v.Visit.Id, v.Visit.ReceiptNumber, v.Visit.CoinsAwarded, v.Visit.RatingAwarded,
            await _profiles.BuildGuestCardAsync(v.Guest, Now, ct), v.ReferralRewardPaid, v.Inviter?.DisplayName);
    }

    /// <summary>Заявки на бронь: по умолчанию ожидающие и подтверждённые на сегодня и позже.</summary>
    [HttpGet("bookings")]
    public async Task<ActionResult<IEnumerable<BookingDto>>> Bookings([FromQuery] BookingStatus? status, CancellationToken ct)
    {
        var today = PermTime.LocalDate(Now);
        var query = _db.Bookings.AsNoTracking().AsQueryable();
        query = status.HasValue
            ? query.Where(b => b.Status == status.Value)
            : query.Where(b => (b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed) && b.Date >= today);
        var list = await query.OrderBy(b => b.Date).ThenBy(b => b.Time).Take(200).ToListAsync(ct);
        return list.Select(BookingDto.From).ToList();
    }

    [HttpPost("bookings/{id:int}/confirm")]
    public async Task<ActionResult<BookingDto>> Confirm(int id, [FromBody] StaffDecisionRequest? request, CancellationToken ct)
    {
        var result = await _bookings.ConfirmAsync(Staff, id, request?.Comment, Now, ct);
        return result.Ok ? BookingDto.From(result.Value!) : Error(result.Error, result.StatusCode);
    }

    [HttpPost("bookings/{id:int}/reject")]
    public async Task<ActionResult<BookingDto>> Reject(int id, [FromBody] StaffDecisionRequest? request, CancellationToken ct)
    {
        var result = await _bookings.RejectAsync(Staff, id, request?.Comment, Now, ct);
        return result.Ok ? BookingDto.From(result.Value!) : Error(result.Error, result.StatusCode);
    }

    [HttpPost("bookings/{id:int}/complete")]
    public async Task<ActionResult<BookingDto>> Complete(int id, CancellationToken ct)
    {
        var result = await _bookings.CompleteAsync(Staff, id, Now, ct);
        return result.Ok ? BookingDto.From(result.Value!) : Error(result.Error, result.StatusCode);
    }
}
