using DieselRoom.Domain.Entities;
using DieselRoom.Domain.Enums;
using DieselRoom.Domain.Services;
using DieselRoom.Infrastructure.Data;

namespace DieselRoom.Tests;

public class PermTimeTests
{
    [Fact]
    public void WeeklyReset_IsSunday2359Perm_Which_Is_1859Utc()
    {
        var reset = PermTime.NextWeeklyReset(TestTimes.WednesdayEvening);
        Assert.Equal(new DateTime(2026, 11, 8, 18, 59, 0, DateTimeKind.Utc), reset);
        Assert.Equal(DayOfWeek.Sunday, reset.DayOfWeek);
    }

    [Fact]
    public void WeekStart_IsMonday_AndLastMinuteOfSundayBelongsToNextWeek()
    {
        Assert.Equal(new DateOnly(2026, 11, 2), PermTime.WeekStart(TestTimes.WednesdayEvening));
        Assert.Equal(new DateOnly(2026, 11, 2), PermTime.WeekStart(PermTime.ToUtc(new DateTime(2026, 11, 8, 23, 58, 59))));
        Assert.Equal(new DateOnly(2026, 11, 9), PermTime.WeekStart(PermTime.ToUtc(new DateTime(2026, 11, 8, 23, 59, 0))));
    }

    [Fact]
    public void BanquetMenu_HiddenAfterNewYearEvePerm()
    {
        var link = new MenuLink { IsActive = true, HiddenAfter = DbInitializer.BanquetMenuHiddenAfterUtc };
        Assert.Equal(new DateTime(2026, 12, 31, 18, 59, 59, DateTimeKind.Utc), DbInitializer.BanquetMenuHiddenAfterUtc);
        Assert.True(link.IsVisibleAt(new DateTime(2026, 12, 31, 18, 59, 59, DateTimeKind.Utc)));
        Assert.False(link.IsVisibleAt(new DateTime(2026, 12, 31, 19, 0, 0, DateTimeKind.Utc)));
    }
}

public class PrivilegeCatalogTests
{
    [Theory]
    [InlineData(1, PrivilegeTier.BusinessLunch)]
    [InlineData(2, PrivilegeTier.UnlimitedCoffee)]
    [InlineData(3, PrivilegeTier.Hookah)]
    [InlineData(4, PrivilegeTier.Dessert)]
    [InlineData(10, PrivilegeTier.Dessert)]
    [InlineData(11, PrivilegeTier.BeerOrCoffee)]
    [InlineData(20, PrivilegeTier.BeerOrCoffee)]
    [InlineData(21, PrivilegeTier.TeaPot)]
    [InlineData(30, PrivilegeTier.TeaPot)]
    [InlineData(31, PrivilegeTier.Cocktail)]
    [InlineData(50, PrivilegeTier.Cocktail)]
    [InlineData(51, PrivilegeTier.None)]
    [InlineData(0, PrivilegeTier.None)]
    public void TierForRank_MatchesSpec(int rank, PrivilegeTier expected) =>
        Assert.Equal(expected, PrivilegeCatalog.TierForRank(rank));

    private static readonly DateOnly Week = new(2026, 11, 2);

    /// <summary>Момент по местному времени Перми — в UTC, как его передаёт сервис.</summary>
    private static DateTime Perm(int month, int day, int hour, int minute = 0) => PermTime.ToUtc(new DateTime(2026, month, day, hour, minute, 0));

    private static PrivilegeStamp ThisWeek() => new(Week, Perm(11, 2, 13));

    [Fact]
    public void BusinessLunch_ThreeTimesPerWeek_OnlyWeekdays12To16()
    {
        var mondayLunch = Perm(11, 2, 13);
        Assert.True(PrivilegeCatalog.CheckAvailability(PrivilegeTier.BusinessLunch, [ThisWeek(), ThisWeek()], Week, mondayLunch).Allowed);

        var exhausted = PrivilegeCatalog.CheckAvailability(PrivilegeTier.BusinessLunch, [ThisWeek(), ThisWeek(), ThisWeek()], Week, mondayLunch);
        Assert.False(exhausted.Allowed);
        Assert.Equal(3, exhausted.Used);

        Assert.False(PrivilegeCatalog.CheckAvailability(PrivilegeTier.BusinessLunch, [], Week, Perm(11, 2, 16)).Allowed);
        Assert.False(PrivilegeCatalog.CheckAvailability(PrivilegeTier.BusinessLunch, [], Week, Perm(11, 7, 13)).Allowed); // суббота
        // Прошлая неделя в лимит этой не входит
        var lastWeek = new PrivilegeStamp(Week.AddDays(-7), Perm(10, 26, 13));
        Assert.True(PrivilegeCatalog.CheckAvailability(PrivilegeTier.BusinessLunch, [lastWeek, lastWeek, lastWeek], Week, mondayLunch).Allowed);
    }

