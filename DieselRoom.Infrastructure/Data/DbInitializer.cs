using DieselRoom.Domain.Entities;
using DieselRoom.Domain.Enums;
using DieselRoom.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace DieselRoom.Infrastructure.Data;

/// <summary>Стартовые данные DIESEL ROOM. Каждый блок сидируется только в пустую таблицу — правки админа не перетираются.</summary>
public static class DbInitializer
{
    public const long VkGroupId = 34613849;
    public const string KitchenMenuUrl = "https://vk.ru/album-34613849_258959228";

    /// <summary>Версия официального каталога подарков (документ «Призы DIESEL ROOM»).</summary>
    public const int CatalogVersion = 2;
    public const string CatalogVersionKey = "catalog.version";

    /// <summary>Банкетное меню скрывается после 31.12.2026 23:59:59 по Перми.</summary>
    public static readonly DateTime BanquetMenuHiddenAfterUtc = PermTime.ToUtc(new DateTime(2026, 12, 31, 23, 59, 59));

    /// <summary>
    /// 16 официальных подарков. Icon — ключ векторной иконки для брендированной заглушки, ImageUrl — превью блюда
    /// из меню (только там, где в меню есть фото именно этого блюда).
    /// </summary>
    public static IReadOnlyList<ShopItem> OfficialCatalog() =>
    [
        Item(1, "Значок DIESEL ROOM", "Фирменный значок с Красным Муравьём", "badge", 200, 50),
        Item(2, "Кофе", "Любой кофе из барной карты", "coffee", 300, 100),
        Item(3, "Бокал пива", "Разливное пиво на выбор", "beer", 350, 100, ageRestricted: true),
        Item(4, "Бокал вина", "Бокал вина из винной карты", "wine", 400, 60, ageRestricted: true),
        Item(5, "Салат «Океаника»", "Тигровые креветки, крокеты из снежного краба, микс салата и соус «Тайгермилк»", "salad", 450, 30,
            "/prizes/salad-oceanika.webp"),
        Item(6, "Сет из 5 шотов водка-редбул", "Пять шотов для компании", "shots", 500, 30, ageRestricted: true),
        Item(7, "Паста «Карбонара»", "Классическая карбонара", "pasta", 550, 30),
        Item(8, "Паста «Шпинатные фетучини с морепродуктами»", "Шпинатные фетучини с морепродуктами", "pasta", 600, 30),
        Item(9, "1 л коктейля виски-кола", "Литр виски-колы на компанию", "cocktail", 650, 30, ageRestricted: true),
        Item(10, "Пицца на выбор", "Любая пицца из меню", "pizza", 700, 30),
        Item(11, "Сет «Мини»", "Закусочный сет «Мини»", "set", 750, 20),
        Item(12, "Брискет из говядины со шпинатным пюре", "Томлёная говядина и шпинатное пюре", "beef", 800, 20),
        Item(13, "Вин Дизель Бургер + Бокал пива", "Фирменный бургер с двумя котлетами, беконом и тигровыми креветками и бокал пива", "burger", 850, 20,
            "/prizes/burger-vin-diesel.webp", ageRestricted: true),
        Item(14, "Кальян на фрукте", "Кальян на фруктовой чаше", "hookah", 900, 15, ageRestricted: true),
        Item(15, "Бутылка водочки «Хаски»", "Бутылка водки «Хаски»", "bottle", 950, 10, ageRestricted: true),
        Item(16, "Сет «БИГ» + 1 литр пива", "Большой закусочный сет и литр пива", "set", 1000, 10, ageRestricted: true),
    ];

    private static ShopItem Item(int order, string title, string description, string icon, int price, int stock, string? image = null, bool ageRestricted = false) =>
        new()
        {
            Title = title,
            Description = description,
            Icon = icon,
            ImageUrl = image,
            PriceCoins = price,
            StockCount = stock,
            AgeRestricted = ageRestricted,
            IsActive = true,
            SortOrder = order,
        };

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (!await db.PromoBanners.AnyAsync(ct))
        {
            db.PromoBanners.Add(new PromoBanner
            {
                Title = "Розыгрыш Земли от DIESEL ROOM",
                Subtitle = "Смотрите клип и участвуйте в розыгрыше",
                LinkUrl = "https://vk.ru/clip-225636716_456240088",
                IsActive = true,
                SortOrder = 0,
            });
        }

        if (!await db.MenuLinks.AnyAsync(ct))
        {
            db.MenuLinks.AddRange(
                new MenuLink { Key = "kitchen", Icon = "utensils", Title = "Меню кухни", Url = KitchenMenuUrl, SortOrder = 0 },
                new MenuLink { Key = "bar", Icon = "martini", Title = "Барная карта", Url = "https://vk.ru/album-34613849_261865462", SortOrder = 1 },
                new MenuLink
                {
                    Key = "banquet", Icon = "wine", Title = "Банкетное меню", Url = "https://vk.ru/album-34613849_298521309",
                    HiddenAfter = BanquetMenuHiddenAfterUtc, SortOrder = 2,
                });
        }

