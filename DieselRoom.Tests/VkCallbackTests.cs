using System.Text.Json;
using DieselRoom.Api.Controllers;
using DieselRoom.Api.DTOs;
using DieselRoom.Api.Models;
using DieselRoom.Domain.Services;
using DieselRoom.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DieselRoom.Tests;

public class VkCallbackTests
{
    private const long GroupId = 34613849;

    private static VkCallbackController Controller(TestDb t, FakeClock clock, string secret = "cb")
    {
        var db = t.NewContext();
        return new VkCallbackController(db, new SettingsService(db),
            Options.Create(new VkOptions { CallbackSecretKey = secret, GroupId = GroupId, CallbackConfirmationCode = "conf-123" }),
            NullLogger<VkCallbackController>.Instance, clock);
    }

    private static VkCallbackRequest Event(string type, object payload, string secret = "cb", long groupId = GroupId) =>
        new(type, JsonSerializer.SerializeToElement(payload), secret, groupId);

    private static object Comment(long from, int post, string text) => new { from_id = from, post_id = post, text };

    [Fact]
    public async Task Confirmation_AndSecretCheck()
    {
        using var t = new TestDb();
        var clock = new FakeClock(TestTimes.WednesdayEvening);
        var confirm = await Controller(t, clock).HandleCallback(new VkCallbackRequest("confirmation", null, "cb", GroupId));
        Assert.Equal("conf-123", ((ContentResult)confirm).Content);

        var forbidden = await Controller(t, clock).HandleCallback(Event("like_add", new { }, secret: "wrong"));
        Assert.Equal(403, ((StatusCodeResult)forbidden).StatusCode);
    }

    [Fact]
    public async Task Comments_MeaningfulOnly_MaxTwoPer24h_NoCopyPaste()
    {
        using var t = new TestDb();
        var guest = t.AddUser("100500");
        var clock = new FakeClock(TestTimes.WednesdayEvening);

        await Controller(t, clock).HandleCallback(Event("wall_reply_new", Comment(100500, 10, "🔥🔥🔥 класс")));
        Assert.Equal(0, t.GetUser(guest.Id).DieselCoinsBalance);

        await Controller(t, clock).HandleCallback(Event("wall_reply_new", Comment(100500, 10, "Отличная вечеринка была!")));
        Assert.Equal((3, 3), (t.GetUser(guest.Id).DieselCoinsBalance, t.GetUser(guest.Id).WeeklyRating));

        // Тот же текст под другим постом — копипаст
        await Controller(t, clock).HandleCallback(Event("wall_reply_new", Comment(100500, 11, "отличная   вечеринка БЫЛА!")));
        Assert.Equal(3, t.GetUser(guest.Id).DieselCoinsBalance);

        // Второй коммент под тем же постом — не оплачивается
        await Controller(t, clock).HandleCallback(Event("wall_reply_new", Comment(100500, 10, "И караоке просто огонь")));
        Assert.Equal(3, t.GetUser(guest.Id).DieselCoinsBalance);

        await Controller(t, clock).HandleCallback(Event("wall_reply_new", Comment(100500, 12, "Лучшие бизнес-ланчи в Перми")));
        Assert.Equal(6, t.GetUser(guest.Id).DieselCoinsBalance);

        // Третий оплачиваемый за 24 часа — уже нет
        await Controller(t, clock).HandleCallback(Event("wall_reply_new", Comment(100500, 13, "Приду в пятницу с друзьями")));
        Assert.Equal(6, t.GetUser(guest.Id).DieselCoinsBalance);

        // Через сутки — снова можно
        clock.Advance(TimeSpan.FromHours(25));
        await Controller(t, clock).HandleCallback(Event("wall_reply_new", Comment(100500, 13, "Приду в пятницу с друзьями")));
        Assert.Equal(9, t.GetUser(guest.Id).DieselCoinsBalance);
    }

    [Fact]
    public async Task Likes_AndReposts_AreAwardedOncePerPost_ForeignGroupAndArchivedIgnored()
    {
        using var t = new TestDb();
        var guest = t.AddUser("200600");
        var clock = new FakeClock(TestTimes.WednesdayEvening);

        using (var db = t.NewContext())
        {
            await new SettingsService(db).SaveRatesAsync(LoyaltyRates.Default with { MinRewardPostId = 100 });
        }

        var like = new { object_type = "post", object_id = 150, liker_id = 200600 };
        await Controller(t, clock).HandleCallback(Event("like_add", like));
        await Controller(t, clock).HandleCallback(Event("like_add", like)); // повторная доставка VK
        Assert.Equal(1, t.GetUser(guest.Id).DieselCoinsBalance);

        await Controller(t, clock).HandleCallback(Event("like_add", new { object_type = "post", object_id = 99, liker_id = 200600 })); // архивный пост
        await Controller(t, clock).HandleCallback(Event("like_add", new { object_type = "photo", object_id = 160, liker_id = 200600 }));
        await Controller(t, clock).HandleCallback(Event("like_add", new { object_type = "post", object_id = 170, liker_id = 200600 }, groupId: 1)); // чужая группа
        Assert.Equal(1, t.GetUser(guest.Id).DieselCoinsBalance);

        var repost = new { owner_id = 200600, from_id = 200600, copy_history = new[] { new { id = 150, owner_id = -GroupId } } };
        await Controller(t, clock).HandleCallback(Event("wall_repost", repost));
        await Controller(t, clock).HandleCallback(Event("wall_repost", repost));
        var after = t.GetUser(guest.Id);
        Assert.Equal((11, 11), (after.DieselCoinsBalance, after.WeeklyRating));

        // Репост чужой записи не оплачивается
        await Controller(t, clock).HandleCallback(Event("wall_repost",
            new { owner_id = 200600, copy_history = new[] { new { id = 151, owner_id = -777L } } }));
        Assert.Equal(11, t.GetUser(guest.Id).DieselCoinsBalance);

        using var check = t.NewContext();
        Assert.Equal(2, await check.VkActivityLogs.CountAsync());
    }