    [Fact]
    public void Hookah_OncePerWeek_OnlyFridayOrSaturday()
    {
        Assert.False(PrivilegeCatalog.CheckAvailability(PrivilegeTier.Hookah, [], Week, Perm(11, 4, 20)).Allowed);
        Assert.True(PrivilegeCatalog.CheckAvailability(PrivilegeTier.Hookah, [], Week, Perm(11, 6, 20)).Allowed);
        Assert.False(PrivilegeCatalog.CheckAvailability(PrivilegeTier.Hookah, [new PrivilegeStamp(Week, Perm(11, 6, 20))], Week, Perm(11, 7, 20)).Allowed);
    }

    [Fact]
    public void Coffee_Unlimited()
    {
        Assert.True(PrivilegeCatalog.CheckAvailability(PrivilegeTier.UnlimitedCoffee, Enumerable.Repeat(ThisWeek(), 20), Week, Perm(11, 4, 20)).Allowed);
    }

    [Fact]
    public void Cocktail_Strictly14DaysFromLastRedemption_OnlyOnPartyDays()
    {
        // Выдан в субботу 24.10 в 23:00. Прежняя схема по календарным неделям пустила бы уже через 8 дней
        var last = new PrivilegeStamp(new DateOnly(2026, 10, 19), Perm(10, 24, 23));

        var week = new DateOnly(2026, 11, 2);
        var friday13Days = PrivilegeCatalog.CheckAvailability(PrivilegeTier.Cocktail, [last], week, Perm(11, 6, 22));
        Assert.False(friday13Days.Allowed);
        Assert.Contains("07.11 23:00", friday13Days.Reason);

        // Суббота, но за минуту до конца 14 суток
        Assert.False(PrivilegeCatalog.CheckAvailability(PrivilegeTier.Cocktail, [last], week, Perm(11, 7, 22, 59)).Allowed);
        // Ровно 14 суток спустя
        Assert.True(PrivilegeCatalog.CheckAvailability(PrivilegeTier.Cocktail, [last], week, Perm(11, 7, 23)).Allowed);

        // Только Пт/Сб: в воскресенье нельзя, даже если окно уже прошло
        var sunday = PrivilegeCatalog.CheckAvailability(PrivilegeTier.Cocktail, [last], week, Perm(11, 8, 12));
        Assert.False(sunday.Allowed);
        Assert.Contains("Пт/Сб", sunday.Reason);
    }

    [Fact]
    public void AgeRestriction_OnlyForAlcoholAndHookah()
    {
        var restricted = PrivilegeCatalog.All.Where(p => p.AgeRestricted).Select(p => p.Tier);
        Assert.Equal([PrivilegeTier.Hookah, PrivilegeTier.BeerOrCoffee, PrivilegeTier.Cocktail], restricted);
    }

    [Theory]
    [InlineData(2, PrivilegeTier.BusinessLunch)]
    [InlineData(3, PrivilegeTier.UnlimitedCoffee)]
    [InlineData(4, PrivilegeTier.Hookah)]
    [InlineData(10, PrivilegeTier.Hookah)]
    [InlineData(11, PrivilegeTier.Dessert)]
    [InlineData(21, PrivilegeTier.BeerOrCoffee)]
    [InlineData(31, PrivilegeTier.TeaPot)]
    [InlineData(51, PrivilegeTier.Cocktail)]
    public void NextTierAbove_IsNearestPrizeZone(int rank, PrivilegeTier expected) =>
        Assert.Equal(expected, PrivilegeCatalog.NextTierAbove(rank)!.Tier);

    [Fact]
    public void NextTierAbove_Leader_HasNoGoal() => Assert.Null(PrivilegeCatalog.NextTierAbove(1));

    [Fact]
    public void NoTier_IsNotAllowed() =>
        Assert.False(PrivilegeCatalog.CheckAvailability(PrivilegeTier.None, [], Week, DateTime.UtcNow).Allowed);
}

public class CommentFilterAndCodesTests
{
    [Theory]
    [InlineData("Отличный бар, всем советую!", true)]
    [InlineData("Супер супер", true)]           // 10 букв, 2 слова
    [InlineData("Суперсупер", false)]           // 1 слово
    [InlineData("🔥🔥🔥🔥🔥🔥 круто", false)]       // эмодзи — не буквы
    [InlineData("ок да", false)]
    [InlineData("+++ 12345 !!!", false)]
    public void Comment_Requires10LettersAnd2Words(string text, bool expected) =>
        Assert.Equal(expected, CommentFilter.IsMeaningful(text, out _, out _));

    [Fact]
    public void Comment_NormalizedForCopyPaste() =>
        Assert.Equal(CommentFilter.Normalize("  Отличный   БАР "), CommentFilter.Normalize("отличный бар"));

    [Theory]
    [InlineData("#DR-AB3K", "DR-AB3K")]
    [InlineData("dr-ab3k", "DR-AB3K")]
    [InlineData("DR AB3K", "DR-AB3K")]
    [InlineData("ab3k", "DR-AB3K")]
    [InlineData("DR-AB3", null)]
    [InlineData("", null)]
    public void OrderCode_Normalized(string input, string? expected) => Assert.Equal(expected, Codes.NormalizeOrderCode(input));

