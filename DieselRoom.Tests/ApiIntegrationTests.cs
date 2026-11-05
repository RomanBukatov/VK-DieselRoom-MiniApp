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
    public FakeCommunityApi Community { get; } = new();

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
            services.RemoveAll<IVkCommunityApi>();
            services.AddSingleton<IVkCommunityApi>(Community);
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

/// <summary>Что ответил бы API ВК: кто подписан и кто разрешил сообщения.</summary>
public sealed class FakeCommunityApi : IVkCommunityApi
{
    public HashSet<string> Members { get; } = [];
    public HashSet<string> MessagesAllowed { get; } = [];
    public bool Unavailable { get; set; }

    public Task<bool?> IsMemberAsync(string vkId, CancellationToken ct = default) =>
        Task.FromResult<bool?>(Unavailable ? null : Members.Contains(vkId));

    public Task<bool?> AreMessagesAllowedAsync(string vkId, CancellationToken ct = default) =>
        Task.FromResult<bool?>(Unavailable ? null : MessagesAllowed.Contains(vkId));
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

    private static async Task<JsonElement> Accept(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/users/me/consent", new { acceptTerms = true, personalData = true, adult = true });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJson(response);
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
        await Accept(guestClient);
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
        await Accept(guest);
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
        Assert.Equal("Бесплатный бизнес-ланч", first.GetProperty("privilegeLabel").GetString());
        Assert.True(first.GetProperty("isMe").GetBoolean());
        Assert.Equal(7, board.GetProperty("tiers").GetArrayLength());
    }

