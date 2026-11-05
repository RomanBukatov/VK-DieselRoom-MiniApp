using DieselRoom.Domain.Entities;
using DieselRoom.Domain.Enums;
using DieselRoom.Domain.Services;
using DieselRoom.Infrastructure.Data;
using DieselRoom.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DieselRoom.Tests;

/// <summary>Управляемые часы: тесты ставят нужный момент (по UTC).</summary>
public sealed class FakeClock : TimeProvider
{
    public DateTimeOffset Now { get; set; }

    public FakeClock(DateTime utc) => Now = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc));

    public override DateTimeOffset GetUtcNow() => Now;

    public DateTime UtcNow => Now.UtcDateTime;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>Сообщения гостям, которые «ушли бы» в ВК.</summary>
public sealed class RecordingNotifier : IGuestNotifier
{
    public List<(string? VkId, string Message, string Description)> Sent { get; } = new();

    public void Send(string? vkId, string message, string description) => Sent.Add((vkId, message, description));
}

/// <summary>
/// SQLite in-memory — реляционная база для тестов: настоящие транзакции, ExecuteUpdate, CHECK-ограничения и
/// УНИКАЛЬНЫЕ индексы (InMemory-провайдер их не проверяет). Соединение живёт, пока жив объект.
/// </summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public RecordingNotifier Notifier { get; } = new();
    public StaffAuthOptions StaffOptions { get; } = new()
    {
        AdminVkIds = ["1001"],
        StaffVkIds = [],
        MasterKey = "master-key-for-tests-123",
        PinPepper = "test-pepper",
    };

    public TestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    public AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    public void Dispose() => _connection.Dispose();

    // ---------- Фабрики сервисов на новом контексте ----------

    public sealed record Services(
        AppDbContext Db, SettingsService Settings, ShopService Shop, BookingService Bookings, VisitService Visits,
        RatingService Rating, PrivilegeService Privileges, UserService Users, StaffAuthService StaffAuth);

    public Services Create(int seed = 42)
    {
        var db = NewContext();
        var settings = new SettingsService(db);
        var shop = new ShopService(db, NullLogger<ShopService>.Instance, new Random(seed));
        var rating = new RatingService(db, shop, Notifier, settings, NullLogger<RatingService>.Instance);
        return new Services(
            db, settings, shop,
            new BookingService(db, Notifier, new StaffAlertService(db, Notifier, StaffOptions)),
            new VisitService(db, settings, Notifier, NullLogger<VisitService>.Instance),
            rating,
            new PrivilegeService(db, rating, StaffOptions),
            new UserService(db),
            new StaffAuthService(db, StaffOptions));
    }

    // ---------- Данные ----------

    public User AddUser(string vkId, int coins = 0, int weekly = 0, int total = 0, DateTime? weeklyAt = null, string? name = null,
        int? referredBy = null, int visits = 0, DateTime? createdAt = null, bool consent = true)
    {
        using var db = NewContext();
        var user = new User
        {
            VkId = vkId,
            CardNumber = "TMP" + vkId,
            FirstName = name ?? $"Гость{vkId}",
            DieselCoinsBalance = coins,
            TotalCoinsEarned = coins,
            WeeklyRating = weekly,
            WeeklyRatingUpdatedAt = weekly > 0 ? weeklyAt ?? new DateTime(2026, 11, 2, 6, 0, 0, DateTimeKind.Utc) : null,
            TotalRating = total == 0 ? weekly : total,
            ReferredByUserId = referredBy,
            VisitsCount = visits,
            CreatedAt = createdAt ?? new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            ConsentAcceptedAt = consent ? new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc) : null,
            AdultConfirmedAt = consent ? new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc) : null,
            ConsentVersion = consent ? LegalInfo.CurrentVersion : null,
        };
        db.Users.Add(user);
        db.SaveChanges();
        user.CardNumber = UserService.CardNumberFor(user.Id);
        db.SaveChanges();
        return user;
    }

    public User GetUser(int id)
    {
        using var db = NewContext();
        return db.Users.AsNoTracking().First(u => u.Id == id);
    }

    public ShopItem AddItem(string title = "Кофе", int price = 50, int stock = 5, bool active = true)
    {
        using var db = NewContext();
        var item = new ShopItem { Title = title, Icon = "coffee", PriceCoins = price, StockCount = stock, IsActive = active };
        db.ShopItems.Add(item);
        db.SaveChanges();
        return item;
    }

    public ShopItem GetItem(int id)
    {
        using var db = NewContext();
        return db.ShopItems.AsNoTracking().First(i => i.Id == id);
    }

    public StaffMember AddStaff(string name, StaffRole role = StaffRole.Staff, string? vkId = null, string? pin = null, bool active = true)
    {
        using var db = NewContext();
        var member = new StaffMember
        {
            Name = name,
            Role = role,
            VkId = vkId,
            PinHash = pin == null ? null : new StaffAuthService(db, StaffOptions).HashPin(pin),
            IsActive = active,
        };
        db.StaffMembers.Add(member);
        db.SaveChanges();
        return member;
    }

    public static StaffIdentity Waiter => new(StaffRole.Staff, 7, "Официант Анна", "555");

    /// <summary>Выдача привилегии, как у настоящего официанта: с кодом с экрана гостя.</summary>
    public Task<OpResult<PrivilegeRedemption>> Redeem(int userId, DateTime now)
    {
        var s = Create();
        return s.Privileges.RedeemAsync(Waiter, userId, s.Privileges.GetCode(userId, now).Code, now);
    }
}

public static class TestTimes
{
    /// <summary>Понедельник 02.11.2026 13:00 по Перми (08:00 UTC).</summary>
    public static readonly DateTime MondayLunch = PermTime.ToUtc(new DateTime(2026, 11, 2, 13, 0, 0));
    /// <summary>Пятница 06.11.2026 21:00 по Перми.</summary>
    public static readonly DateTime FridayEvening = PermTime.ToUtc(new DateTime(2026, 11, 6, 21, 0, 0));
    /// <summary>Среда 04.11.2026 18:00 по Перми.</summary>
    public static readonly DateTime WednesdayEvening = PermTime.ToUtc(new DateTime(2026, 11, 4, 18, 0, 0));
}
