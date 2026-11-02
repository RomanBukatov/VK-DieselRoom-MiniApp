using DieselRoom.Domain.Enums;
using DieselRoom.Domain.Services;
using DieselRoom.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace DieselRoom.Tests;

public class RatingAndPrivilegeTests
{
    private static readonly DateTime T0 = new(2026, 11, 2, 5, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task WeeklyRanks_OrderByPoints_ThenWhoScoredFirst()
    {
        using var t = new TestDb();
        var a = t.AddUser("500", weekly: 50, weeklyAt: T0.AddMinutes(10));
        var b = t.AddUser("501", weekly: 50, weeklyAt: T0.AddMinutes(5));
        var c = t.AddUser("502", weekly: 70, weeklyAt: T0.AddMinutes(20));
        t.AddUser("503"); // без очков — вне гонки

        var s = t.Create();
        var top = await s.Rating.GetWeeklyTopAsync(10);
        Assert.Equal([c.Id, b.Id, a.Id], top.Select(r => r.UserId));
        Assert.Equal(PrivilegeTier.BusinessLunch, top[0].Tier);
        Assert.Equal(PrivilegeTier.UnlimitedCoffee, top[1].Tier);
        Assert.Equal(PrivilegeTier.Hookah, top[2].Tier);

        Assert.Equal(3, await s.Rating.GetWeeklyRankAsync(a.Id));
        Assert.Equal(2, await s.Rating.GetWeeklyRankAsync(b.Id));
        Assert.Equal(1, await s.Rating.GetWeeklyRankAsync(c.Id));
    }

    [Fact]
    public async Task UnlimitedCoffee_MovesInstantly_WhenPlayerBOvertakesPlayerA()
    {
        using var t = new TestDb();
        var now = TestTimes.WednesdayEvening;
        t.AddUser("510", weekly: 100, weeklyAt: T0);
        var a = t.AddUser("511", weekly: 60, weeklyAt: T0);
        var b = t.AddUser("512", weekly: 55, weeklyAt: T0);

        var before = await t.Create().Privileges.GetActiveAsync(a.Id, now);
        Assert.Equal(PrivilegeTier.UnlimitedCoffee, before.Tier);
        Assert.True(before.Availability.Allowed);
        Assert.Equal(PrivilegeTier.Hookah, (await t.Create().Privileges.GetActiveAsync(b.Id, now)).Tier);

        // Б получает визит (+10) и обгоняет А
        Assert.True((await t.Create().Visits.AccrueAsync(TestDb.Waiter, b.Id, "OVR-1", VisitType.Regular, now)).Ok);

        var aAfter = await t.Create().Privileges.GetActiveAsync(a.Id, now);
        var bAfter = await t.Create().Privileges.GetActiveAsync(b.Id, now);
        Assert.Equal(PrivilegeTier.UnlimitedCoffee, bAfter.Tier);
        Assert.Equal(PrivilegeTier.Hookah, aAfter.Tier);

        // Официант пробивает кофе Б — проходит; А кофе уже не положен
        Assert.True((await t.Create().Privileges.RedeemAsync(TestDb.Waiter, b.Id, now)).Ok);
        var aCoffee = await t.Create().Privileges.RedeemAsync(TestDb.Waiter, a.Id, now);
        Assert.False(aCoffee.Ok); // у А теперь кальян, а в среду он не выдаётся
        Assert.Contains("Пт/Сб", aCoffee.Error);
    }

    [Fact]
    public async Task FirstPlace_BusinessLunch_UpToThreeTimesPerWeek_ThenResetsNextWeek()
    {
        using var t = new TestDb();
        var leader = t.AddUser("520", weekly: 90, weeklyAt: T0);
        var lunch = TestTimes.MondayLunch;

        for (var i = 1; i <= 3; i++)
        {
            var ok = await t.Create().Privileges.RedeemAsync(TestDb.Waiter, leader.Id, lunch.AddDays(i - 1));
            Assert.True(ok.Ok, ok.Error);
        }
        var fourth = await t.Create().Privileges.RedeemAsync(TestDb.Waiter, leader.Id, lunch.AddDays(3));
        Assert.False(fourth.Ok);
        Assert.Contains("3 из 3", fourth.Error);

        var status = await t.Create().Privileges.GetActiveAsync(leader.Id, lunch.AddDays(3));
        Assert.Equal(3, status.Availability.Used);

        // Вне окна 12:00–16:00 не выдаётся
        var evening = await t.Create().Privileges.RedeemAsync(TestDb.Waiter, leader.Id, lunch.AddDays(7).AddHours(5));
        Assert.False(evening.Ok);

        // Следующая неделя (лидер всё ещё первый — у него остались очки в тесте) — лимит снова 0 из 3
        Assert.True((await t.Create().Privileges.RedeemAsync(TestDb.Waiter, leader.Id, lunch.AddDays(7))).Ok);

        using var db = t.NewContext();
        Assert.Equal(4, await db.PrivilegeRedemptions.CountAsync());
        Assert.Equal(4, await db.StaffAuditLogs.CountAsync(a => a.Action == "PrivilegeIssued"));
    }

    [Fact]
    public async Task Hookah_ForThirdPlace_OncePerWeek_OnFriday()
    {
        using var t = new TestDb();
        t.AddUser("530", weekly: 90, weeklyAt: T0);
        t.AddUser("531", weekly: 80, weeklyAt: T0);
        var third = t.AddUser("532", weekly: 70, weeklyAt: T0);

        Assert.True((await t.Create().Privileges.RedeemAsync(TestDb.Waiter, third.Id, TestTimes.FridayEvening)).Ok);
        var again = await t.Create().Privileges.RedeemAsync(TestDb.Waiter, third.Id, TestTimes.FridayEvening.AddDays(1));
        Assert.False(again.Ok);
        Assert.Contains("Лимит исчерпан", again.Error);
    }

    [Fact]
    public async Task OutsideTop50_HasNoPrivilege()
    {
        using var t = new TestDb();
        for (var i = 0; i < 50; i++) t.AddUser($"6{i:00}", weekly: 100 + i, weeklyAt: T0);
        var last = t.AddUser("699", weekly: 1, weeklyAt: T0);

        var p = await t.Create().Privileges.GetActiveAsync(last.Id, TestTimes.WednesdayEvening);
        Assert.Equal(51, p.Rank);
        Assert.Equal(PrivilegeTier.None, p.Tier);
        Assert.False((await t.Create().Privileges.RedeemAsync(TestDb.Waiter, last.Id, TestTimes.WednesdayEvening)).Ok);
    }

    [Fact]
    public async Task Displacement_NotifiesWhenDroppingOutOfTier_WithPointsToReturn_AndSixHourCooldown()
    {
        using var t = new TestDb();
        var now = TestTimes.WednesdayEvening;
        var a = t.AddUser("700", weekly: 50, weeklyAt: T0);
        var b = t.AddUser("701", weekly: 40, weeklyAt: T0);

        // Первая проверка запоминает места, уведомлений нет
        Assert.Empty(await t.Create().Rating.DetectDisplacementsAsync(now));
        Assert.Equal(1, t.GetUser(a.Id).LastKnownWeeklyRank);

        // Б обгоняет А (визит +10 → 50, но позже — нужно ещё +1; второй визит → 60)
        await t.Create().Visits.AccrueAsync(TestDb.Waiter, b.Id, "D-1", VisitType.Regular, now);
        await t.Create().Visits.AccrueAsync(TestDb.Waiter, b.Id, "D-2", VisitType.Regular, now);

        var notices = await t.Create().Rating.DetectDisplacementsAsync(now.AddMinutes(1));
        var notice = Assert.Single(notices);
        Assert.Equal(a.Id, notice.UserId);
        Assert.Equal(1, notice.OldRank);
        Assert.Equal(2, notice.NewRank);
        Assert.Equal(11, notice.PointsToReturn); // 60 − 50 + 1
        Assert.Equal("Вас обошли в недельном рейтинге Diesel Room! Вы переместились на #2 место. Наберите ещё 11 очков, чтобы вернуть свою привилегию!",
            notice.Message);

        // А возвращает 1-е место (70) — теперь вниз смещается Б, он получает своё уведомление
        await t.Create().Visits.AccrueAsync(TestDb.Waiter, a.Id, "D-3", VisitType.Regular, now);
        await t.Create().Visits.AccrueAsync(TestDb.Waiter, a.Id, "D-4", VisitType.Regular, now);
        var bNotice = Assert.Single(await t.Create().Rating.DetectDisplacementsAsync(now.AddMinutes(30)));
        Assert.Equal(b.Id, bNotice.UserId);

        // Через час А снова теряет 1-е место — уведомление А не чаще 1 раза в 6 часов
        await t.Create().Visits.AccrueAsync(TestDb.Waiter, b.Id, "D-5", VisitType.Regular, now);
        await t.Create().Visits.AccrueAsync(TestDb.Waiter, b.Id, "D-6", VisitType.Regular, now);
        Assert.Empty(await t.Create().Rating.DetectDisplacementsAsync(now.AddHours(1)));
        Assert.Equal(2, t.GetUser(a.Id).LastKnownWeeklyRank); // место всё равно запомнено

        // Б теряет 1-е место через 5,5 ч после своего уведомления — тоже молчим
        await t.Create().Visits.AccrueAsync(TestDb.Waiter, a.Id, "D-7", VisitType.Regular, now);
        await t.Create().Visits.AccrueAsync(TestDb.Waiter, a.Id, "D-8", VisitType.Regular, now);
        Assert.Empty(await t.Create().Rating.DetectDisplacementsAsync(now.AddHours(6)));

        // Через 7 ч после первого уведомления А снова можно предупредить
        await t.Create().Visits.AccrueAsync(TestDb.Waiter, b.Id, "D-9", VisitType.Regular, now);
        await t.Create().Visits.AccrueAsync(TestDb.Waiter, b.Id, "D-10", VisitType.Regular, now);
        Assert.Equal(a.Id, Assert.Single(await t.Create().Rating.DetectDisplacementsAsync(now.AddHours(7))).UserId);
    }

    [Fact]
    public async Task Displacement_WithinSameTier_DoesNotNotify()
    {
        using var t = new TestDb();
        var now = TestTimes.WednesdayEvening;
        int[] scores = [200, 190, 180, 170, 100, 95]; // места 1..6
        for (var i = 0; i < scores.Length; i++) t.AddUser($"71{i}", weekly: scores[i], weeklyAt: T0);
        var top = await t.Create().Rating.GetWeeklyTopAsync(6);
        Assert.Empty(await t.Create().Rating.DetectDisplacementsAsync(now));

        // 6-й (95 + 10 = 105) обгоняет 5-го (100): оба в тире 4–10 (десерт) — без уведомления
        await t.Create().Visits.AccrueAsync(TestDb.Waiter, top[5].UserId, "S-1", VisitType.Regular, now);
        Assert.Equal(5, await t.Create().Rating.GetWeeklyRankAsync(top[5].UserId));
        Assert.Empty(await t.Create().Rating.DetectDisplacementsAsync(now.AddMinutes(1)));
    }

    [Fact]
    public async Task WeeklyReset_ArchivesResults_ZeroesWeekly_KeepsTotalAndCoins()
    {
        using var t = new TestDb();
        var a = t.AddUser("800", coins: 77, weekly: 30, total: 300, weeklyAt: T0);
        var b = t.AddUser("801", coins: 5, weekly: 20, total: 20, weeklyAt: T0);
        var s = t.Create();

        // Первый запуск на этой неделе только запоминает неделю — ничего не обнуляет
        Assert.False(await s.Rating.RunWeeklyResetIfDueAsync(TestTimes.WednesdayEvening));
        Assert.Equal(30, t.GetUser(a.Id).WeeklyRating);

        // Воскресенье 23:58 по Перми — ещё рано
        Assert.False(await t.Create().Rating.RunWeeklyResetIfDueAsync(PermTime.ToUtc(new DateTime(2026, 11, 8, 23, 58, 0))));

        // Воскресенье 23:59 по Перми (18:59 UTC) — сброс
        Assert.True(await t.Create().Rating.RunWeeklyResetIfDueAsync(new DateTime(2026, 11, 8, 18, 59, 0, DateTimeKind.Utc)));
        var ua = t.GetUser(a.Id);
        Assert.Equal(0, ua.WeeklyRating);
        Assert.Equal(300, ua.TotalRating);
        Assert.Equal(77, ua.DieselCoinsBalance);
        Assert.Equal(0, t.GetUser(b.Id).WeeklyRating);

        using (var db = t.NewContext())
        {
            var archive = await db.WeeklyRaceResults.OrderBy(r => r.Rank).ToListAsync();
            Assert.Equal(2, archive.Count);
            Assert.Equal((a.Id, 1, 30, PrivilegeTier.BusinessLunch), (archive[0].UserId, archive[0].Rank, archive[0].WeeklyRating, archive[0].Tier));
            Assert.All(archive, r => Assert.Equal(new DateOnly(2026, 11, 2), r.WeekStart));
        }

        // Повторный запуск в ту же неделю ничего не делает
        Assert.False(await t.Create().Rating.RunWeeklyResetIfDueAsync(new DateTime(2026, 11, 9, 6, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task FirstPlaceStreak_CountsDays_AndAwardsHuskyBottleOnSeventhDay()
    {
        using var t = new TestDb();
        var leader = t.AddUser("900", weekly: 100, weeklyAt: T0);
        t.AddUser("901", weekly: 10, weeklyAt: T0);

        var day1 = PermTime.ToUtc(new DateTime(2026, 11, 2, 23, 56, 0));
        Assert.Equal(1, await t.Create().Rating.GetFirstPlaceStreakAsync(leader.Id, day1));

        for (var d = 0; d < 6; d++)
        {
            var snap = await t.Create().Rating.TakeDailySnapshotAsync(day1.AddDays(d));
            Assert.Equal(d + 1, snap!.StreakDays);
            Assert.False(snap.StreakPrizeAwarded);
        }
        Assert.Null(await t.Create().Rating.TakeDailySnapshotAsync(day1.AddDays(5))); // повтор за ту же дату
        Assert.Equal(6, await t.Create().Rating.GetFirstPlaceStreakAsync(leader.Id, day1.AddDays(5)));
        Assert.Equal(7, await t.Create().Rating.GetFirstPlaceStreakAsync(leader.Id, day1.AddDays(6).AddMinutes(-60)));

        var seventh = await t.Create().Rating.TakeDailySnapshotAsync(day1.AddDays(6));
        Assert.Equal(7, seventh!.StreakDays);
        Assert.True(seventh.StreakPrizeAwarded);

        using var db = t.NewContext();
        var prize = await db.ShopOrders.SingleAsync();
        Assert.Equal(leader.Id, prize.UserId);
        Assert.Equal("Бутылка Хаски 0.5", prize.Title);
        Assert.Equal(0, prize.PriceCoins);
        Assert.Null(prize.ShopItemId);
        Assert.Contains(t.Notifier.Sent, m => m.VkId == "900" && m.Message.Contains("Хаски"));
    }

    [Fact]
    public async Task FirstPlaceStreak_ResetsWhenLeaderChanges()
    {
        using var t = new TestDb();
        var a = t.AddUser("910", weekly: 100, weeklyAt: T0);
        var b = t.AddUser("911", weekly: 95, weeklyAt: T0);
        var day1 = PermTime.ToUtc(new DateTime(2026, 11, 2, 23, 56, 0));

        await t.Create().Rating.TakeDailySnapshotAsync(day1);
        await t.Create().Rating.TakeDailySnapshotAsync(day1.AddDays(1));
        await t.Create().Visits.AccrueAsync(TestDb.Waiter, b.Id, "ST-1", VisitType.Regular, day1.AddDays(2));
        var snap = await t.Create().Rating.TakeDailySnapshotAsync(day1.AddDays(2));
        Assert.Equal(b.Id, snap!.UserId);
        Assert.Equal(1, snap.StreakDays);
        Assert.Equal(0, await t.Create().Rating.GetFirstPlaceStreakAsync(a.Id, day1.AddDays(2)));
    }
}
