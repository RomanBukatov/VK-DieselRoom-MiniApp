using DieselRoom.Domain.Enums;
using DieselRoom.Domain.Entities;
using DieselRoom.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace DieselRoom.Tests;

public class BookingServiceTests
{
    private static readonly DateTime Now = TestTimes.WednesdayEvening; // среда 18:00 Пермь

    private static BookingRequest Valid(DateOnly? date = null, string time = "20:00") => new(
        date ?? new DateOnly(2026, 11, 6), TimeOnly.Parse(time), 4, BookingFormat.Karaoke,
        "Мария", "+7 (902) 123-45-67", "У окна", null);

    [Fact]
    public async Task Create_ValidBooking_IsPending_WithNormalizedPhone()
    {
        using var t = new TestDb();
        var guest = t.AddUser("400");
        var result = await t.Create().Bookings.CreateAsync(guest.Id, Valid(), Now);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(BookingStatus.Pending, result.Value!.Status);
        Assert.Equal("79021234567", result.Value.Phone);
        Assert.Equal("Ожидает подтверждения", BookingService.StatusLabel(result.Value.Status));
    }

    [Theory]
    [InlineData("2026-11-03", "20:00")] // вчера
    [InlineData("2026-11-04", "18:10")] // сегодня, меньше чем за 30 минут
    [InlineData("2027-03-01", "20:00")] // дальше 90 дней
    public async Task Create_RejectsPastOrTooFarDates(string date, string time)
    {
        using var t = new TestDb();
        var guest = t.AddUser("401");
        var result = await t.Create().Bookings.CreateAsync(guest.Id, Valid(DateOnly.Parse(date), time), Now);
        Assert.False(result.Ok);
    }

    [Fact]
    public async Task Create_RejectsBadPhoneNameAndGuests()
    {
        using var t = new TestDb();
        var guest = t.AddUser("402");
        var bad = Valid() with { Phone = "123", Name = "Я", GuestsCount = 0 };
        var result = await t.Create().Bookings.CreateAsync(guest.Id, bad, Now);
        Assert.False(result.Ok);
        Assert.Contains("телефон", result.Error);
        Assert.Contains("имя", result.Error);
        Assert.Contains("гостей", result.Error);
    }

    [Fact]
    public async Task Create_LimitsActiveBookingsPerGuest()
    {
        using var t = new TestDb();
        var guest = t.AddUser("403");
        for (var i = 0; i < BookingService.MaxActiveBookingsPerGuest; i++)
        {
            Assert.True((await t.Create().Bookings.CreateAsync(guest.Id, Valid(new DateOnly(2026, 11, 6 + i)), Now)).Ok);
        }
        var extra = await t.Create().Bookings.CreateAsync(guest.Id, Valid(new DateOnly(2026, 11, 20)), Now);
        Assert.False(extra.Ok);
        Assert.Equal(409, extra.StatusCode);
    }

    [Fact]
    public async Task Confirm_SendsVkMessageToGuest_AndWritesAudit_SecondDecisionRefused()
    {
        using var t = new TestDb();
        var guest = t.AddUser("404");
        var booking = (await t.Create().Bookings.CreateAsync(guest.Id, Valid(), Now)).Value!;

        var confirmed = await t.Create().Bookings.ConfirmAsync(TestDb.Waiter, booking.Id, "Стол №5", Now.AddMinutes(10));
        Assert.True(confirmed.Ok, confirmed.Error);
        Assert.Equal(BookingStatus.Confirmed, confirmed.Value!.Status);

        var message = Assert.Single(t.Notifier.Sent, m => m.VkId == "404");
        Assert.Contains("подтверждена", message.Message);
        Assert.Contains("06.11.2026 в 20:00", message.Message);
        Assert.Contains("Стол №5", message.Message);

        var rejectAfter = await t.Create().Bookings.RejectAsync(TestDb.Waiter, booking.Id, null, Now.AddMinutes(11));
        Assert.False(rejectAfter.Ok);
        Assert.Single(t.Notifier.Sent, m => m.VkId == "404");

        using var db = t.NewContext();
        var audit = await db.StaffAuditLogs.SingleAsync();
        Assert.Equal(StaffAuditActions.BookingConfirmed, audit.Action);
        Assert.Equal("Официант Анна", (await db.Bookings.SingleAsync()).ProcessedByStaffName);
    }

