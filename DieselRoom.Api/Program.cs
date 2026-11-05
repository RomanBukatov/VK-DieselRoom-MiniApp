using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using DieselRoom.Api.Json;
using DieselRoom.Api.Models;
using DieselRoom.Api.Services;
using DieselRoom.Infrastructure.Data;
using DieselRoom.Infrastructure.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

static List<string> SplitIds(string? raw) =>
    string.IsNullOrWhiteSpace(raw)
        ? new List<string>()
        : raw.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();

string? Env(string name, string? configKey = null) =>
    builder.Configuration[name] ?? (configKey != null ? builder.Configuration[configKey] : null) ?? Environment.GetEnvironmentVariable(name);

// Конфигурация VK: appsettings + переменные окружения из docker-compose / .env
builder.Services.Configure<VkOptions>(options =>
{
    builder.Configuration.GetSection(VkOptions.SectionName).Bind(options);

    if (Env("VK_ADMIN_IDS") is { Length: > 0 } adminIds) options.AdminVkIds = SplitIds(adminIds);
    if (Env("VK_STAFF_IDS") is { Length: > 0 } staffIds) options.StaffVkIds = SplitIds(staffIds);
    if (long.TryParse(Env("VK_APP_ID"), out var appId)) options.AppId = appId;
    if (long.TryParse(Env("VK_GROUP_ID"), out var groupId)) options.GroupId = groupId;
    if (Env("VK_PROTECTED_KEY") is { Length: > 0 } secret) options.ClientSecret = secret;
    if (Env("VK_COMMUNITY_TOKEN") is { Length: > 0 } token) options.CommunityToken = token;
    if (Env("VK_CALLBACK_CONFIRMATION_CODE") is { Length: > 0 } confirmation) options.CallbackConfirmationCode = confirmation;
    if (Env("VK_CALLBACK_SECRET_KEY") is { Length: > 0 } callbackSecret) options.CallbackSecretKey = callbackSecret;
});

// Роли персонала: администраторы и сотрудники по VK ID, мастер-ключ и «перец» ПИН-кодов — только из окружения
builder.Services.AddSingleton(sp =>
{
    var vk = sp.GetRequiredService<IOptions<VkOptions>>().Value;
    return new StaffAuthOptions
    {
        AdminVkIds = vk.AdminVkIds,
        StaffVkIds = vk.StaffVkIds,
        MasterKey = Env("DIESEL_MASTER_KEY") ?? string.Empty,
        PinPepper = Env("DIESEL_PIN_PEPPER") ?? string.Empty,
    };
});

// Секреты — только из окружения (.env / docker-compose). В Production без них не стартуем: отпечатки ПИН-кодов
// с публичным значением по умолчанию легко подобрать, а без строки подключения API бесполезен.
if (builder.Environment.IsProduction())
{
    string[] required = ["DIESEL_PIN_PEPPER"];
    var missing = required.Where(name => string.IsNullOrWhiteSpace(Env(name))).ToList();
    if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("DefaultConnection"))) missing.Add("ConnectionStrings__DefaultConnection");
    if (missing.Count > 0) throw new InvalidOperationException($"Не заданы обязательные переменные окружения: {string.Join(", ", missing)}.");
}

// PostgreSQL: пул 30 соединений, statement_timeout и idle_in_transaction_session_timeout (DbConnectionDefaults)
var connectionString = DbConnectionDefaults.Apply(builder.Configuration.GetConnectionString("DefaultConnection"));
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IGuestNotifier>(sp => new VkGuestNotifier(
    sp.GetRequiredService<IOptions<VkOptions>>().Value.CommunityToken,
    sp.GetRequiredService<ILogger<VkGuestNotifier>>()));
builder.Services.AddSingleton<IVkCommunityApi>(sp =>
{
    var vk = sp.GetRequiredService<IOptions<VkOptions>>().Value;
    return new VkCommunityApi(vk.CommunityToken, vk.GroupId, sp.GetRequiredService<ILogger<VkCommunityApi>>());
});
builder.Services.AddScoped<StaffAlertService>();
builder.Services.AddScoped<IVkAuthValidator, VkAuthValidator>();
builder.Services.AddScoped<StaffAuthService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped(sp => new ShopService(sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<ILogger<ShopService>>()));
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<VisitService>();
builder.Services.AddScoped<RatingService>();
builder.Services.AddScoped<PrivilegeService>();
builder.Services.AddScoped<ProfileBuilder>();
builder.Services.AddHostedService<LoyaltyBackgroundWorker>();

// Ограничение частоты запросов по IP (вторая линия после limit_req в nginx): чтение — 600/мин, изменения — 120/мин.
// Callback API ВКонтакте не ограничиваем: события приходят пачками с серверов VK.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        if (!context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/api/vk/callback"))
        {
            return RateLimitPartition.GetNoLimiter("unlimited");
        }

        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var isRead = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) || HttpMethods.IsOptions(context.Request.Method);
        return RateLimitPartition.GetFixedWindowLimiter($"{(isRead ? "r" : "w")}:{ip}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = isRead ? 600 : 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Перед API стоит nginx фронтенд-контейнера в docker-сети
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("VkPolicy", policy =>
    {
        policy.SetIsOriginAllowed(origin =>
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
            if (uri.Host is "localhost" or "127.0.0.1") return true;
            string[] allowed = ["vk.com", "vk.ru", "vk.me", "vk-apps.com", "vk-apps.ru", "vk-portal.net", "vkuser.net"];
            return allowed.Any(d => uri.Host.Equals(d, StringComparison.OrdinalIgnoreCase) ||
                                    uri.Host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase));
        })
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials();
    });
});

// Строгий формат дат (ISO 8601, UTC, миллисекунды) — старый iOS WebKit падает на других; перечисления — строками
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new UtcDateTimeConverter());
    options.JsonSerializerOptions.Converters.Add(new NullableUtcDateTimeConverter());
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddOpenApi();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    var context = services.GetRequiredService<AppDbContext>();
    try
    {
        // Интеграционные тесты подменяют PostgreSQL на SQLite: схему создаём по модели, а не миграциями Npgsql
        if (app.Environment.IsEnvironment("Testing")) await context.Database.EnsureCreatedAsync();
        else await context.Database.MigrateAsync();
        await DbInitializer.SeedAsync(context);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Ошибка миграций или сида базы данных.");
    }
}

app.UseForwardedHeaders();
app.UseCors("VkPolicy");
app.UseRateLimiter();

// Медленные запросы — в лог [SLOW_REQ] с идентификатором запроса клиента (X-Client-Request-Id)
app.Use(async (context, next) =>
{
    var stopwatch = Stopwatch.StartNew();
    try
    {
        await next();
    }
    finally
    {
        stopwatch.Stop();
        if (stopwatch.ElapsedMilliseconds > 2000 && context.Request.Path.StartsWithSegments("/api"))
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("SlowRequest").LogWarning(
                "[SLOW_REQ] {Method} {Path} {ElapsedMs}ms Status={Status} ReqId={ReqId}",
                context.Request.Method, context.Request.Path.Value, stopwatch.ElapsedMilliseconds,
                context.Response.StatusCode, context.Request.Headers["X-Client-Request-Id"].ToString());
        }
    }
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.Title = "DIESEL ROOM API";
        options.Theme = ScalarTheme.DeepSpace;
    });
}

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapControllers();

app.Run();

public partial class Program;
