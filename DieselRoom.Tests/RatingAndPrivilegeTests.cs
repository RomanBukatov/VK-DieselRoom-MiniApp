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
    public async Task UnlimitedCoffee_MovesInstantly_WhenGuestBOvertakesGuestA()
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
        Assert.True((await t.Redeem(b.Id, now)).Ok);
        var aCoffee = await t.Redeem(a.Id, now);
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
            var ok = await t.Redeem(leader.Id, lunch.AddDays(i - 1));
            Assert.True(ok.Ok, ok.Error);
        }
        var fourth = await t.Redeem(leader.Id, lunch.AddDays(3));
        Assert.False(fourth.Ok);
        Assert.Contains("3 из 3", fourth.Error);

        var status = await t.Create().Privileges.GetActiveAsync(leader.Id, lunch.AddDays(3));
        Assert.Equal(3, status.Availability.Used);

        // Вне окна 12:00–16:00 не выдаётся
        var evening = await t.Redeem(leader.Id, lunch.AddDays(7).AddHours(5));
        Assert.False(evening.Ok);

        // Следующая неделя (лидер всё ещё первый — у него остались очки в тесте) — лимит снова 0 из 3
        Assert.True((await t.Redeem(leader.Id, lunch.AddDays(7))).Ok);

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

        Assert.True((await t.Redeem(third.Id, TestTimes.FridayEvening)).Ok);
        var again = await t.Redeem(third.Id, TestTimes.FridayEvening.AddDays(1));
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
        Assert.False((await t.Redeem(last.Id, TestTimes.WednesdayEvening)).Ok);
    }

    [Fact]
    public async Task Displacement_NamesLostPrivilegeAndPlace_AndPointsToReturn()
    {
        using var t = new TestDb();
        var now = TestTimes.WednesdayEvening;
        var a = t.AddUser("700", weekly: 50, weeklyAt: T0);
        var b = t.AddUser("701", weekly: 40, weeklyAt: T0);

        // Первая проверка запоминает места, уведомлений нет
        Assert.Empty(await t.Create().Rating.DetectDisplacementsAsync(now));
        Assert.Equal(1, t.GetUser(a.Id).LastKnownWeeklyRank);

        // Б обгоняет А: два визита по +10 дают 60 очков
        await t.Create().Visits.AccrueAsync(TestDb.Waiter, b.Id, "D-1", VisitType.Regular, now);
        await t.Create().Visits.AccrueAsync(TestDb.Waiter, b.Id, "D-2", VisitType.Regular, now);

        var notice = Assert.Single(await t.Create().Rating.DetectDisplacementsAsync(now.AddMinutes(1)));
        Assert.Equal(a.Id, notice.UserId);
        Assert.Equal((1, 2, 11), (notice.OldRank, notice.NewRank, notice.PointsToReturn)); // 60 − 50 + 1
        Assert.Equal("Вас сместили с 1-го места! Привилегия «Бесплатный бизнес-ланч» больше недоступна: сейчас вы на 2-м месте. Наберите ещё 11 очков, чтобы вернуть её.",
            notice.Message);
    }

    [Fact]
    public void DisplacementMessage_ForSecondPlace_ReadsNaturally() =>
        Assert.StartsWith("Вас сместили со 2-го места! Привилегия «Безлимитный кофе»",
            RatingService.BuildDisplacementMessage(2, 3, "Безлимитный кофе", 1));

    [Fact]
    public async Task Displacement_DuringCooldown_IsDelivered_AfterCooldownEnds()
    {
        using var t = new TestDb();
        var now = TestTimes.WednesdayEvening;
        var a = t.AddUser("705", weekly: 50, weeklyAt: T0);
        var b = t.AddUser("706", weekly: 45, weeklyAt: T0);
        Assert.Empty(await t.Create().Rating.DetectDisplacementsAsync(now));

        // А смещён и получил уведомление
        await t.Create().Visits.AccrueAsync(TestDb.Waiter, b.Id, "C-1", VisitType.Regular, now);
        Assert.Equal(a.Id, Assert.Single(await t.Create().Rating.DetectDisplacementsAsync(now.AddMinutes(1))).UserId);

        // А вернул 1-е место: теперь смещён Б
        await t.Create().Visits.AccrueAsync(TestDb.Waiter, a.Id, "C-2", VisitType.Regular, now);
        Assert.Equal(b.Id, Assert.Single(await t.Create().Rating.DetectDisplacementsAsync(now.AddMinutes(2))).UserId);

        // Через час А смещён снова: пауза 6 часов, сообщения нет, но прежнее место остаётся в базе
        await t.Create().Visits.AccrueAsync(TestDb.Waiter, b.Id, "C-3", VisitType.Regular, now);
        Assert.Empty(await t.Create().Rating.DetectDisplacementsAsync(now.AddHours(1)));
        Assert.Equal(1, t.GetUser(a.Id).LastKnownWeeklyRank);

        // Пауза кончилась: событие не потеряно
        var late = Assert.Single(await t.Create().Rating.DetectDisplacementsAsync(now.AddHours(7)));
        Assert.Equal(a.Id, late.UserId);
        Assert.Equal(1, late.OldRank);
        Assert.Equal(2, t.GetUser(a.Id).LastKnownWeeklyRank);
    }

    [Fact]
    public async Task Displacement_RegainedDuringCooldown_IsNotReported()
    {
        using var t = new TestDb();
        var now = TestTimes.WednesdayEvening;
        var a = t.AddUser("707", weekly: 50, weeklyAt: T0);
        var b = t.AddUser("708", weekly: 45, weeklyAt: T0);
        Assert.Empty(await t.Create().Rating.DetectDisplacementsAsync(now));

        await t.Create().Visits.AccrueAsync(TestDb.Waiter, b.Id, "E-1", VisitType.Regular, now);
        Assert.Single(await t.Create().Rating.DetectDisplacementsAsync(now.AddMinutes(1)));
        await t.Create().Visits.AccrueAsync(TestDb.Waiter, a.Id, "E-2", VisitType.Regular, now);
        Assert.Single(await t.Create().Rating.DetectDisplacementsAsync(now.AddMinutes(2)));
        await t.Create().Visits.AccrueAsync(TestDb.Waiter, b.Id, "E-3", VisitType.Regular, now);
        Assert.Empty(await t.Create().Rating.DetectDisplacementsAsync(now.AddHours(1)));

        // А вернул себе место до конца паузы — сообщать не о чем
        await t.Create().Visits.AccrueAsync(TestDb.Waiter, a.Id, "E-4", VisitType.Regular, now);
        var later = await t.Create().Rating.DetectDisplacementsAsync(now.AddHours(8));
        Assert.DoesNotContain(later, n => n.UserId == a.Id);
    }

    [Fact]
    public async Task Approach_WarnsOncePerGoalPerWeek_WithExactText()
    {
        using var t = new TestDb();
        var now = TestTimes.WednesdayEvening;
        t.AddUser("720", weekly: 500, weeklyAt: T0);
        t.AddUser("721", weekly: 400, weeklyAt: T0);
        t.AddUser("722", weekly: 100, weeklyAt: T0); // 3-е место
        var fourth = t.AddUser("723", weekly: 89, weeklyAt: T0); // до Топ-3: 100 − 89 + 1 = 12

        var notice = Assert.Single(await t.Create().Rating.DetectApproachesAsync(now));
        Assert.Equal(fourth.Id, notice.UserId);
        Assert.Equal(PrivilegeTier.Hookah, notice.Goal);
        Assert.Equal("До бесплатного кальяна (Топ-3) осталось всего 12 очков!", notice.Message);

        // О той же цели на этой неделе не повторяем, даже после паузы
        Assert.Empty(await t.Create().Rating.DetectApproachesAsync(now.AddHours(13)));
    }

    [Fact]
    public async Task Approach_FarGuests_DisabledSetting_AndSkipList()
    {
        using var t = new TestDb();
        var now = TestTimes.WednesdayEvening;
        t.AddUser("730", weekly: 500, weeklyAt: T0);
        var second = t.AddUser("731", weekly: 490, weeklyAt: T0);
        t.AddUser("732", weekly: 100, weeklyAt: T0); // далеко от 2-го места

        // Гостю только что ушло сообщение о смещении: второе подряд не шлём
        Assert.Empty(await t.Create().Rating.DetectApproachesAsync(now, new HashSet<int> { second.Id }));

        using (var db = t.NewContext())
        {
            await new SettingsService(db).SaveRatesAsync(LoyaltyRates.Default with { ApproachNotifyPoints = 0 });
        }
        Assert.Empty(await t.Create().Rating.DetectApproachesAsync(now));
    }

    [Fact]
    public async Task Approach_GoalResetsAfterWeeklyReset()
    {
        using var t = new TestDb();
        var guest = t.AddUser("740", weekly: 90, weeklyAt: T0);
        t.AddUser("741", weekly: 95, weeklyAt: T0);
        Assert.Single(await t.Create().Rating.DetectApproachesAsync(TestTimes.WednesdayEvening));
        Assert.Equal(PrivilegeTier.BusinessLunch, t.GetUser(guest.Id).LastApproachTier);

        await t.Create().Rating.RunWeeklyResetIfDueAsync(TestTimes.WednesdayEvening);
        await t.Create().Rating.RunWeeklyResetIfDueAsync(new DateTime(2026, 11, 8, 18, 59, 0, DateTimeKind.Utc));
        Assert.Null(t.GetUser(guest.Id).LastApproachTier);
    }

    [Fact]
    public async Task RaceProgress_ShowsPointsToNextPlaceAndNextPrize()
    {
        using var t = new TestDb();
        t.AddUser("750", weekly: 200, weeklyAt: T0);
        t.AddUser("751", weekly: 150, weeklyAt: T0);
        t.AddUser("752", weekly: 100, weeklyAt: T0);
        var fifthAhead = t.AddUser("753", weekly: 80, weeklyAt: T0);
        var me = t.AddUser("754", weekly: 70, weeklyAt: T0);

        var s = t.Create();
        var race = await s.Rating.GetRaceProgressAsync(t.GetUser(me.Id), 5);
        Assert.Equal(11, race.PointsToNextPlace); // 80 − 70 + 1
        Assert.Equal(4, race.NextRank);
        Assert.Equal("Классический кальян", race.NextPrizeTitle);
        Assert.Equal(3, race.NextPrizeRank);
        Assert.Equal(31, race.PointsToNextPrize); // 100 − 70 + 1

        var leader = await s.Rating.GetRaceProgressAsync(t.GetUser(fifthAhead.Id), 1);
        Assert.Null(leader.PointsToNextPlace);
    }

    [Fact]
    public async Task Privilege_RequiresValidCode_AndCodeIsSingleUse()
    {
        using var t = new TestDb();
        var now = TestTimes.WednesdayEvening;
        t.AddUser("760", weekly: 100, weeklyAt: T0);
        var second = t.AddUser("761", weekly: 90, weeklyAt: T0); // безлимитный кофе: лимита нет, защищает только код

        var s = t.Create();
        var code = s.Privileges.GetCode(second.Id, now);
        Assert.Matches("^[0-9]{6}$", code.Code);
        Assert.NotEqual(t.GetUser(second.Id).CardNumber, code.Code);

        // Без кода и с номером карты выдать нельзя
        Assert.False((await t.Create().Privileges.RedeemAsync(TestDb.Waiter, second.Id, null, now)).Ok);
        var byCard = await t.Create().Privileges.RedeemAsync(TestDb.Waiter, second.Id, t.GetUser(second.Id).CardNumber, now);
        Assert.False(byCard.Ok);
        Assert.Contains("Код неверный", byCard.Error);

        // Правильный код срабатывает один раз
        Assert.True((await t.Create().Privileges.RedeemAsync(TestDb.Waiter, second.Id, code.Code, now)).Ok);
        var replay = await t.Create().Privileges.RedeemAsync(TestDb.Waiter, second.Id, code.Code, now.AddSeconds(5));
        Assert.False(replay.Ok);
        Assert.Contains("уже использован", replay.Error);

        // Новый код — снова можно, старый через 2 минуты уже не подходит
        var fresh = t.Create().Privileges.GetCode(second.Id, now.AddMinutes(1));
        Assert.True((await t.Create().Privileges.RedeemAsync(TestDb.Waiter, second.Id, fresh.Code, now.AddMinutes(1))).Ok);
        var stale = await t.Create().Privileges.RedeemAsync(TestDb.Waiter, second.Id, fresh.Code, now.AddMinutes(4));
        Assert.False(stale.Ok);
    }

    [Fact]
    public async Task PrivilegeCode_FromPreviousWindow_StillAccepted_AndFoundByStaffLookup()
    {
        using var t = new TestDb();
        var now = TestTimes.WednesdayEvening;
        var guest = t.AddUser("762", weekly: 100, weeklyAt: T0);
        t.AddUser("763", weekly: 90, weeklyAt: T0);

        var code = t.Create().Privileges.GetCode(guest.Id, now).Code;
        Assert.Contains(guest.Id, await t.Create().Privileges.FindByCodeAsync(code, now.AddSeconds(60)));
        Assert.DoesNotContain(guest.Id, await t.Create().Privileges.FindByCodeAsync(code, now.AddMinutes(5)));
    }

    [Fact]
    public async Task Staff_CannotServeThemselves()
    {
        using var t = new TestDb();
        var now = TestTimes.WednesdayEvening;
        var waiterAsGuest = t.AddUser("555", coins: 500, weekly: 100, weeklyAt: T0);
        var item = t.AddItem();

        var visit = await t.Create().Visits.AccrueAsync(TestDb.Waiter, waiterAsGuest.Id, "SELF-1", VisitType.Regular, now);
        Assert.False(visit.Ok);
        Assert.Equal(403, visit.StatusCode);
        Assert.Equal(0, t.GetUser(waiterAsGuest.Id).VisitsCount);

        var privilege = await t.Redeem(waiterAsGuest.Id, TestTimes.MondayLunch);
        Assert.Equal(403, privilege.StatusCode);

        var order = (await t.Create().Shop.PurchaseAsync(waiterAsGuest.Id, item.Id, now)).Value!;
        Assert.Equal(403, (await t.Create().Shop.IssueAsync(TestDb.Waiter, order.Code, now)).StatusCode);

        // Вход по ПИН-коду на планшете: сотрудник узнаётся по VK ID, привязанному в админке
        var tablet = new StaffIdentity(StaffRole.Staff, 8, "Бармен", "9999", LinkedVkId: "555");
        Assert.Equal(403, (await t.Create().Visits.AccrueAsync(tablet, waiterAsGuest.Id, "SELF-2", VisitType.Regular, now)).StatusCode);
    }

    [Fact]
    public async Task Admin_MayTestEverythingOnThemselves_AuditKeepsTrace()
    {
        using var t = new TestDb();
        var now = TestTimes.MondayLunch;
        var stas = t.AddUser("1001", coins: 500, weekly: 100, weeklyAt: T0);
        var item = t.AddItem();
        var admin = new StaffIdentity(StaffRole.Admin, null, "Стас", "1001", "1001");

        Assert.True((await t.Create().Visits.AccrueAsync(admin, stas.Id, "ADM-1", VisitType.Regular, now)).Ok);

        var s = t.Create();
        Assert.True((await s.Privileges.RedeemAsync(admin, stas.Id, s.Privileges.GetCode(stas.Id, now).Code, now)).Ok);

        var order = (await t.Create().Shop.PurchaseAsync(stas.Id, item.Id, now)).Value!;
        Assert.True((await t.Create().Shop.IssueAsync(admin, order.Code, now)).Ok);

        using var db = t.NewContext();
        Assert.Equal(3, await db.StaffAuditLogs.CountAsync(a => a.StaffName == "Стас"));

        // Обычный сотрудник с тем же VK ID тем же самым заблокирован
        var staff = admin with { Role = StaffRole.Staff };
        Assert.Equal(403, (await t.Create().Visits.AccrueAsync(staff, stas.Id, "ADM-2", VisitType.Regular, now)).StatusCode);
    }

    [Fact]
    public async Task StreakPrize_IsMarkedAgeRestricted()
    {
        using var t = new TestDb();
        var leader = t.AddUser("770", weekly: 100, weeklyAt: T0);
        var day1 = PermTime.ToUtc(new DateTime(2026, 11, 2, 23, 56, 0));
        for (var d = 0; d < 7; d++) await t.Create().Rating.TakeDailySnapshotAsync(day1.AddDays(d));

        using var db = t.NewContext();
        Assert.True((await db.ShopOrders.SingleAsync(o => o.UserId == leader.Id)).AgeRestricted);
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