    [Fact]
    public void GeneratedOrderCode_HasPrefixAndNoAmbiguousChars()
    {
        var random = new Random(1);
        for (var i = 0; i < 500; i++)
        {
            var code = Codes.GenerateOrderCode(random);
            Assert.Matches("^DR-[A-HJKMNP-Z2-9]{4}$", code);
            Assert.Equal(code, Codes.NormalizeOrderCode("#" + code.ToLowerInvariant()));
        }
    }

    [Theory]
    [InlineData(" 12 34 ", "1234")]
    [InlineData("#a-77/1", "A-77/1")]
    [InlineData("чек;drop", null)]
    [InlineData("   ", null)]
    public void ReceiptNumber_Normalized(string input, string? expected) => Assert.Equal(expected, Codes.NormalizeReceiptNumber(input));

    [Theory]
    [InlineData("+7 (902) 123-45-67", "79021234567")]
    [InlineData("89021234567", "79021234567")]
    [InlineData("9021234567", "79021234567")]
    [InlineData("12345", null)]
    public void Phone_Normalized(string input, string? expected) => Assert.Equal(expected, Codes.NormalizePhone(input));

    [Fact]
    public void Achievements_UnlockByVisits()
    {
        var list = Achievements.Compute(visits: 5, paidReferrals: 0, giftsReceived: 0, bestWeeklyRank: 0);
        Assert.True(list.Single(a => a.Key == "first_visit").Unlocked);
        Assert.True(list.Single(a => a.Key == "visits_5").Unlocked);
        Assert.False(list.Single(a => a.Key == "visits_10").Unlocked);
        Assert.Equal(5, list.Single(a => a.Key == "visits_10").Progress);
    }

    [Fact]
    public void DefaultRates_MatchSpec()
    {
        var r = LoyaltyRates.Default;
        Assert.Equal((1, 1), (r.LikeCoins, r.LikeRating));
        Assert.Equal((3, 3), (r.CommentCoins, r.CommentRating));
        Assert.Equal((10, 10), (r.RepostCoins, r.RepostRating));
        Assert.Equal((20, 10), (r.VisitCoins, r.VisitRating));
        Assert.Equal((100, 5), (r.ReferralCoins, r.ReferralRating));
        Assert.Equal(2, r.MaxPaidCommentsPer24h);
        Assert.Equal((30, 5), (r.ProfileBonusCoins, r.ProfileBonusRating));
        Assert.Equal((20, 5), (r.GroupJoinCoins, r.GroupJoinRating));
        Assert.Equal((20, 5), (r.MessageAllowCoins, r.MessageAllowRating));
        Assert.Equal((5, 2), (r.PollCoins, r.PollRating));
        Assert.Empty(r.Validate());
        Assert.NotEmpty((r with { VisitCoins = -1 }).Validate());
        Assert.NotEmpty((r with { PollCoins = -1 }).Validate());
    }

    [Theory]
    [InlineData("P-482913", "482913", true)]
    [InlineData("р-482913", "482913", true)]
    [InlineData("482 913", "482913", false)]
    [InlineData("482913", "482913", false)]
    [InlineData("4829", null, false)]
    [InlineData("#DR-AB3K", null, false)]
    public void PrivilegeCode_Normalized(string input, string? expected, bool prefixed)
    {
        Assert.Equal(expected, Codes.NormalizePrivilegeCode(input, out var fromQr));
        Assert.Equal(prefixed, fromQr);
    }

    [Theory]
    [InlineData(1, "1 очко")]
    [InlineData(2, "2 очка")]
    [InlineData(5, "5 очков")]
    [InlineData(11, "11 очков")]
    [InlineData(12, "12 очков")]
    [InlineData(21, "21 очко")]
    public void Points_Declension(int n, string expected) => Assert.Equal(expected, RuText.Points(n));

    [Theory]
    [InlineData(0, "Гость", 0, 100)]
    [InlineData(50, "Гость", 50, 50)]
    [InlineData(100, "Свой", 0, 400)]
    [InlineData(500, "Резидент", 0, 1000)]
    [InlineData(1500, "Легенда Дизеля", 100, null)]
    [InlineData(99999, "Легенда Дизеля", 100, null)]
    public void GuestLevel_FromTotalRating(int total, string title, int percent, int? pointsToNext)
    {
        var level = GuestLevels.Compute(total, GuestLevels.Default);
        Assert.Equal((title, percent, pointsToNext), (level.Title, level.ProgressPercent, level.PointsToNext));
    }

    [Fact]
    public void GuestLevels_Validation()
    {
        Assert.Empty(GuestLevels.Validate(GuestLevels.Default));
        Assert.NotEmpty(GuestLevels.Validate([new LevelRule("А", 10), new LevelRule("Б", 20)])); // первый не с нуля
        Assert.NotEmpty(GuestLevels.Validate([new LevelRule("А", 0), new LevelRule("Б", 0)])); // пороги не растут
        Assert.NotEmpty(GuestLevels.Validate([new LevelRule("А", 0)])); // один уровень
        Assert.NotEmpty(GuestLevels.Validate([new LevelRule("", 0), new LevelRule("Б", 5)]));
    }
}