    [Fact]
    public async Task Consent_IsRequired_NamesNotStoredBefore_AndDateIsSaved()
    {
        var client = _app.ClientFor("2101");
        var before = await ReadJson(await client.PostAsJsonAsync("/api/users/me", new { firstName = "Анна", lastName = "К", photoUrl = "https://vk.ru/a.jpg" }));
        Assert.True(before.GetProperty("consentRequired").GetBoolean());
        Assert.Equal("Гость", before.GetProperty("firstName").GetString());
        Assert.Equal(JsonValueKind.Null, before.GetProperty("photoUrl").ValueKind);

        // Без согласия нельзя ни брони, ни профиля, ни обмена, ни кода
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/users/me/profile", new { firstName = "Анна", phone = "89020000001" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/bookings", new { date = "2026-11-07", time = "21:30", guestsCount = 2, format = "Hall", name = "Анна", phone = "89020000001" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/shop/items/1/purchase", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users/me/privilege-code")).StatusCode);

        // Все три галочки обязательны
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/users/me/consent", new { acceptTerms = true, personalData = true, adult = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/users/me/consent", new { acceptTerms = true, personalData = false, adult = true })).StatusCode);

        var after = await Accept(client);
        Assert.False(after.GetProperty("consentRequired").GetBoolean());
        Assert.EndsWith("Z", after.GetProperty("consentAcceptedAt").GetString());
        using (var db = _app.NewDb())
        {
            var user = await db.Users.SingleAsync(u => u.VkId == "2101");
            Assert.NotNull(user.AdultConfirmedAt);
            Assert.Equal("2026-11", user.ConsentVersion);
        }

        var synced = await ReadJson(await client.PostAsJsonAsync("/api/users/me", new { firstName = "Анна", lastName = "К", photoUrl = "https://vk.ru/a.jpg" }));
        Assert.Equal("Анна", synced.GetProperty("firstName").GetString());
        Assert.Equal("https://vk.ru/a.jpg", synced.GetProperty("photoUrl").GetString());
    }

    [Fact]
    public async Task ProfileForm_PaysBonusOnce_KeepsNameFromVkSync_RejectsTakenPhone()
    {
        var client = _app.ClientFor("2102");
        await Accept(client);
        var saved = await client.PutAsJsonAsync("/api/users/me/profile", new { firstName = "Иван", lastName = "Петров", phone = "+7 902 000-00-02" });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var profile = await ReadJson(saved);
        Assert.Equal(30, profile.GetProperty("coins").GetInt32());
        Assert.Equal(5, profile.GetProperty("weeklyRating").GetInt32());
        Assert.Equal("79020000002", profile.GetProperty("phone").GetString());
        Assert.True(profile.GetProperty("profileCompleted").GetBoolean());
        Assert.Contains(profile.GetProperty("tasks").EnumerateArray(), x => x.GetProperty("key").GetString() == "profile" && x.GetProperty("done").GetBoolean());

        var again = await ReadJson(await client.PutAsJsonAsync("/api/users/me/profile", new { firstName = "Ваня", phone = "+7 902 000-00-02" }));
        Assert.Equal(30, again.GetProperty("coins").GetInt32());
        Assert.Equal("Ваня", again.GetProperty("firstName").GetString());

        // Запуск из ВК присылает имя из профиля ВК: то, что гость указал сам, не затирается
        var synced = await ReadJson(await client.PostAsJsonAsync("/api/users/me", new { firstName = "Vanya" }));
        Assert.Equal("Ваня", synced.GetProperty("firstName").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/users/me/profile", new { firstName = "Иван", phone = "123" })).StatusCode);

        var other = _app.ClientFor("2103");
        await Accept(other);
        Assert.Equal(HttpStatusCode.Conflict, (await other.PutAsJsonAsync("/api/users/me/profile", new { firstName = "Олег", phone = "89020000002" })).StatusCode);
    }

    [Fact]
    public async Task VerifyTasks_AwardsOnlyByVkAnswer_AndOnce()
    {
        var client = _app.ClientFor("2104");
        await Accept(client);

        var nothing = await ReadJson(await client.PostAsync("/api/users/me/tasks/verify", null));
        Assert.Equal(0, nothing.GetProperty("coins").GetInt32());

        _app.Community.Members.Add("2104");
        var joined = await ReadJson(await client.PostAsync("/api/users/me/tasks/verify", null));
        Assert.Equal(20, joined.GetProperty("coins").GetInt32());

        _app.Community.MessagesAllowed.Add("2104");
        await client.PostAsync("/api/users/me/tasks/verify", null);
        var twice = await ReadJson(await client.PostAsync("/api/users/me/tasks/verify", null));
        Assert.Equal(40, twice.GetProperty("coins").GetInt32());
    }

    [Fact]
    public async Task PrivilegeFlow_GuestCode_StaffLookupByQr_RedeemWithCodeOnly()
    {
        AddStaff("Официант Код", StaffRole.Staff, vkId: "4646");
        var guestClient = _app.ClientFor("2105");
        await Accept(guestClient);
        var me = await ReadJson(await guestClient.GetAsync("/api/users/me"));
        var userId = me.GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.NotFound, (await guestClient.GetAsync("/api/users/me/privilege-code")).StatusCode); // вне топ-50

        using (var db = _app.NewDb())
        {
            await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s
                .SetProperty(u => u.WeeklyRating, 90000).SetProperty(u => u.WeeklyRatingUpdatedAt, TestTimes.WednesdayEvening));
        }

        var code = await ReadJson(await guestClient.GetAsync("/api/users/me/privilege-code"));
        var value = code.GetProperty("code").GetString()!;
        Assert.Equal("P-" + value, code.GetProperty("qrValue").GetString());
        Assert.InRange(code.GetProperty("secondsLeft").GetInt32(), 1, 60);
        Assert.NotEqual(me.GetProperty("cardNumber").GetString(), value);

        var staff = _app.ClientFor("4646");
        var lookup = await ReadJson(await staff.GetAsync($"/api/staff/lookup?q=P-{value}"));
        Assert.Equal(userId, lookup.GetProperty("guests")[0].GetProperty("userId").GetInt32());
        Assert.Equal(value, lookup.GetProperty("privilegeCode").GetString());
        Assert.Contains(userId, lookup.GetProperty("privilegeCodeUserIds").EnumerateArray().Select(i => i.GetInt32()));

        // По номеру карты гостя найти можно, но кода в ответе нет и выдать без него нельзя
        var byCard = await ReadJson(await staff.GetAsync($"/api/staff/lookup?q={me.GetProperty("cardNumber").GetString()}"));
        Assert.Equal(JsonValueKind.Null, byCard.GetProperty("privilegeCode").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync("/api/staff/privileges/redeem", new { userId })).StatusCode);

        // В среду 18:00 бизнес-ланч лидеру не выдаётся: проверяем в окно Пн 13:00 с новым кодом
        _app.Clock.Now = new DateTimeOffset(new DateTime(2026, 11, 9, 8, 0, 0, DateTimeKind.Utc));
        var lunchCode = (await ReadJson(await guestClient.GetAsync("/api/users/me/privilege-code"))).GetProperty("code").GetString();
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsJsonAsync("/api/staff/privileges/redeem", new { userId, code = lunchCode })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync("/api/staff/privileges/redeem", new { userId, code = lunchCode })).StatusCode);

        using (var db = _app.NewDb())
        {
            await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.WeeklyRating, 0));
        }
    }

    [Fact]
    public async Task Staff_CannotAccrueVisitToSelf_ViaApi()
    {
        AddStaff("Самозванец", StaffRole.Staff, vkId: "4747");
        var staff = _app.ClientFor("4747");
        var me = await ReadJson(await staff.GetAsync("/api/users/me"));
        var response = await staff.PostAsJsonAsync("/api/staff/visits", new { userId = me.GetProperty("id").GetInt32(), receiptNumber = "SELF-API", type = "Regular" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("самому себе", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AdminAdjust_RequiresReason_WritesHistoryAndAudit_StaffForbidden()
    {
        AddStaff("Официант Без Прав", StaffRole.Staff, vkId: "4848");
        var guestClient = _app.ClientFor("2106");
        await Accept(guestClient);
        var me = await ReadJson(await guestClient.GetAsync("/api/users/me"));
        var id = me.GetProperty("id").GetInt32();
        var admin = _app.ClientFor("1001");

        Assert.Equal(HttpStatusCode.Forbidden, (await _app.ClientFor("4848").PostAsJsonAsync($"/api/admin/guests/{id}/adjust", new { coins = 50, rating = 0, reason = "хочу" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/admin/guests/{id}/adjust", new { coins = 50, rating = 0, reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/admin/guests/{id}/adjust", new { coins = 0, rating = 0, reason = "пусто" })).StatusCode);

        var added = await ReadJson(await admin.PostAsJsonAsync($"/api/admin/guests/{id}/adjust", new { coins = 50, rating = 7, reason = "Компенсация за сбой" }));
        Assert.Equal((50, 7), (added.GetProperty("coins").GetInt32(), added.GetProperty("weeklyRating").GetInt32()));

        // В минус баланс не уходит, рейтинг ограничен нулём
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/admin/guests/{id}/adjust", new { coins = -51, rating = 0, reason = "Слишком много" })).StatusCode);
        var lowered = await ReadJson(await admin.PostAsJsonAsync($"/api/admin/guests/{id}/adjust", new { coins = -50, rating = -100, reason = "Ошибочное начисление" }));
        Assert.Equal((0, 0), (lowered.GetProperty("coins").GetInt32(), lowered.GetProperty("weeklyRating").GetInt32()));

        var history = await ReadJson(await guestClient.GetAsync("/api/users/me/transactions"));
        Assert.Contains(history.EnumerateArray(), t => t.GetProperty("description").GetString()!.Contains("Компенсация за сбой"));

        var audit = await ReadJson(await admin.GetAsync("/api/admin/audit?action=GuestAdjusted"));
        Assert.Contains(audit.EnumerateArray(), a => a.GetProperty("details").GetString()!.Contains("Баллы +50, рейтинг +7. Причина: Компенсация за сбой"));

        // Себе администратор начислить не может
        var adminMe = await ReadJson(await admin.GetAsync("/api/users/me"));
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"/api/admin/guests/{adminMe.GetProperty("id").GetInt32()}/adjust", new { coins = 1, rating = 0, reason = "Себе" })).StatusCode);

        var list = await ReadJson(await admin.GetAsync($"/api/admin/guests?q={me.GetProperty("cardNumber").GetString()}"));
        Assert.Equal(id, Assert.Single(list.EnumerateArray()).GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Levels_AdminConfigures_GuestSeesProgress()
    {
        var admin = _app.ClientFor("1001");
        var levels = await ReadJson(await admin.GetAsync("/api/admin/levels"));
        Assert.Equal(["Гость", "Свой", "Резидент", "Легенда Дизеля"], levels.EnumerateArray().Select(l => l.GetProperty("title").GetString()!).ToArray());

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/admin/levels", new[] { new { title = "А", minRating = 5 }, new { title = "Б", minRating = 3 } })).StatusCode);

        var guest = _app.ClientFor("2107");
        var me = await ReadJson(await guest.GetAsync("/api/users/me"));
        Assert.Equal("Гость", me.GetProperty("level").GetProperty("title").GetString());
        Assert.Equal(100, me.GetProperty("level").GetProperty("pointsToNext").GetInt32());

        var updated = new[] { new { title = "Гость", minRating = 0 }, new { title = "Свой", minRating = 200 }, new { title = "Резидент", minRating = 500 }, new { title = "Легенда Дизеля", minRating = 1500 } };
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/admin/levels", updated)).StatusCode);
        var after = await ReadJson(await guest.GetAsync("/api/users/me"));
        Assert.Equal(200, after.GetProperty("level").GetProperty("pointsToNext").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/admin/levels", levels)).StatusCode);
    }

    [Fact]
    public async Task MediaUpload_ValidatesContent_ServedPublicly_UsableAsPrizePhoto()
    {
        var admin = _app.ClientFor("1001");
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
        var dataUrl = "data:image/png;base64," + Convert.ToBase64String(png);

        Assert.Equal(HttpStatusCode.Forbidden, (await _app.ClientFor("5151").PostAsJsonAsync("/api/admin/media", new { dataUrl })).StatusCode);
        // Содержимое не картинка, хотя заявлено как png
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/media", new { dataUrl = "data:image/png;base64," + Convert.ToBase64String("<svg onload=x>"u8.ToArray()) })).StatusCode);

        var url = (await ReadJson(await admin.PostAsJsonAsync("/api/admin/media", new { dataUrl }))).GetProperty("url").GetString()!;
        Assert.Matches(@"^/api/media/\d+$", url);

        var served = await _app.CreateClient().GetAsync(url); // без подписи ВК: так грузит тег img
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/png", served.Content.Headers.ContentType!.MediaType);
        Assert.Equal(png, await served.Content.ReadAsByteArrayAsync());

        var item = await admin.PostAsJsonAsync("/api/admin/shop/items", new { title = "Тестовый подарок", icon = "gift", imageUrl = url, priceCoins = 10, stockCount = 1, isActive = false, sortOrder = 99, ageRestricted = true });
        Assert.Equal(HttpStatusCode.OK, item.StatusCode);
        var dto = await ReadJson(item);
        Assert.Equal(url, dto.GetProperty("imageUrl").GetString());
        Assert.True(dto.GetProperty("ageRestricted").GetBoolean());
    }

    [Fact]
    public async Task DeleteStaff_RevokesAccess_AuditSurvives_CannotDeleteSelf()
    {
        var id = AddStaff("Уволенный Боря", StaffRole.Staff, vkId: "4949");
        var former = _app.ClientFor("4949");
        Assert.Equal(HttpStatusCode.OK, (await former.GetAsync("/api/staff/session")).StatusCode);

        var admin = _app.ClientFor("1001");
        Assert.Equal(HttpStatusCode.Forbidden, (await former.DeleteAsync($"/api/admin/staff/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/staff/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await former.GetAsync("/api/staff/session")).StatusCode);

        var audit = await ReadJson(await admin.GetAsync("/api/admin/audit?action=StaffChanged"));
        Assert.Contains(audit.EnumerateArray(), a => a.GetProperty("details").GetString()!.Contains("Уволенный Боря» удалён"));

        var selfId = AddStaff("Админ По Списку", StaffRole.Admin, vkId: "5050");
        Assert.Equal(HttpStatusCode.Conflict, (await _app.ClientFor("5050").DeleteAsync($"/api/admin/staff/{selfId}")).StatusCode);
    }

    [Fact]
    public async Task EventDate_SavedByAdmin_ShownToGuest_AndBookingKeepsEventTitle()
    {
        var admin = _app.ClientFor("1001");
        var created = await admin.PostAsJsonAsync("/api/admin/events", new
        {
            title = "Вечер караоке", tagline = "Вход свободный", eventDate = "2026-11-20", eventTime = "21:00", productUrl = "https://vk.ru/x", productLabel = "Программа",
            allowBooking = true, defaultFormat = "Karaoke", isActive = true, sortOrder = 50,
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var ev = await ReadJson(created);
        Assert.Equal("2026-11-20", ev.GetProperty("eventDate").GetString());
        Assert.StartsWith("21:00", ev.GetProperty("eventTime").GetString());
        Assert.Equal("Программа", ev.GetProperty("productLabel").GetString());

        var guest = _app.ClientFor("2108");
        await Accept(guest);
        var booking = await ReadJson(await guest.PostAsJsonAsync("/api/bookings", new
        {
            date = "2026-11-20", time = "21:00", guestsCount = 2, format = "Karaoke", name = "Вера", phone = "89020000008", afishaEventId = ev.GetProperty("id").GetInt32(),
        }));
        Assert.Equal("Вечер караоке", booking.GetProperty("eventTitle").GetString());
        Assert.Contains(_app.Notifier.Sent, m => m.VkId == "1001" && m.Message.Contains("Событие: Вечер караоке"));

        var seeded = await ReadJson(await guest.GetAsync("/api/afisha"));
        var newYear = seeded.GetProperty("events").EnumerateArray().First(e => e.GetProperty("title").GetString() == "НОВЫЙ ГОД В DIESEL ROOM");
        Assert.Equal("2026-12-31", newYear.GetProperty("eventDate").GetString());
        var lunch = seeded.GetProperty("events").EnumerateArray().First(e => e.GetProperty("title").GetString() == "БИЗНЕС-ЛАНЧИ В DIESEL ROOM");
        Assert.Equal("Меню бизнес-ланча", lunch.GetProperty("productLabel").GetString());
    }

    [Fact]
    public async Task ReferralCode_EnteredManually_WithClearErrors()
    {
        var inviter = _app.ClientFor("2109");
        var inviterProfile = await Accept(inviter);
        var code = inviterProfile.GetProperty("referral").GetProperty("code").GetString();

        var friend = _app.ClientFor("2110");
        var friendProfile = await Accept(friend);
        Assert.True(friendProfile.GetProperty("referral").GetProperty("canEnterCode").GetBoolean());

        Assert.Equal(HttpStatusCode.BadRequest, (await friend.PostAsJsonAsync("/api/users/me/referral", new { code = "000" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await friend.PostAsJsonAsync("/api/users/me/referral", new { code = friendProfile.GetProperty("cardNumber").GetString() })).StatusCode);

        var ok = await friend.PostAsJsonAsync("/api/users/me/referral", new { code });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var attached = await ReadJson(ok);
        Assert.True(attached.GetProperty("referral").GetProperty("hasInviter").GetBoolean());
        Assert.False(attached.GetProperty("referral").GetProperty("canEnterCode").GetBoolean());

        Assert.Equal(HttpStatusCode.Conflict, (await friend.PostAsJsonAsync("/api/users/me/referral", new { code })).StatusCode);
    }
}