        if (!await db.AfishaEvents.AnyAsync(ct))
        {
            db.AfishaEvents.AddRange(
                new AfishaEvent
                {
                    Title = "НОВЫЙ ГОД В DIESEL ROOM",
                    Tagline = "Спецпредложение · 5 500 ₽",
                    Description = "Новогодняя ночь с банкетом, шоу-программой и танцами до утра.",
                    ProductUrl = "https://vk.ru/market-34613849?w=product-34613849_9277057",
                    EventDate = new DateOnly(2026, 12, 31),
                    AllowBooking = true,
                    DefaultFormat = BookingFormat.Banquet,
                    SortOrder = 0,
                },
                new AfishaEvent
                {
                    Title = "ПЯТНИЦА & СУББОТА: ГЛАВНЫЕ ВЕЧЕРИНКИ И КАРАОКЕ",
                    Tagline = "Каждые выходные · DJ, танцы, караоке до утра · Куйбышева, 47",
                    AllowBooking = true,
                    DefaultFormat = BookingFormat.Karaoke,
                    SortOrder = 1,
                },
                new AfishaEvent
                {
                    Title = "БИЗНЕС-ЛАНЧИ В DIESEL ROOM",
                    Tagline = "Пн–Пт с 12:00 до 16:00 · Сытные обеды и быстрая подача",
                    ProductUrl = KitchenMenuUrl,
                    ProductLabel = "Меню бизнес-ланча",
                    AllowBooking = true,
                    DefaultFormat = BookingFormat.Hall,
                    SortOrder = 2,
                });
        }

        await db.SaveChangesAsync(ct);
        await UpgradeCatalogAsync(db, ct);
        await UpgradeIconsAsync(db, ct);
    }

    /// <summary>
    /// Переход на официальный каталог из 16 подарков: прежние позиции не удаляются (на них ссылаются выданные
    /// купоны), а снимаются с витрины; недостающие официальные — добавляются. Выполняется один раз.
    /// </summary>
    public static async Task UpgradeCatalogAsync(AppDbContext db, CancellationToken ct = default)
    {
        var raw = await db.AppSettings.Where(s => s.Key == CatalogVersionKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
        if (int.TryParse(raw, out var version) && version >= CatalogVersion) return;

        var official = OfficialCatalog();
        var titles = official.Select(i => i.Title).ToHashSet();
        var existing = await db.ShopItems.ToListAsync(ct);

        foreach (var old in existing.Where(i => !titles.Contains(i.Title)))
        {
            old.IsActive = false;
        }

        foreach (var item in official)
        {
            var match = existing.FirstOrDefault(i => i.Title == item.Title);
            if (match == null)
            {
                db.ShopItems.Add(item);
                continue;
            }

            match.Icon = item.Icon;
            match.ImageUrl ??= item.ImageUrl;
            match.PriceCoins = item.PriceCoins;
            match.SortOrder = item.SortOrder;
            match.IsActive = true;
        }

        var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == CatalogVersionKey, ct);
        if (setting == null)
        {
            db.AppSettings.Add(new AppSetting { Key = CatalogVersionKey, Value = CatalogVersion.ToString() });
        }
        else
        {
            setting.Value = CatalogVersion.ToString();
            setting.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Базы, созданные до отказа от эмодзи: в плитках меню вместо символов — ключи векторных иконок.</summary>
    private static async Task UpgradeIconsAsync(AppDbContext db, CancellationToken ct)
    {
        var defaults = new Dictionary<string, string> { ["kitchen"] = "utensils", ["bar"] = "martini", ["banquet"] = "wine" };
        var links = await db.MenuLinks.ToListAsync(ct);
        var changed = false;
        foreach (var link in links.Where(l => !IsIconKey(l.Icon)))
        {
            link.Icon = defaults.GetValueOrDefault(link.Key, "utensils");
            changed = true;
        }

        var newYear = await db.AfishaEvents.FirstOrDefaultAsync(e => e.Title.StartsWith("НОВЫЙ ГОД В DIESEL ROOM ") && e.Title != "НОВЫЙ ГОД В DIESEL ROOM", ct);
        if (newYear != null)
        {
            newYear.Title = "НОВЫЙ ГОД В DIESEL ROOM";
            changed = true;
        }

        if (changed) await db.SaveChangesAsync(ct);
    }

    public static bool IsIconKey(string? value) =>
        !string.IsNullOrEmpty(value) && value.All(c => c is >= 'a' and <= 'z' or '-');
}
