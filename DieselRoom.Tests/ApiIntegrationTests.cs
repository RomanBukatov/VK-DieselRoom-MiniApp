using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DieselRoom.Api.Services;
using DieselRoom.Domain.Entities;
using DieselRoom.Domain.Enums;
using DieselRoom.Infrastructure.Data;
using DieselRoom.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace DieselRoom.Tests;

/// <summary>Приложение целиком (фильтры ролей, контроллеры, JSON) поверх SQLite in-memory и управляемых часов.</summary>
public sealed class DieselApp : WebApplicationFactory<Program>
{
    public const string Secret = "vk-test-secret";
    public const string MasterKey = "master-key-for-integration";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public FakeClock Clock { get; } = new(TestTimes.WednesdayEvening);
    public RecordingNotifier Notifier { get; } = new();

    public DieselApp()
    {
        _connection.Open();
        StaffAuthService.ResetPinThrottleForTests();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("VkOptions:ClientSecret", Secret);
        builder.UseSetting("VkOptions:CallbackSecretKey", "cb-secret");
        builder.UseSetting("VK_ADMIN_IDS", "1001");
        builder.UseSetting("DIESEL_MASTER_KEY", MasterKey);
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=unused");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
            services.RemoveAll<IHostedService>();
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<IGuestNotifier>();
            services.AddSingleton<IGuestNotifier>(Notifier);
        });
    }

    /// <summary>Контекст тестовой базы; обращение к Services запускает приложение, а оно создаёт схему и сид.</summary>
    public AppDbContext NewDb()
    {
        _ = Services;
        return NewDbUnchecked();
    }

    private AppDbContext NewDbUnchecked() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    /// <summary>Клиент с подписанными параметрами запуска VK (как в настоящем мини-приложении).</summary>
    public HttpClient ClientFor(string vkUserId, string? pin = null, string? masterKey = null)
    {
        var client = CreateClient();
        var parameters = $"vk_app_id=54000000&vk_platform=mobile_web&vk_user_id={vkUserId}";
        client.DefaultRequestHeaders.Add("X-VK-Sign", $"{parameters}&sign={VkAuthValidator.ComputeSign(Secret, parameters)}");
        if (pin != null) client.DefaultRequestHeaders.Add("X-Staff-Pin", pin);
        if (masterKey != null) client.DefaultRequestHeaders.Add("X-Admin-Key", masterKey);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}

