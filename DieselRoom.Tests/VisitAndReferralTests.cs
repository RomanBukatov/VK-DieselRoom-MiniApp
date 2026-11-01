using DieselRoom.Domain.Entities;
using DieselRoom.Domain.Enums;
using DieselRoom.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace DieselRoom.Tests;

public class VisitAndReferralTests
{
    private static readonly DateTime Now = TestTimes.WednesdayEvening;

    [Fact]
    public async Task Visit_AwardsCoinsAndRating_IncrementsVisits_WritesAudit_AndNotifiesGuest()
    {
        using var t = new TestDb();
        var guest = t.AddUser("300");

        var result = await t.Create().Visits.AccrueAsync(TestDb.Waiter, guest.Id, " 000123 ", VisitType.BusinessLunch, Now);

        Assert.True(result.Ok, result.Error);
        var after = t.GetUser(guest.Id);
        Assert.Equal(20, after.DieselCoinsBalance);
        Assert.Equal(10, after.WeeklyRating);
        Assert.Equal(10, after.TotalRating);
        Assert.Equal(1, after.VisitsCount);
        Assert.Equal("000123", result.Value!.Visit.ReceiptNumber);

        using var db = t.NewContext();
        var audit = await db.StaffAuditLogs.SingleAsync();
        Assert.Equal(StaffAuditActions.VisitAccrued, audit.Action);
        Assert.Equal("000123", audit.ReceiptNumber);
        Assert.Equal("Официант Анна", audit.StaffName);
        Assert.Contains(t.Notifier.Sent, m => m.VkId == "300" && m.Message.Contains("+20 баллов"));
    }

    [Fact]
    public async Task SameReceipt_CannotBeAccruedTwice_EvenForAnotherGuestOrWithSpaces()
    {
        using var t = new TestDb();
        var first = t.AddUser("301");
        var second = t.AddUser("302");

        Assert.True((await t.Create().Visits.AccrueAsync(TestDb.Waiter, first.Id, "A-555", VisitType.Regular, Now)).Ok);

        var duplicate = await t.Create().Visits.AccrueAsync(TestDb.Waiter, second.Id, " a-555 ", VisitType.Regular, Now.AddMinutes(5));
        Assert.False(duplicate.Ok);
        Assert.Equal(409, duplicate.StatusCode);
        Assert.Contains("уже проведён", duplicate.Error);

        Assert.Equal(0, t.GetUser(second.Id).DieselCoinsBalance);
        Assert.Equal(20, t.GetUser(first.Id).DieselCoinsBalance);
        using var db = t.NewContext();
        Assert.Equal(1, await db.Visits.CountAsync());
    }

    [Fact]
    public async Task ReceiptNumber_HasUniqueIndex_AtDatabaseLevel()
    {
        using var t = new TestDb();
        var guest = t.AddUser("303");
        using var db = t.NewContext();
        db.Visits.Add(new Visit { UserId = guest.Id, ReceiptNumber = "777", StaffName = "a" });
        db.Visits.Add(new Visit { UserId = guest.Id, ReceiptNumber = "777", StaffName = "b" });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.True(DieselRoom.Infrastructure.Data.DbErrors.IsUniqueViolation(ex));

        var index = db.Model.FindEntityType(typeof(Visit))!.GetIndexes().Single(i => i.Properties.Any(p => p.Name == nameof(Visit.ReceiptNumber)));
        Assert.True(index.IsUnique);
    }

    [Fact]
    public async Task InvalidReceipt_IsRejected()
    {
        using var t = new TestDb();
        var guest = t.AddUser("304");
        var result = await t.Create().Visits.AccrueAsync(TestDb.Waiter, guest.Id, "   ", VisitType.Regular, Now);
        Assert.False(result.Ok);
        Assert.Equal(0, t.GetUser(guest.Id).VisitsCount);
    }

    [Fact]
    public async Task Referral_Bonus_IsPaidToInviter_OnlyAfterFriendsFirstVisit_AndOnlyOnce()
    {
        using var t = new TestDb();
        var inviter = t.AddUser("310", coins: 5);
        var friend = t.AddUser("311", referredBy: inviter.Id);

        // До визита бонуса нет
        Assert.Equal(5, t.GetUser(inviter.Id).DieselCoinsBalance);

        var first = await t.Create().Visits.AccrueAsync(TestDb.Waiter, friend.Id, "R-1", VisitType.Regular, Now);
        Assert.True(first.Ok);
        Assert.True(first.Value!.ReferralRewardPaid);

        var inv = t.GetUser(inviter.Id);
        Assert.Equal(105, inv.DieselCoinsBalance);
        Assert.Equal(5, inv.WeeklyRating);
        Assert.True(t.GetUser(friend.Id).ReferralRewardPaid);
        Assert.Contains(t.Notifier.Sent, m => m.VkId == "310" && m.Message.Contains("+100 баллов"));

        var second = await t.Create().Visits.AccrueAsync(TestDb.Waiter, friend.Id, "R-2", VisitType.Regular, Now.AddDays(1));
        Assert.True(second.Ok);
        Assert.False(second.Value!.ReferralRewardPaid);
        Assert.Equal(105, t.GetUser(inviter.Id).DieselCoinsBalance);
    }

    [Fact]
    public async Task UserSync_AttachesReferralCode_ButNotSelfOrRingOrLate()
    {
        using var t = new TestDb();
        var inviter = t.AddUser("320");
        var s = t.Create();

        var friend = await s.Users.SyncAsync("321", new GuestSyncRequest("Иван", "Петров", null, inviter.CardNumber), Now);
        Assert.Equal(inviter.Id, friend.ReferredByUserId);
        Assert.Equal(UserService.CardNumberFor(friend.Id), friend.CardNumber);

        // Сам себя пригласить нельзя
        var self = await t.Create().Users.SyncAsync("322", new GuestSyncRequest("A", null, null, null), Now);
        var selfAgain = await t.Create().Users.SyncAsync("322", new GuestSyncRequest(null, null, null, self.CardNumber), Now);
        Assert.Null(selfAgain.ReferredByUserId);

        // Кольцо: пригласивший не может стать приглашённым своего друга
        var ring = await t.Create().Users.SyncAsync("320", new GuestSyncRequest(null, null, null, friend.CardNumber), Now);
        Assert.Null(ring.ReferredByUserId);

        // Промокод можно привязать только в первые сутки
        var late = t.AddUser("323", createdAt: Now.AddDays(-3));
        var lateSync = await t.Create().Users.SyncAsync("323", new GuestSyncRequest(null, null, null, inviter.CardNumber), Now);
        Assert.Null(lateSync.ReferredByUserId);
    }

    [Fact]
    public async Task NoReferralBonus_WhenGuestWasNotInvited()
    {
        using var t = new TestDb();
        var other = t.AddUser("330", coins: 0);
        var guest = t.AddUser("331");
        var result = await t.Create().Visits.AccrueAsync(TestDb.Waiter, guest.Id, "N-1", VisitType.PartyKaraoke, Now);
        Assert.False(result.Value!.ReferralRewardPaid);
        Assert.Equal(0, t.GetUser(other.Id).DieselCoinsBalance);
    }
}
