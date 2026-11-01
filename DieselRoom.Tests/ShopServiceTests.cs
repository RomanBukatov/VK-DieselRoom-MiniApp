using DieselRoom.Domain.Entities;
using DieselRoom.Domain.Enums;
using DieselRoom.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace DieselRoom.Tests;

public class ShopServiceTests
{
    private static readonly DateTime Now = TestTimes.WednesdayEvening;

    [Fact]
    public async Task Purchase_DebitsCoins_ReservesStock_CreatesSevenDayCoupon_KeepsRating()
    {
        using var t = new TestDb();
        var guest = t.AddUser("200", coins: 120, weekly: 40, total: 90);
        var item = t.AddItem(price: 50, stock: 2);

        var s = t.Create();
        var result = await s.Shop.PurchaseAsync(guest.Id, item.Id, Now);

        Assert.True(result.Ok, result.Error);
        var order = result.Value!;
        Assert.Matches("^DR-[A-Z2-9]{4}$", order.Code);
        Assert.Equal(ShopOrderStatus.Pending, order.Status);
        Assert.Equal(Now.AddDays(7), order.ExpiresAt);

        var after = t.GetUser(guest.Id);
        Assert.Equal(70, after.DieselCoinsBalance);
        Assert.Equal(40, after.WeeklyRating);   // рейтинг покупкой не уменьшается
        Assert.Equal(90, after.TotalRating);
        Assert.Equal(1, t.GetItem(item.Id).StockCount);
    }

    [Fact]
    public async Task Purchase_Fails_WhenNotEnoughCoins_OrOutOfStock_AndChangesNothing()
    {
        using var t = new TestDb();
        var poor = t.AddUser("201", coins: 10);
        var rich = t.AddUser("202", coins: 1000);
        var item = t.AddItem(price: 50, stock: 1);

        var s = t.Create();
        var noMoney = await s.Shop.PurchaseAsync(poor.Id, item.Id, Now);
        Assert.False(noMoney.Ok);
        Assert.Equal(409, noMoney.StatusCode);
        Assert.Equal(10, t.GetUser(poor.Id).DieselCoinsBalance);
        Assert.Equal(1, t.GetItem(item.Id).StockCount);

        Assert.True((await s.Shop.PurchaseAsync(rich.Id, item.Id, Now)).Ok);
        var soldOut = await t.Create().Shop.PurchaseAsync(rich.Id, item.Id, Now);
        Assert.False(soldOut.Ok);
        Assert.Equal(950, t.GetUser(rich.Id).DieselCoinsBalance);
        Assert.Equal(0, t.GetItem(item.Id).StockCount);
    }

    [Fact]
    public async Task GuestCancel_RefundsCoinsInstantly_AndReturnsStock_OnlyOnce()
    {
        using var t = new TestDb();
        var guest = t.AddUser("203", coins: 100);
        var item = t.AddItem(price: 60, stock: 3);
        var order = (await t.Create().Shop.PurchaseAsync(guest.Id, item.Id, Now)).Value!;

        var cancel = await t.Create().Shop.CancelByGuestAsync(guest.Id, order.Id, Now.AddHours(1));
        Assert.True(cancel.Ok, cancel.Error);
        Assert.Equal(ShopOrderStatus.Cancelled, cancel.Value!.Status);
        Assert.Equal(100, t.GetUser(guest.Id).DieselCoinsBalance);
        Assert.Equal(3, t.GetItem(item.Id).StockCount);

        var again = await t.Create().Shop.CancelByGuestAsync(guest.Id, order.Id, Now.AddHours(2));
        Assert.False(again.Ok);
        Assert.Equal(100, t.GetUser(guest.Id).DieselCoinsBalance);
        Assert.Equal(3, t.GetItem(item.Id).StockCount);

        using var db = t.NewContext();
        var history = await db.PointsTransactions.Where(x => x.UserId == guest.Id).OrderBy(x => x.Id).Select(x => x.Coins).ToListAsync();
        Assert.Equal([-60, 60], history);
    }

    [Fact]
    public async Task CannotCancelSomeoneElsesOrder()
    {
        using var t = new TestDb();
        var owner = t.AddUser("204", coins: 100);
        var stranger = t.AddUser("205", coins: 0);
        var item = t.AddItem(price: 60);
        var order = (await t.Create().Shop.PurchaseAsync(owner.Id, item.Id, Now)).Value!;

        var result = await t.Create().Shop.CancelByGuestAsync(stranger.Id, order.Id, Now);
        Assert.False(result.Ok);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal(40, t.GetUser(owner.Id).DieselCoinsBalance);
    }