public class ApiIntegrationTests : IClassFixture<DieselApp>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly DieselApp _app;

    public ApiIntegrationTests(DieselApp app)
    {
        _app = app;
        _app.Clock.Now = new DateTimeOffset(TestTimes.WednesdayEvening);
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), Json);

    /// <summary>Сотрудник по VK ID. ПИН-сотрудников создаём через API админа (там нужен «перец» приложения).</summary>
    private int AddStaff(string name, StaffRole role, string vkId)
    {
        using var db = _app.NewDb();
        var member = new StaffMember { Name = name, Role = role, VkId = vkId };
        db.StaffMembers.Add(member);
        db.SaveChanges();
        return member.Id;
    }

    [Fact]
    public async Task UnsignedOrForgedRequests_AreRejected()
    {
        var anonymous = _app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/users/me")).StatusCode);

        var forged = _app.CreateClient();
        forged.DefaultRequestHeaders.Add("X-VK-Sign", "vk_app_id=1&vk_user_id=1001&sign=AAAA");
        Assert.Equal(HttpStatusCode.Unauthorized, (await forged.GetAsync("/api/users/me")).StatusCode);

        // X-Test-Vk-Id вне Development не принимается
        var testHeader = _app.CreateClient();
        testHeader.DefaultRequestHeaders.Add("X-Test-Vk-Id", "1001");
        Assert.Equal(HttpStatusCode.Unauthorized, (await testHeader.GetAsync("/api/admin/rates")).StatusCode);
    }

    [Fact]
    public async Task Roles_GuestStaffAdmin_AreSeparated()
    {
        AddStaff("Бармен Олег", StaffRole.Staff, vkId: "4242");

        var guest = _app.ClientFor("5151");
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.GetAsync("/api/staff/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.GetAsync("/api/admin/rates")).StatusCode);

        var staff = _app.ClientFor("4242");
        var session = await ReadJson(await staff.GetAsync("/api/staff/session"));
        Assert.Equal("Staff", session.GetProperty("role").GetString());
        Assert.Equal("Бармен Олег", session.GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/admin/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync("/api/admin/rates", new { visitCoins = 999 })).StatusCode);

        var admin = _app.ClientFor("1001");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/staff/session")).StatusCode); // админу доступно всё сотрудника

        var master = _app.ClientFor("6161", masterKey: DieselApp.MasterKey);
        Assert.Equal(HttpStatusCode.OK, (await master.GetAsync("/api/admin/rates")).StatusCode);

        var profile = await ReadJson(await staff.GetAsync("/api/users/me"));
        Assert.Equal("Staff", profile.GetProperty("role").GetString());
    }

    [Fact]
    public async Task AdminCreatesPinStaff_PinGrantsStaffAccess_AuditShowsChange()
    {
        var admin = _app.ClientFor("1001");
        var created = await admin.PostAsJsonAsync("/api/admin/staff", new { name = "Хостес Ира", role = "Staff", pin = "8642", isActive = true });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var dto = await ReadJson(created);
        Assert.True(dto.GetProperty("hasPin").GetBoolean());
        Assert.False(dto.TryGetProperty("pinHash", out _));

        var duplicatePin = await admin.PostAsJsonAsync("/api/admin/staff", new { name = "Другой", role = "Staff", pin = "8642", isActive = true });
        Assert.Equal(HttpStatusCode.Conflict, duplicatePin.StatusCode);

        var tablet = _app.ClientFor("7171", pin: "8642");
        var session = await ReadJson(await tablet.GetAsync("/api/staff/session"));
        Assert.Equal("Хостес Ира", session.GetProperty("name").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, (await _app.ClientFor("7172", pin: "0000").GetAsync("/api/staff/session")).StatusCode);

        var audit = await ReadJson(await admin.GetAsync("/api/admin/audit?action=StaffChanged"));
        Assert.Contains(audit.EnumerateArray(), a => a.GetProperty("details").GetString()!.Contains("Хостес Ира"));
    }

    [Fact]
    public async Task ShopFlow_Purchase_StaffIssuesByCode_Audit_AndCancelRefund()
    {
        AddStaff("Официант Вика", StaffRole.Staff, vkId: "4343");
        var guestClient = _app.ClientFor("8181");
        var me = await ReadJson(await guestClient.PostAsJsonAsync("/api/users/me", new { firstName = "Пётр", lastName = "Иванов" }));
        var userId = me.GetProperty("id").GetInt32();

        using (var db = _app.NewDb())
        {
            await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.DieselCoinsBalance, 1000));
        }

        var items = await ReadJson(await guestClient.GetAsync("/api/shop/items"));
        var coffee = items.EnumerateArray().First(i => i.GetProperty("title").GetString() == "Кофе");
        var stockBefore = coffee.GetProperty("stockCount").GetInt32();

        var bought = await ReadJson(await guestClient.PostAsync($"/api/shop/items/{coffee.GetProperty("id").GetInt32()}/purchase", null));
        var code = bought.GetProperty("order").GetProperty("code").GetString()!;
        Assert.Equal(700, bought.GetProperty("coins").GetInt32());
        Assert.Equal("Pending", bought.GetProperty("order").GetProperty("status").GetString());
        Assert.EndsWith("Z", bought.GetProperty("order").GetProperty("expiresAt").GetString());

        var staff = _app.ClientFor("4343");
        var lookup = await ReadJson(await staff.GetAsync($"/api/staff/lookup?q=%23{code}"));
        Assert.Equal(userId, lookup.GetProperty("order").GetProperty("userId").GetInt32());

        var issued = await staff.PostAsJsonAsync("/api/staff/coupons/issue", new { code = "#" + code });
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync("/api/staff/coupons/issue", new { code })).StatusCode);

        // Второй подарок отменяем — баллы и остаток возвращаются
        var second = await ReadJson(await guestClient.PostAsync($"/api/shop/items/{coffee.GetProperty("id").GetInt32()}/purchase", null));
        var cancel = await ReadJson(await guestClient.PostAsync($"/api/shop/orders/{second.GetProperty("order").GetProperty("id").GetInt32()}/cancel", null));
        Assert.Equal(700, cancel.GetProperty("coins").GetInt32());

        var orders = await ReadJson(await guestClient.GetAsync("/api/shop/orders"));
        Assert.Equal(["Cancelled", "Issued"], orders.EnumerateArray().Select(o => o.GetProperty("status").GetString()!).ToArray());
        var itemsAfter = await ReadJson(await guestClient.GetAsync("/api/shop/items"));
        Assert.Equal(stockBefore - 1, itemsAfter.EnumerateArray().First(i => i.GetProperty("id").GetInt32() == coffee.GetProperty("id").GetInt32()).GetProperty("stockCount").GetInt32());

        var audit = await ReadJson(await _app.ClientFor("1001").GetAsync("/api/admin/audit?action=GiftIssued"));
        Assert.Contains(audit.EnumerateArray(), a => a.GetProperty("staffName").GetString() == "Официант Вика" && a.GetProperty("details").GetString()!.Contains(code));
    }

    [Fact]
    public async Task VisitByReceipt_IsUnique_AndShowsInAuditWithReceiptNumber()
    {
        AddStaff("Бармен Саша", StaffRole.Staff, vkId: "4444");
        var guest = await ReadJson(await _app.ClientFor("9191").PostAsJsonAsync("/api/users/me", new { firstName = "Олег" }));
        var staff = _app.ClientFor("4444");

        var first = await staff.PostAsJsonAsync("/api/staff/visits", new { userId = guest.GetProperty("id").GetInt32(), receiptNumber = "R-2026-77", type = "BusinessLunch" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var body = await ReadJson(first);
        Assert.Equal(20, body.GetProperty("guest").GetProperty("coins").GetInt32());

        var dup = await staff.PostAsJsonAsync("/api/staff/visits", new { userId = guest.GetProperty("id").GetInt32(), receiptNumber = "r-2026-77", type = "Regular" });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);

        var audit = await ReadJson(await _app.ClientFor("1001").GetAsync("/api/admin/audit?q=R-2026-77"));
        var row = Assert.Single(audit.EnumerateArray());
        Assert.Equal("Бармен Саша", row.GetProperty("staffName").GetString());
        Assert.Equal("VisitAccrued", row.GetProperty("action").GetString());
    }

    [Fact]
    public async Task Afisha_SeedsBannerMenuAndEvents_AndHidesBanquetMenuAfterNewYear()
    {
        var client = _app.ClientFor("1212");
        var afisha = await ReadJson(await client.GetAsync("/api/afisha"));
        Assert.Equal("Розыгрыш Земли от DIESEL ROOM", afisha.GetProperty("banners")[0].GetProperty("title").GetString());
        Assert.Equal("https://vk.ru/clip-225636716_456240088", afisha.GetProperty("banners")[0].GetProperty("linkUrl").GetString());
        Assert.Equal(["kitchen", "bar", "banquet"], afisha.GetProperty("menu").EnumerateArray().Select(m => m.GetProperty("key").GetString()!).ToArray());
        Assert.Equal(3, afisha.GetProperty("events").GetArrayLength());
        Assert.Contains("5 500 ₽", afisha.GetProperty("events")[0].GetProperty("tagline").GetString());

        _app.Clock.Now = new DateTimeOffset(new DateTime(2026, 12, 31, 19, 0, 1, DateTimeKind.Utc));
        var after = await ReadJson(await client.GetAsync("/api/afisha"));
        Assert.Equal(["kitchen", "bar"], after.GetProperty("menu").EnumerateArray().Select(m => m.GetProperty("key").GetString()!).ToArray());
    }

    [Fact]
    public async Task Booking_ViaApi_StaffConfirms_GuestGetsVkMessage()
    {
        AddStaff("Хостес Лена", StaffRole.Staff, vkId: "4545");
        var guest = _app.ClientFor("1313");
        var created = await guest.PostAsJsonAsync("/api/bookings", new
        {
            date = "2026-11-07", time = "21:30", guestsCount = 6, format = "Party", name = "Анна", phone = "89021112233", wishes = "День рождения",
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var booking = await ReadJson(created);
        Assert.Equal("Ожидает подтверждения", booking.GetProperty("statusLabel").GetString());

        var staff = _app.ClientFor("4545");
        var inbox = await ReadJson(await staff.GetAsync("/api/staff/bookings"));
        Assert.Contains(inbox.EnumerateArray(), b => b.GetProperty("id").GetInt32() == booking.GetProperty("id").GetInt32());

        var confirmed = await ReadJson(await staff.PostAsJsonAsync($"/api/staff/bookings/{booking.GetProperty("id").GetInt32()}/confirm", new { comment = "Зал 2" }));
        Assert.Equal("Confirmed", confirmed.GetProperty("status").GetString());
        Assert.Contains(_app.Notifier.Sent, m => m.VkId == "1313" && m.Message.Contains("подтверждена"));

        Assert.Equal(HttpStatusCode.Forbidden, (await guest.PostAsJsonAsync($"/api/staff/bookings/{booking.GetProperty("id").GetInt32()}/reject", new { })).StatusCode);
    }

    [Fact]
    public async Task Leaderboard_ShowsPrivilegeLabelsOnRows()
    {
        using (var db = _app.NewDb())
        {
            db.Users.Add(new User { VkId = "1414", CardNumber = "L1414", FirstName = "Лидер", WeeklyRating = 5000, TotalRating = 5000, WeeklyRatingUpdatedAt = TestTimes.MondayLunch });
            db.SaveChanges();
        }
        var board = await ReadJson(await _app.ClientFor("1414").GetAsync("/api/ratings/weekly"));
        var first = board.GetProperty("rows")[0];
        Assert.Equal(1, first.GetProperty("rank").GetInt32());
        Assert.Equal("Бизнес-ланч бесплатно", first.GetProperty("privilegeLabel").GetString());
        Assert.True(first.GetProperty("isMe").GetBoolean());
        Assert.Equal(7, board.GetProperty("tiers").GetArrayLength());
    }
}
