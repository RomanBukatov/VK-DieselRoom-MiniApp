using DieselRoom.Domain.Entities;
using DieselRoom.Domain.Enums;
using DieselRoom.Domain.Services;
using DieselRoom.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DieselRoom.Infrastructure.Services;

public sealed record BookingRequest(
    DateOnly Date, TimeOnly Time, int GuestsCount, BookingFormat Format,
    string? Name, string? Phone, string? Wishes, int? AfishaEventId);

/// <summary>Онлайн-бронирование: заявка гостя → подтверждение/отклонение сотрудником с сообщением гостю в ВК.</summary>
public class BookingService
{
    public const int MaxActiveBookingsPerGuest = 3;
    public const int MaxGuests = 200;
    public const int MaxDaysAhead = 90;
    /// <summary>На сегодня бронь принимается не раньше чем за 30 минут.</summary>
    public static readonly TimeSpan MinLeadTime = TimeSpan.FromMinutes(30);

    public const string VenueAddress = "Пермь, ул. Куйбышева, 47";
    public const string VenuePhone = "202-11-00";

    private readonly AppDbContext _db;
    private readonly IGuestNotifier _notifier;
    private readonly StaffAlertService _alerts;

    public BookingService(AppDbContext db, IGuestNotifier notifier, StaffAlertService alerts)
    {
        _db = db;
        _notifier = notifier;
        _alerts = alerts;
    }

    public static string FormatLabel(BookingFormat format) => format switch
    {
        BookingFormat.Hall => "Стол в зале",
        BookingFormat.Karaoke => "Караоке",
        BookingFormat.Party => "Вечеринка / Мероприятие",
        BookingFormat.Banquet => "Банкет",
        _ => "Стол",
    };