    [Fact]
    public async Task UnknownUser_IsIgnored()
    {
        using var t = new TestDb();
        var clock = new FakeClock(TestTimes.WednesdayEvening);
        var result = await Controller(t, clock).HandleCallback(Event("like_add", new { object_type = "post", object_id = 5, liker_id = 42 }));
        Assert.Equal("ok", ((ContentResult)result).Content);
        using var db = t.NewContext();
        Assert.Equal(0, await db.PointsTransactions.CountAsync());
    }

    [Fact]
    public async Task GroupJoin_AndMessageAllow_AwardedStrictlyOnce()
    {
        using var t = new TestDb();
        var guest = t.AddUser("300700");
        var clock = new FakeClock(TestTimes.WednesdayEvening);

        await Controller(t, clock).HandleCallback(Event("group_join", new { user_id = 300700, join_type = "join" }));
        await Controller(t, clock).HandleCallback(Event("group_join", new { user_id = 300700, join_type = "join" })); // повторная доставка
        Assert.Equal((20, 5), (t.GetUser(guest.Id).DieselCoinsBalance, t.GetUser(guest.Id).WeeklyRating));

        // Отписался и подписался снова — второго бонуса нет
        clock.Advance(TimeSpan.FromDays(3));
        await Controller(t, clock).HandleCallback(Event("group_join", new { user_id = 300700, join_type = "join" }));
        Assert.Equal(20, t.GetUser(guest.Id).DieselCoinsBalance);

        await Controller(t, clock).HandleCallback(Event("message_allow", new { user_id = 300700, key = "x" }));
        await Controller(t, clock).HandleCallback(Event("message_allow", new { user_id = 300700, key = "x" }));
        Assert.Equal((40, 10), (t.GetUser(guest.Id).DieselCoinsBalance, t.GetUser(guest.Id).WeeklyRating));
    }

    [Fact]
    public async Task PollVote_OncePerPoll_ForeignPollIgnored_NotCappedBySocialLimit()
    {
        using var t = new TestDb();
        var guest = t.AddUser("300800");
        var clock = new FakeClock(TestTimes.WednesdayEvening);

        object Vote(int poll, long owner = -GroupId) => new { owner_id = owner, poll_id = poll, option_id = 1, user_id = 300800 };

        await Controller(t, clock).HandleCallback(Event("poll_vote_new", Vote(7)));
        await Controller(t, clock).HandleCallback(Event("poll_vote_new", Vote(7)));
        Assert.Equal((5, 2), (t.GetUser(guest.Id).DieselCoinsBalance, t.GetUser(guest.Id).WeeklyRating));

        await Controller(t, clock).HandleCallback(Event("poll_vote_new", Vote(8)));
        Assert.Equal(10, t.GetUser(guest.Id).DieselCoinsBalance);

        await Controller(t, clock).HandleCallback(Event("poll_vote_new", Vote(9, owner: -555)));
        Assert.Equal(10, t.GetUser(guest.Id).DieselCoinsBalance);
    }

    [Fact]
    public async Task GuestWithoutConsent_GetsNothing()
    {
        using var t = new TestDb();
        var guest = t.AddUser("300900", consent: false);
        var clock = new FakeClock(TestTimes.WednesdayEvening);

        await Controller(t, clock).HandleCallback(Event("like_add", new { object_type = "post", object_id = 5, liker_id = 300900 }));
        await Controller(t, clock).HandleCallback(Event("group_join", new { user_id = 300900 }));
        Assert.Equal(0, t.GetUser(guest.Id).DieselCoinsBalance);
    }

    [Fact]
    public async Task DailySocialCap_ResetsAtPermMidnight_NotUtc()
    {
        using var t = new TestDb();
        var guest = t.AddUser("301000");
        using (var db = t.NewContext())
        {
            await new SettingsService(db).SaveRatesAsync(LoyaltyRates.Default with { DailySocialCoinsCap = 1 });
        }

        // Среда 23:30 по Перми (18:30 UTC): лимит выбран
        var clock = new FakeClock(PermTime.ToUtc(new DateTime(2026, 11, 4, 23, 30, 0)));
        await Controller(t, clock).HandleCallback(Event("like_add", new { object_type = "post", object_id = 1, liker_id = 301000 }));
        await Controller(t, clock).HandleCallback(Event("like_add", new { object_type = "post", object_id = 2, liker_id = 301000 }));
        Assert.Equal(1, t.GetUser(guest.Id).DieselCoinsBalance);

        // Четверг 00:30 по Перми (19:30 UTC той же даты по UTC): сутки заведения новые
        clock.Advance(TimeSpan.FromHours(1));
        await Controller(t, clock).HandleCallback(Event("like_add", new { object_type = "post", object_id = 3, liker_id = 301000 }));
        Assert.Equal(2, t.GetUser(guest.Id).DieselCoinsBalance);
    }
}
