using System.Text.Json;
using System.Text.RegularExpressions;
using DieselRoom.Api.Controllers;
using DieselRoom.Api.DTOs;
using DieselRoom.Api.Models;
using DieselRoom.Domain.Entities;
using DieselRoom.Domain.Enums;
using DieselRoom.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DieselRoom.Tests;

/// <summary>Ни одно начисление или списание не теряется: журнал операций всегда сходится с балансом.</summary>
public class LedgerTests
{
    private static readonly DateTime Now = TestTimes.WednesdayEvening;

    private static async Task Adjust(TestDb t, int userId, int coins, int rating, string reason = "Проверка")
    {
        using var db = t.NewContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        await Ledger.AdjustAsync(db, userId, coins, rating, reason, Now);
        await tx.CommitAsync();
    }

    private static void AssertLedgerMatchesBalance(TestDb t, int userId)
    {
        using var db = t.NewContext();
        var entries = db.PointsTransactions.AsNoTracking().Where(x => x.UserId == userId).ToList();
        var user = t.GetUser(userId);
        Assert.Equal(user.DieselCoinsBalance, entries.Sum(x => x.Coins));
        Assert.Equal(user.TotalRating, entries.Sum(x => x.Rating));
    }

    [Fact]
    public async Task EveryOperationType_LeavesEntry_AndLedgerMatchesBalance()
    {
        using var t = new TestDb();
        var guest = t.AddUser("500100");
        var inviter = t.AddUser("500101");
        var item = t.AddItem(price: 50, stock: 5);

        // Корректировка, визит, бонус за друга
        await Adjust(t, guest.Id, 500, 40);
        using (var db = t.NewContext())
        {
            await db.Users.Where(u => u.Id == guest.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.ReferredByUserId, inviter.Id));
        }
        Assert.True((await t.Create().Visits.AccrueAsync(TestDb.Waiter, guest.Id, "L-1", VisitType.Regular, Now)).Ok);

        // Активность ВК и разовые бонусы
        var clock = new FakeClock(Now);
        VkCallbackController Callback()
        {
            var db = t.NewContext();
            return new VkCallbackController(db, new SettingsService(db),
                Options.Create(new VkOptions { CallbackSecretKey = "cb", GroupId = 34613849 }), NullLogger<VkCallbackController>.Instance, clock);
        }
        VkCallbackRequest Event(string type, object payload) => new(type, JsonSerializer.SerializeToElement(payload), "cb", 34613849);
        await Callback().HandleCallback(Event("like_add", new { object_type = "post", object_id = 1, liker_id = 500100 }));
        await Callback().HandleCallback(Event("wall_reply_new", new { from_id = 500100, post_id = 1, text = "Отличная вечеринка была" }));
        await Callback().HandleCallback(Event("wall_repost", new { owner_id = 500100, from_id = 500100, copy_history = new[] { new { id = 1, owner_id = -34613849L } } }));
        await Callback().HandleCallback(Event("group_join", new { user_id = 500100 }));
        await Callback().HandleCallback(Event("message_allow", new { user_id = 500100 }));
        await Callback().HandleCallback(Event("poll_vote_new", new { owner_id = -34613849L, poll_id = 3, user_id = 500100 }));

        // Обмен, отмена с возвратом, истечение с возвратом
        var kept = (await t.Create().Shop.PurchaseAsync(guest.Id, item.Id, Now)).Value!;
        Assert.True((await t.Create().Shop.IssueAsync(TestDb.Waiter, kept.Code, Now)).Ok);
        var cancelled = (await t.Create().Shop.PurchaseAsync(guest.Id, item.Id, Now)).Value!;
        Assert.True((await t.Create().Shop.CancelByGuestAsync(guest.Id, cancelled.Id, Now)).Ok);
        await t.Create().Shop.PurchaseAsync(guest.Id, item.Id, Now);
        Assert.Equal(1, await t.Create().Shop.ExpireOverdueAsync(Now.AddDays(8)));