    [Fact]
    public async Task ExpiredCoupons_AreClosedByBackgroundCheck_WithRefundAndRestock()
    {
        using var t = new TestDb();
        var guest = t.AddUser("206", coins: 200);
        var item = t.AddItem(price: 80, stock: 1);
        var order = (await t.Create().Shop.PurchaseAsync(guest.Id, item.Id, Now)).Value!;

        // Ещё действует — ничего не происходит
        Assert.Equal(0, await t.Create().Shop.ExpireOverdueAsync(Now.AddDays(6)));
        Assert.Equal(120, t.GetUser(guest.Id).DieselCoinsBalance);

        Assert.Equal(1, await t.Create().Shop.ExpireOverdueAsync(Now.AddDays(7).AddMinutes(1)));
        using var db = t.NewContext();
        Assert.Equal(ShopOrderStatus.Expired, (await db.ShopOrders.FirstAsync(o => o.Id == order.Id)).Status);
        Assert.Equal(200, t.GetUser(guest.Id).DieselCoinsBalance);
        Assert.Equal(1, t.GetItem(item.Id).StockCount);
        Assert.Equal(PointsTransactionTypes.ShopExpiredRefund,
            (await db.PointsTransactions.OrderByDescending(x => x.Id).FirstAsync()).Type);

        // Повторная проверка не возвращает баллы второй раз
        Assert.Equal(0, await t.Create().Shop.ExpireOverdueAsync(Now.AddDays(8)));
        Assert.Equal(200, t.GetUser(guest.Id).DieselCoinsBalance);
    }

    [Fact]
    public async Task StaffIssue_ClosesCoupon_WritesAudit_AndIssuedCouponIsNeverRefunded()
    {
        using var t = new TestDb();
        var guest = t.AddUser("207", coins: 100);
        var item = t.AddItem(price: 100, stock: 1);
        var order = (await t.Create().Shop.PurchaseAsync(guest.Id, item.Id, Now)).Value!;

        var issued = await t.Create().Shop.IssueAsync(TestDb.Waiter, "#" + order.Code.ToLowerInvariant(), Now.AddHours(1));
        Assert.True(issued.Ok, issued.Error);
        Assert.Equal(ShopOrderStatus.Issued, issued.Value!.Status);

        var twice = await t.Create().Shop.IssueAsync(TestDb.Waiter, order.Code, Now.AddHours(2));
        Assert.False(twice.Ok);
        Assert.Contains("уже выдан", twice.Error);

        Assert.False((await t.Create().Shop.CancelByGuestAsync(guest.Id, order.Id, Now.AddHours(3))).Ok);
        Assert.Equal(0, await t.Create().Shop.ExpireOverdueAsync(Now.AddDays(30)));
        Assert.Equal(0, t.GetUser(guest.Id).DieselCoinsBalance);
        Assert.Equal(0, t.GetItem(item.Id).StockCount);

        using var db = t.NewContext();
        var audit = await db.StaffAuditLogs.SingleAsync();
        Assert.Equal(StaffAuditActions.GiftIssued, audit.Action);
        Assert.Equal("Официант Анна", audit.StaffName);
        Assert.Equal(guest.Id, audit.GuestUserId);
    }

    [Fact]
    public async Task StaffIssue_OfExpiredCoupon_IsRefused_AndRefundsGuest()
    {
        using var t = new TestDb();
        var guest = t.AddUser("208", coins: 100);
        var item = t.AddItem(price: 100, stock: 1);
        var order = (await t.Create().Shop.PurchaseAsync(guest.Id, item.Id, Now)).Value!;

        var result = await t.Create().Shop.IssueAsync(TestDb.Waiter, order.Code, Now.AddDays(8));
        Assert.False(result.Ok);
        Assert.Contains("истёк", result.Error);
        Assert.Equal(100, t.GetUser(guest.Id).DieselCoinsBalance);
        Assert.Equal(1, t.GetItem(item.Id).StockCount);
    }

    [Fact]
    public async Task OrderCode_IsUniqueInDatabase()
    {
        using var t = new TestDb();
        var guest = t.AddUser("209");
        using var db = t.NewContext();
        db.ShopOrders.Add(new ShopOrder { UserId = guest.Id, Code = "DR-AAAA", Title = "x", ExpiresAt = Now });
        db.ShopOrders.Add(new ShopOrder { UserId = guest.Id, Code = "DR-AAAA", Title = "y", ExpiresAt = Now });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