    public static IEnumerable<string> Validate(BookingRequest request, DateTime utcNow)
    {
        var localNow = PermTime.ToLocal(utcNow);
        var today = DateOnly.FromDateTime(localNow);

        if (!Enum.IsDefined(request.Format)) yield return "Выберите формат брони.";
        if (request.GuestsCount < 1 || request.GuestsCount > MaxGuests) yield return $"Количество гостей — от 1 до {MaxGuests}.";

        if (request.Date < today) yield return "Дата брони уже прошла.";
        else if (request.Date > today.AddDays(MaxDaysAhead)) yield return $"Бронь принимается не более чем на {MaxDaysAhead} дней вперёд.";
        else if (request.Date.ToDateTime(request.Time) < localNow + MinLeadTime) yield return "Это время уже прошло или наступит меньше чем через 30 минут — позвоните нам: " + VenuePhone + ".";

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length < 2 || name.Length > 60) yield return "Укажите имя (от 2 до 60 символов).";
        if (Codes.NormalizePhone(request.Phone) == null) yield return "Укажите телефон в формате +7 XXX XXX-XX-XX.";
        if (request.Wishes is { Length: > 500 }) yield return "Пожелания — не длиннее 500 символов.";
    }

    public async Task<OpResult<Booking>> CreateAsync(int userId, BookingRequest request, DateTime now, CancellationToken ct = default)
    {
        var errors = Validate(request, now).ToList();
        if (errors.Count > 0) return OpResult<Booking>.Fail(string.Join(" ", errors));

        var active = await _db.Bookings.CountAsync(b => b.UserId == userId &&
            (b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed) &&
            b.Date >= PermTime.LocalDate(now), ct);
        if (active >= MaxActiveBookingsPerGuest)
        {
            return OpResult<Booking>.Fail($"У вас уже {active} активные брони. Отмените лишнюю или позвоните: {VenuePhone}.", 409);
        }

        string? eventTitle = null;
        if (request.AfishaEventId.HasValue)
        {
            eventTitle = await _db.AfishaEvents.AsNoTracking()
                .Where(e => e.Id == request.AfishaEventId && e.IsActive)
                .Select(e => e.Title)
                .FirstOrDefaultAsync(ct);
            if (eventTitle == null) return OpResult<Booking>.Fail("Мероприятие не найдено.", 404);
        }

        var booking = new Booking
        {
            UserId = userId,
            Date = request.Date,
            Time = request.Time,
            GuestsCount = request.GuestsCount,
            Format = request.Format,
            Name = request.Name!.Trim(),
            Phone = Codes.NormalizePhone(request.Phone)!,
            Wishes = string.IsNullOrWhiteSpace(request.Wishes) ? null : request.Wishes.Trim(),
            AfishaEventId = request.AfishaEventId,
            EventTitle = eventTitle,
            Status = BookingStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync(ct);

        await _alerts.NotifyAsync(BuildNewBookingAlert(booking), $"новая бронь #{booking.Id}", ct);
        return OpResult<Booking>.Success(booking);
    }

    public async Task<OpResult<Booking>> CancelByGuestAsync(int userId, int bookingId, DateTime now, CancellationToken ct = default)
    {
        var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId && b.UserId == userId, ct);
        if (booking == null) return OpResult<Booking>.Fail("Бронь не найдена.", 404);
        if (booking.Status is not (BookingStatus.Pending or BookingStatus.Confirmed))
        {
            return OpResult<Booking>.Fail("Эту бронь уже нельзя отменить.", 409);
        }

        booking.Status = BookingStatus.Cancelled;
        booking.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        await _alerts.NotifyAsync(BuildCancelledAlert(booking), $"отмена брони #{booking.Id}", ct);
        return OpResult<Booking>.Success(booking);
    }

    public Task<OpResult<Booking>> ConfirmAsync(StaffIdentity staff, int bookingId, string? comment, DateTime now, CancellationToken ct = default) =>
        TransitionAsync(staff, bookingId, BookingStatus.Pending, BookingStatus.Confirmed, StaffAuditActions.BookingConfirmed, comment, now, ct);

    public Task<OpResult<Booking>> RejectAsync(StaffIdentity staff, int bookingId, string? comment, DateTime now, CancellationToken ct = default) =>
        TransitionAsync(staff, bookingId, BookingStatus.Pending, BookingStatus.Rejected, StaffAuditActions.BookingRejected, comment, now, ct);

    public Task<OpResult<Booking>> CompleteAsync(StaffIdentity staff, int bookingId, DateTime now, CancellationToken ct = default) =>
        TransitionAsync(staff, bookingId, BookingStatus.Confirmed, BookingStatus.Completed, StaffAuditActions.BookingCompleted, null, now, ct);

    private async Task<OpResult<Booking>> TransitionAsync(StaffIdentity staff, int bookingId, BookingStatus from, BookingStatus to,
        string auditAction, string? comment, DateTime now, CancellationToken ct)
    {
        var booking = await _db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking == null) return OpResult<Booking>.Fail("Бронь не найдена.", 404);
        if (booking.Status != from)
        {
            return OpResult<Booking>.Fail($"Бронь уже в статусе «{StatusLabel(booking.Status)}».", 409);
        }

        var cleanComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if (cleanComment is { Length: > 300 }) cleanComment = cleanComment[..300];

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Условный UPDATE (WHERE Status = from): два сотрудника не обработают одну заявку дважды
        var changed = await _db.Bookings
            .Where(b => b.Id == bookingId && b.Status == from)
            .ExecuteUpdateAsync(s =>
            {
                s.SetProperty(b => b.Status, to);
                s.SetProperty(b => b.UpdatedAt, now);
                s.SetProperty(b => b.ProcessedByStaffName, staff.Name);
                if (cleanComment != null) s.SetProperty(b => b.StaffComment, cleanComment);
            }, ct);
        if (changed != 1) return OpResult<Booking>.Fail("Бронь уже обработал другой сотрудник — обновите список.", 409);

        booking.Status = to;
        booking.UpdatedAt = now;
        booking.ProcessedByStaffName = staff.Name;
        if (cleanComment != null) booking.StaffComment = cleanComment;

        var guest = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == booking.UserId, ct);
        StaffAudit.Add(_db, staff, auditAction, guest,
            details: $"Бронь #{booking.Id}: {booking.Date:dd.MM} {booking.Time:HH\\:mm}, {booking.GuestsCount} гост., {FormatLabel(booking.Format)}" +
                     (cleanComment != null ? $" · {cleanComment}" : string.Empty),
            now: now);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var message = to switch
        {
            BookingStatus.Confirmed => BuildConfirmedMessage(booking),
            BookingStatus.Rejected => BuildRejectedMessage(booking),
            _ => null,
        };
        if (message != null)
        {
            _notifier.Send(guest?.VkId, message, $"бронь #{booking.Id} → {to}");
        }

        return OpResult<Booking>.Success(booking);
    }

    public static string StatusLabel(BookingStatus status) => status switch
    {
        BookingStatus.Pending => "Ожидает подтверждения",
        BookingStatus.Confirmed => "Подтверждено",
        BookingStatus.Rejected => "Отклонено",
        BookingStatus.Completed => "Посетил",
        BookingStatus.Cancelled => "Отменено гостем",
        _ => status.ToString(),
    };

    private static string Summary(Booking b) =>
        $"{b.Date:dd.MM.yyyy} в {b.Time:HH\\:mm} · гостей: {b.GuestsCount} · {FormatLabel(b.Format)}\n" +
        (b.EventTitle != null ? $"Событие: {b.EventTitle}\n" : string.Empty);

    public static string BuildNewBookingAlert(Booking b) =>
        $"Новая заявка на бронь #{b.Id}\n" + Summary(b) +
        $"Гость: {b.Name}, +{b.Phone}\n" +
        (b.Wishes != null ? $"Пожелания: {b.Wishes}\n" : string.Empty) +
        "Подтвердите в приложении: Управление → Брони.";

    public static string BuildCancelledAlert(Booking b) =>
        $"Гость отменил бронь #{b.Id}\n" + Summary(b) + $"Гость: {b.Name}, +{b.Phone}";

    public static string BuildConfirmedMessage(Booking b) =>
        $"Ваша бронь в DIESEL ROOM подтверждена.\n" + Summary(b) +
        (b.StaffComment != null ? $"Комментарий: {b.StaffComment}\n" : string.Empty) +
        $"Ждём вас: {VenueAddress}. Телефон: {VenuePhone}.";

    public static string BuildRejectedMessage(Booking b) =>
        $"К сожалению, не получается подтвердить бронь на {b.Date:dd.MM.yyyy} в {b.Time:HH\\:mm}.\n" +
        (b.StaffComment != null ? $"Комментарий: {b.StaffComment}\n" : string.Empty) +
        $"Позвоните нам — подберём другое время: {VenuePhone}.";
}