    [Fact]
    public async Task Reject_SendsVkMessage_WithReason()
    {
        using var t = new TestDb();
        var guest = t.AddUser("405");
        var booking = (await t.Create().Bookings.CreateAsync(guest.Id, Valid(), Now)).Value!;

        var rejected = await t.Create().Bookings.RejectAsync(TestDb.Waiter, booking.Id, "Зал закрыт на спецмероприятие", Now);
        Assert.True(rejected.Ok);
        Assert.Equal(BookingStatus.Rejected, rejected.Value!.Status);
        var message = Assert.Single(t.Notifier.Sent, m => m.VkId == "405");
        Assert.Contains("не получается подтвердить", message.Message);
        Assert.Contains("Зал закрыт", message.Message);
    }

    [Fact]
    public async Task Complete_OnlyFromConfirmed_GuestCancelOnlyActive()
    {
        using var t = new TestDb();
        var guest = t.AddUser("406");
        var booking = (await t.Create().Bookings.CreateAsync(guest.Id, Valid(), Now)).Value!;

        Assert.False((await t.Create().Bookings.CompleteAsync(TestDb.Waiter, booking.Id, Now)).Ok);
        Assert.True((await t.Create().Bookings.ConfirmAsync(TestDb.Waiter, booking.Id, null, Now)).Ok);
        Assert.True((await t.Create().Bookings.CompleteAsync(TestDb.Waiter, booking.Id, Now)).Ok);
        Assert.False((await t.Create().Bookings.CancelByGuestAsync(guest.Id, booking.Id, Now)).Ok);

        var second = (await t.Create().Bookings.CreateAsync(guest.Id, Valid(new DateOnly(2026, 11, 7)), Now)).Value!;
        var cancelled = await t.Create().Bookings.CancelByGuestAsync(guest.Id, second.Id, Now);
        Assert.True(cancelled.Ok);
        Assert.Equal("Отменено гостем", BookingService.StatusLabel(cancelled.Value!.Status));

        var other = t.AddUser("407");
        Assert.Equal(404, (await t.Create().Bookings.CancelByGuestAsync(other.Id, second.Id, Now)).StatusCode);
    }

    [Fact]
    public async Task NewBooking_AlertsAdminsAndStaff_NotTheGuest_WithEventAndContact()
    {
        using var t = new TestDb();
        t.StaffOptions.StaffVkIds.Add("2002");
        t.AddStaff("Хостес", vkId: "3003");
        t.AddStaff("Уволен", vkId: "4004", active: false);
        t.AddStaff("Планшет", pin: "1234");
        var guest = t.AddUser("408");
        int eventId;
        using (var db = t.NewContext())
        {
            var ev = new AfishaEvent { Title = "Новый год", DefaultFormat = BookingFormat.Banquet };
            db.AfishaEvents.Add(ev);
            await db.SaveChangesAsync();
            eventId = ev.Id;
        }

        var created = await t.Create().Bookings.CreateAsync(guest.Id, Valid() with { AfishaEventId = eventId }, Now);
        Assert.True(created.Ok, created.Error);
        Assert.Equal("Новый год", created.Value!.EventTitle);

        var recipients = t.Notifier.Sent.Select(m => m.VkId).ToHashSet();
        Assert.Equal(new HashSet<string?> { "1001", "2002", "3003" }, recipients);
        var alert = t.Notifier.Sent.First().Message;
        Assert.Contains("Новая заявка на бронь", alert);
        Assert.Contains("Событие: Новый год", alert);
        Assert.Contains("Мария, +79021234567", alert);
        Assert.Contains("06.11.2026 в 20:00", alert);
    }

    [Fact]
    public async Task GuestCancel_AlertsStaff()
    {
        using var t = new TestDb();
        var guest = t.AddUser("409");
        var booking = (await t.Create().Bookings.CreateAsync(guest.Id, Valid(), Now)).Value!;
        t.Notifier.Sent.Clear();

        Assert.True((await t.Create().Bookings.CancelByGuestAsync(guest.Id, booking.Id, Now)).Ok);
        var alert = Assert.Single(t.Notifier.Sent);
        Assert.Equal("1001", alert.VkId);
        Assert.Contains("отменил бронь", alert.Message);
    }

    [Fact]
    public async Task Confirmation_MentionsTheEvent()
    {
        using var t = new TestDb();
        var guest = t.AddUser("410");
        int eventId;
        using (var db = t.NewContext())
        {
            var ev = new AfishaEvent { Title = "Караоке-вечер" };
            db.AfishaEvents.Add(ev);
            await db.SaveChangesAsync();
            eventId = ev.Id;
        }
        var booking = (await t.Create().Bookings.CreateAsync(guest.Id, Valid() with { AfishaEventId = eventId }, Now)).Value!;
        await t.Create().Bookings.ConfirmAsync(TestDb.Waiter, booking.Id, null, Now);
        Assert.Contains(t.Notifier.Sent, m => m.VkId == "410" && m.Message.Contains("Событие: Караоке-вечер"));
    }
}
