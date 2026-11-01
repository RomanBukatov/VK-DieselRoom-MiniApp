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

    [Fact]
    public void BusinessLunch_ThreeTimesPerWeek_OnlyWeekdays12To16()
    {
        var mondayLunch = new DateTime(2026, 11, 2, 13, 0, 0);
        Assert.True(PrivilegeCatalog.CheckAvailability(PrivilegeTier.BusinessLunch, [Week, Week], Week, mondayLunch).Allowed);

        var exhausted = PrivilegeCatalog.CheckAvailability(PrivilegeTier.BusinessLunch, [Week, Week, Week], Week, mondayLunch);
        Assert.False(exhausted.Allowed);
        Assert.Equal(3, exhausted.Used);

        Assert.False(PrivilegeCatalog.CheckAvailability(PrivilegeTier.BusinessLunch, [], Week, new DateTime(2026, 11, 2, 16, 0, 0)).Allowed);
        Assert.False(PrivilegeCatalog.CheckAvailability(PrivilegeTier.BusinessLunch, [], Week, new DateTime(2026, 11, 7, 13, 0, 0)).Allowed); // суббота
        // Прошлая неделя в лимит этой не входит
        Assert.True(PrivilegeCatalog.CheckAvailability(PrivilegeTier.BusinessLunch, [Week.AddDays(-7), Week.AddDays(-7), Week.AddDays(-7)], Week, mondayLunch).Allowed);
    }

    [Fact]
    public void Hookah_OncePerWeek_OnlyFridayOrSaturday()
    {
        Assert.False(PrivilegeCatalog.CheckAvailability(PrivilegeTier.Hookah, [], Week, new DateTime(2026, 11, 4, 20, 0, 0)).Allowed);
        Assert.True(PrivilegeCatalog.CheckAvailability(PrivilegeTier.Hookah, [], Week, new DateTime(2026, 11, 6, 20, 0, 0)).Allowed);
        Assert.False(PrivilegeCatalog.CheckAvailability(PrivilegeTier.Hookah, [Week], Week, new DateTime(2026, 11, 7, 20, 0, 0)).Allowed);
    }

    [Fact]
    public void Coffee_Unlimited_Cocktail_OncePerTwoWeeks()
    {
        var any = new DateTime(2026, 11, 4, 20, 0, 0);
        Assert.True(PrivilegeCatalog.CheckAvailability(PrivilegeTier.UnlimitedCoffee, Enumerable.Repeat(Week, 20), Week, any).Allowed);

        Assert.False(PrivilegeCatalog.CheckAvailability(PrivilegeTier.Cocktail, [Week.AddDays(-7)], Week, any).Allowed);
        Assert.True(PrivilegeCatalog.CheckAvailability(PrivilegeTier.Cocktail, [Week.AddDays(-14)], Week, any).Allowed);
    }

    [Fact]
    public void NoTier_IsNotAllowed() =>
        Assert.False(PrivilegeCatalog.CheckAvailability(PrivilegeTier.None, [], Week, DateTime.Now).Allowed);
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
        Assert.Empty(r.Validate());
        Assert.NotEmpty((r with { VisitCoins = -1 }).Validate());
    }
}