        using (var check = t.NewContext())
        {
            var types = await check.PointsTransactions.Select(x => x.Type).Distinct().ToListAsync();
            Assert.Superset(new HashSet<string>
            {
                PointsTransactionTypes.ManualAdjust, PointsTransactionTypes.Visit, PointsTransactionTypes.Referral,
                PointsTransactionTypes.VkLike, PointsTransactionTypes.VkComment, PointsTransactionTypes.VkRepost,
                PointsTransactionTypes.GroupJoinBonus, PointsTransactionTypes.MessageAllowBonus, PointsTransactionTypes.PollVote,
                PointsTransactionTypes.ShopPurchase, PointsTransactionTypes.ShopRefund, PointsTransactionTypes.ShopExpiredRefund,
            }, types.ToHashSet());
        }
        AssertLedgerMatchesBalance(t, guest.Id);
        AssertLedgerMatchesBalance(t, inviter.Id);
    }

    [Fact]
    public async Task Adjust_RecordsActualRatingChange_WhenClampedAtZero()
    {
        using var t = new TestDb();
        var guest = t.AddUser("500200");
        await Adjust(t, guest.Id, 0, 7);
        await Adjust(t, guest.Id, 0, -100);

        Assert.Equal(0, t.GetUser(guest.Id).TotalRating);
        using var db = t.NewContext();
        var last = await db.PointsTransactions.OrderByDescending(x => x.Id).FirstAsync();
        Assert.Equal(-7, last.Rating); // а не запрошенные -100
        AssertLedgerMatchesBalance(t, guest.Id);
    }

    [Fact]
    public async Task RolledBackOperation_LeavesNeitherBalanceNorEntry()
    {
        using var t = new TestDb();
        var guest = t.AddUser("500300");
        using (var db = t.NewContext())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await Ledger.AwardAsync(db, guest.Id, 100, 10, PointsTransactionTypes.Visit, "Откатится", Now);
            await tx.RollbackAsync();
        }

        Assert.Equal(0, t.GetUser(guest.Id).DieselCoinsBalance);
        using var check = t.NewContext();
        Assert.Equal(0, await check.PointsTransactions.CountAsync());
    }

    [Fact]
    public async Task RefusedDebit_WritesNothing()
    {
        using var t = new TestDb();
        var guest = t.AddUser("500400");
        using var db = t.NewContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        Assert.False(await Ledger.DebitAsync(db, guest.Id, 10, PointsTransactionTypes.ShopPurchase, "Не хватает", Now));
        Assert.Null(await Ledger.AdjustAsync(db, guest.Id, -10, 0, "Не хватает", Now));
        await tx.CommitAsync();
        Assert.Equal(0, await db.PointsTransactions.CountAsync());
    }

    [Fact]
    public async Task Ledger_RefusesToWorkOutsideDbTransaction()
    {
        using var t = new TestDb();
        var guest = t.AddUser("500500", coins: 100);
        using var db = t.NewContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Ledger.AwardAsync(db, guest.Id, 1, 1, PointsTransactionTypes.Visit, "x", Now));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Ledger.DebitAsync(db, guest.Id, 1, PointsTransactionTypes.ShopPurchase, "x", Now));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Ledger.AdjustAsync(db, guest.Id, 1, 0, "x", Now));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Ledger.RefundAsync(db, guest.Id, 1, PointsTransactionTypes.ShopRefund, "x", Now));
        Assert.Equal(100, t.GetUser(guest.Id).DieselCoinsBalance);
    }

    /// <summary>Защита на будущее: новый код не сможет менять баланс в обход журнала.</summary>
    [Fact]
    public void OnlyLedger_ChangesBalanceAndWritesEntries()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DieselRoomApp.slnx"))) dir = dir.Parent;
        var bypass = new Regex(@"SetProperty\(\s*u\s*=>\s*u\.(DieselCoinsBalance|TotalRating|TotalCoinsEarned)|new PointsTransaction\b|PointsTransactions\.(Add|Remove|Update|Execute)", RegexOptions.Compiled);

        var offenders = new[] { "DieselRoom.Api", "DieselRoom.Domain", "DieselRoom.Infrastructure" }
            .SelectMany(p => Directory.EnumerateFiles(Path.Combine(dir!.FullName, p), "*.cs", SearchOption.AllDirectories))
            .Where(f => !Regex.IsMatch(f, @"[\\/](bin|obj|Migrations)[\\/]") && Path.GetFileName(f) != "Ledger.cs")
            .Where(f => bypass.IsMatch(File.ReadAllText(f)))
            .ToList();
        Assert.Empty(offenders);
    }
}
