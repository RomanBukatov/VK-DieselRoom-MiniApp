using DieselRoom.Domain.Entities;
using DieselRoom.Domain.Enums;
using DieselRoom.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace DieselRoom.Infrastructure.Data;

/// <summary>Стартовые данные DIESEL ROOM. Каждый блок сидируется только в пустую таблицу — правки админа не перетираются.</summary>
public static class DbInitializer
{
    public const long VkGroupId = 34613849;

    /// <summary>Банкетное меню скрывается после 31.12.2026 23:59:59 по Перми.</summary>
    public static readonly DateTime BanquetMenuHiddenAfterUtc = PermTime.ToUtc(new DateTime(2026, 12, 31, 23, 59, 59));

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (!await db.PromoBanners.AnyAsync(ct))
        {
            db.PromoBanners.Add(new PromoBanner
            {
                Title = "Розыгрыш Земли от DIESEL ROOM",
                Subtitle = "Смотрите клип и участвуйте в розыгрыше!",
                LinkUrl = "https://vk.ru/clip-225636716_456240088",
                IsActive = true,
                SortOrder = 0,
            });
        }

        if (!await db.MenuLinks.AnyAsync(ct))
        {
            db.MenuLinks.AddRange(
                new MenuLink { Key = "kitchen", Emoji = "🍽", Title = "Меню кухни", Url = "https://vk.ru/album-34613849_258959228", SortOrder = 0 },
                new MenuLink { Key = "bar", Emoji = "🍸", Title = "Барная карта", Url = "https://vk.ru/album-34613849_261865462", SortOrder = 1 },
                new MenuLink
                {
                    Key = "banquet", Emoji = "🥂", Title = "Банкетное меню", Url = "https://vk.ru/album-34613849_298521309",
                    HiddenAfter = BanquetMenuHiddenAfterUtc, SortOrder = 2,
                });
        }

        if (!await db.AfishaEvents.AnyAsync(ct))
        {
            db.AfishaEvents.AddRange(
                new AfishaEvent
                {
                    Title = "НОВЫЙ ГОД В DIESEL ROOM 🎄",
                    Tagline = "Спецпредложение · 5 500 ₽",
                    Description = "Новогодняя ночь с банкетом, шоу-программой и танцами до утра.",
                    ProductUrl = "https://vk.ru/market-34613849?w=product-34613849_9277057",
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
                    AllowBooking = true,
                    DefaultFormat = BookingFormat.Hall,
                    SortOrder = 2,
                });
        }

        if (!await db.ShopItems.AnyAsync(ct))
        {
            db.ShopItems.AddRange(
                new ShopItem { Title = "Кофе на выбор", Icon = "☕", Description = "Любой кофе из барной карты", PriceCoins = 60, StockCount = 100, SortOrder = 0 },
                new ShopItem { Title = "Десерт дня", Icon = "🍰", Description = "Десерт от шефа", PriceCoins = 120, StockCount = 50, SortOrder = 1 },
                new ShopItem { Title = "Бокал пива 0.5", Icon = "🍺", Description = "Разливное пиво на выбор", PriceCoins = 150, StockCount = 80, SortOrder = 2 },
                new ShopItem { Title = "Фирменный коктейль", Icon = "🍹", Description = "Коктейль от бармена", PriceCoins = 250, StockCount = 40, SortOrder = 3 },
                new ShopItem { Title = "Кальян", Icon = "💨", Description = "Классический кальян (Пт/Сб)", PriceCoins = 600, StockCount = 15, SortOrder = 4 },
                new ShopItem { Title = "Час караоке", Icon = "🎤", Description = "Час караоке для компании", PriceCoins = 900, StockCount = 10, SortOrder = 5 });
        }

        await db.SaveChangesAsync(ct);
    }
}
