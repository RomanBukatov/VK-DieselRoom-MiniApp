using System.Text.RegularExpressions;
using DieselRoom.Domain.Entities;
using DieselRoom.Domain.Enums;
using DieselRoom.Domain.Services;
using DieselRoom.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DieselRoom.Tests;

public class CatalogTests
{
    /// <summary>Официальный список из документа «Призы DIESEL ROOM»: название → цена в баллах.</summary>
    private static readonly (string Title, int Price)[] Official =
    [
        ("Значок DIESEL ROOM", 200),
        ("Кофе", 300),
        ("Бокал пива", 350),
        ("Бокал вина", 400),
        ("Салат «Океаника»", 450),
        ("Сет из 5 шотов водка-редбул", 500),
        ("Паста «Карбонара»", 550),
        ("Паста «Шпинатные фетучини с морепродуктами»", 600),
        ("1 л коктейля виски-кола", 650),
        ("Пицца на выбор", 700),
        ("Сет «Мини»", 750),
        ("Брискет из говядины со шпинатным пюре", 800),
        ("Вин Дизель Бургер + Бокал пива", 850),
        ("Кальян на фрукте", 900),
        ("Бутылка водочки «Хаски»", 950),
        ("Сет «БИГ» + 1 литр пива", 1000),
    ];

    private static readonly Regex Emoji = new(@"[☀-➿]|\p{Cs}", RegexOptions.Compiled);

    [Fact]
    public async Task FreshDatabase_GetsExactly16OfficialPrizes_InOrder()
    {
        using var t = new TestDb();
        using (var db = t.NewContext()) await DbInitializer.SeedAsync(db);

        using var check = t.NewContext();
        var items = await check.ShopItems.AsNoTracking().Where(i => i.IsActive).OrderBy(i => i.SortOrder).ToListAsync();
        Assert.Equal(Official, items.Select(i => (i.Title, i.PriceCoins)).ToArray());
        Assert.All(items, i =>
        {
            Assert.True(DbInitializer.IsIconKey(i.Icon), $"«{i.Title}»: иконка должна быть ключом, а не символом");
            Assert.True(i.StockCount > 0);
        });
        Assert.Equal(DbInitializer.CatalogVersion.ToString(),
            await check.AppSettings.Where(s => s.Key == DbInitializer.CatalogVersionKey).Select(s => s.Value).SingleAsync());
    }

    [Fact]
    public void Photos_OnlyForDishesThatHaveThemInTheMenu()
    {
        var withPhoto = DbInitializer.OfficialCatalog().Where(i => i.ImageUrl != null).ToDictionary(i => i.Title, i => i.ImageUrl);
        Assert.Equal("/prizes/salad-oceanika.webp", withPhoto["Салат «Океаника»"]);
        Assert.Equal("/prizes/burger-vin-diesel.webp", withPhoto["Вин Дизель Бургер + Бокал пива"]);
        Assert.Equal(2, withPhoto.Count);

        // Файлы превью лежат в клиенте и попадают в сборку
        var root = FindRepoRoot();
        foreach (var url in withPhoto.Values)
        {
            var file = new FileInfo(Path.Combine(root, "diesel-client", "public", url!.TrimStart('/')));
            Assert.True(file.Exists, file.FullName);
            Assert.InRange(file.Length, 1, 120_000); // лёгкие превью, а не исходники меню
        }
    }

    [Fact]
    public async Task ExistingDatabase_OldPrizesHidden_OrdersKept_UpgradeRunsOnce()
    {
        using var t = new TestDb();
        var guest = t.AddUser("100", coins: 500);
        var old = t.AddItem("Час караоке", price: 900, stock: 3);
        using (var db = t.NewContext())
        {
            db.ShopOrders.Add(new ShopOrder { UserId = guest.Id, ShopItemId = old.Id, Code = "DR-OLD1", Title = old.Title, Icon = "🎤", ExpiresAt = DateTime.UtcNow.AddDays(1) });
            db.MenuLinks.Add(new MenuLink { Key = "kitchen", Icon = "🍽", Title = "Меню кухни", Url = "https://vk.ru/album-34613849_258959228" });
            await db.SaveChangesAsync();
        }

        using (var db = t.NewContext()) await DbInitializer.SeedAsync(db);

        using (var check = t.NewContext())
        {
            Assert.False((await check.ShopItems.SingleAsync(i => i.Id == old.Id)).IsActive);
            Assert.Equal(16, await check.ShopItems.CountAsync(i => i.IsActive));
            Assert.Equal(1, await check.ShopOrders.CountAsync());
            Assert.Equal("utensils", (await check.MenuLinks.SingleAsync()).Icon);
        }

        // Админ вернул старую позицию на витрину — повторный старт её не трогает
        using (var db = t.NewContext())
        {
            (await db.ShopItems.SingleAsync(i => i.Id == old.Id)).IsActive = true;
            await db.SaveChangesAsync();
        }
        using (var db = t.NewContext()) await DbInitializer.SeedAsync(db);
        using (var check = t.NewContext())
        {
            Assert.True((await check.ShopItems.SingleAsync(i => i.Id == old.Id)).IsActive);
            Assert.Equal(17, await check.ShopItems.CountAsync());
        }
    }

    [Fact]
    public void ServerTexts_HaveNoEmoji()
    {
        Assert.All(PrivilegeCatalog.All, p =>
        {
            Assert.DoesNotMatch(Emoji, p.Label);
            Assert.True(DbInitializer.IsIconKey(p.Icon), p.Tier.ToString());
        });
        Assert.All(Achievements.Compute(10, 1, 1, 1), a => Assert.True(DbInitializer.IsIconKey(a.Icon), a.Key));
        foreach (var status in Enum.GetValues<BookingStatus>())
        {
            Assert.DoesNotMatch(Emoji, DieselRoom.Infrastructure.Services.BookingService.StatusLabel(status));
        }
        Assert.True(DbInitializer.IsIconKey(PrivilegeCatalog.FirstPlaceStreakPrizeIcon));
    }

    [Theory]
    [InlineData("coffee", true)]
    [InlineData("set-big", true)]
    [InlineData("☕", false)]
    [InlineData("", false)]
    [InlineData("Coffee", false)]
    public void IconKey_Format(string value, bool expected) => Assert.Equal(expected, DbInitializer.IsIconKey(value));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DieselRoomApp.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Корень репозитория не найден.");
    }
}
