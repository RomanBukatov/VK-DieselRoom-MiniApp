using System.Text.Json;
using System.Text.RegularExpressions;
using DieselRoom.Api.DTOs;
using DieselRoom.Api.Extensions;
using DieselRoom.Api.Filters;
using DieselRoom.Domain.Entities;
using DieselRoom.Domain.Enums;
using DieselRoom.Domain.Services;
using DieselRoom.Infrastructure.Data;
using DieselRoom.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DieselRoom.Api.Controllers;

/// <summary>
/// Панель администратора (Стас, Фёдор): афиша, баннер и ссылки меню, каталог и фото подарков, ставки и уровни,
/// гости с ручной корректировкой, сотрудники (VK ID / ПИН-коды) и журнал аудита персонала.
/// </summary>
[ApiController]
[Route("api/admin")]
[VkAuthorize(MinRole = StaffRole.Admin)]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly SettingsService _settings;
    private readonly StaffAuthService _staffAuth;
    private readonly TimeProvider _clock;

    public AdminController(AppDbContext db, SettingsService settings, StaffAuthService staffAuth, TimeProvider clock)
    {
        _db = db;
        _settings = settings;
        _staffAuth = staffAuth;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;
    private StaffIdentity Admin => HttpContext.GetStaff();
    private ObjectResult Error(string message, int status = 400) => StatusCode(status, new { message });

    private async Task SaveWithAuditAsync(string action, string details, CancellationToken ct)
    {
        StaffAudit.Add(_db, Admin, action, details: details, now: Now);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Фото подарка: файл из клиента (/prizes/...) или загруженное через админку (/api/media/...).</summary>
    private static bool IsPrizeImagePath(string path) =>
        Regex.IsMatch(path, @"^(/prizes/[a-z0-9-]{1,60}\.(webp|jpg|png)|/api/media/\d{1,10})$");

    private static string? ValidUrl(string? url) =>
        string.IsNullOrWhiteSpace(url) ? null
        : Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri.ToString() : "invalid";

    // ---------------- Афиша: баннер ----------------

    [HttpGet("banners")]
    public async Task<IEnumerable<BannerDto>> Banners(CancellationToken ct) =>
        (await _db.PromoBanners.AsNoTracking().OrderBy(b => b.SortOrder).ThenBy(b => b.Id).ToListAsync(ct)).Select(BannerDto.From);

    [HttpPost("banners")]
    public Task<ActionResult<BannerDto>> CreateBanner([FromBody] UpsertBannerRequest request, CancellationToken ct) =>
        UpsertBanner(null, request, ct);

    [HttpPut("banners/{id:int}")]
    public Task<ActionResult<BannerDto>> UpdateBanner(int id, [FromBody] UpsertBannerRequest request, CancellationToken ct) =>
        UpsertBanner(id, request, ct);

    private async Task<ActionResult<BannerDto>> UpsertBanner(int? id, UpsertBannerRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Title) || r.Title.Trim().Length > 200) return Error("Заголовок баннера: от 1 до 200 символов.");
        var link = ValidUrl(r.LinkUrl);
        var image = ValidUrl(r.ImageUrl);
        if (link == "invalid" || image == "invalid") return Error("Ссылки должны начинаться с https://");

        var banner = id.HasValue ? await _db.PromoBanners.FirstOrDefaultAsync(b => b.Id == id, ct) : new PromoBanner();
        if (banner == null) return Error("Баннер не найден.", 404);
        banner.Title = r.Title.Trim();
        banner.Subtitle = string.IsNullOrWhiteSpace(r.Subtitle) ? null : r.Subtitle.Trim();
        banner.LinkUrl = link;
        banner.ImageUrl = image;
        banner.IsActive = r.IsActive;
        banner.SortOrder = r.SortOrder;
        if (!id.HasValue) _db.PromoBanners.Add(banner);
        await SaveWithAuditAsync(StaffAuditActions.AfishaChanged, $"Баннер «{banner.Title}» {(id.HasValue ? "изменён" : "создан")}", ct);
        return BannerDto.From(banner);
    }

    [HttpDelete("banners/{id:int}")]
    public async Task<IActionResult> DeleteBanner(int id, CancellationToken ct)
    {
        var banner = await _db.PromoBanners.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (banner == null) return Error("Баннер не найден.", 404);
        _db.PromoBanners.Remove(banner);
        await SaveWithAuditAsync(StaffAuditActions.AfishaChanged, $"Баннер «{banner.Title}» удалён", ct);
        return NoContent();
    }

    // ---------------- Ссылки меню ----------------

    [HttpGet("menu")]
    public async Task<IEnumerable<MenuLinkDto>> Menu(CancellationToken ct) =>
        (await _db.MenuLinks.AsNoTracking().OrderBy(m => m.SortOrder).ThenBy(m => m.Id).ToListAsync(ct)).Select(MenuLinkDto.From);

    [HttpPut("menu/{id:int}")]
    public async Task<ActionResult<MenuLinkDto>> UpdateMenu(int id, [FromBody] UpdateMenuLinkRequest r, CancellationToken ct)
    {
        var link = await _db.MenuLinks.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (link == null) return Error("Кнопка меню не найдена.", 404);
        var url = ValidUrl(r.Url);
        if (url is null or "invalid") return Error("Ссылка должна начинаться с https://");
        if (string.IsNullOrWhiteSpace(r.Title) || r.Title.Trim().Length > 100) return Error("Название кнопки: от 1 до 100 символов.");

        link.Title = r.Title.Trim();
        link.Icon = string.IsNullOrWhiteSpace(r.Icon) ? link.Icon : r.Icon.Trim();
        link.Url = url;
        link.HiddenAfter = r.HiddenAfter.HasValue ? DateTime.SpecifyKind(r.HiddenAfter.Value, DateTimeKind.Utc) : null;
        link.IsActive = r.IsActive;
        link.SortOrder = r.SortOrder;
        await SaveWithAuditAsync(StaffAuditActions.AfishaChanged, $"Кнопка меню «{link.Title}» → {link.Url}", ct);
        return MenuLinkDto.From(link);
    }

    // ---------------- Карточки афиши ----------------

    [HttpGet("events")]
    public async Task<IEnumerable<AfishaEventDto>> Events(CancellationToken ct) =>
        (await _db.AfishaEvents.AsNoTracking().OrderBy(e => e.SortOrder).ThenBy(e => e.Id).ToListAsync(ct)).Select(AfishaEventDto.From);

    [HttpPost("events")]
    public Task<ActionResult<AfishaEventDto>> CreateEvent([FromBody] UpsertAfishaEventRequest r, CancellationToken ct) => UpsertEvent(null, r, ct);

    [HttpPut("events/{id:int}")]
    public Task<ActionResult<AfishaEventDto>> UpdateEvent(int id, [FromBody] UpsertAfishaEventRequest r, CancellationToken ct) => UpsertEvent(id, r, ct);

    private async Task<ActionResult<AfishaEventDto>> UpsertEvent(int? id, UpsertAfishaEventRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Title) || r.Title.Trim().Length > 200) return Error("Название мероприятия: от 1 до 200 символов.");
        if (r.Description is { Length: > 2000 }) return Error("Описание — не длиннее 2000 символов.");
        var product = ValidUrl(r.ProductUrl);
        var image = ValidUrl(r.ImageUrl);
        if (product == "invalid" || image == "invalid") return Error("Ссылки должны начинаться с https://");
        if (!Enum.IsDefined(r.DefaultFormat)) return Error("Неизвестный формат брони.");
        if (r.ProductLabel is { Length: > 60 }) return Error("Подпись кнопки — не длиннее 60 символов.");

        var ev = id.HasValue ? await _db.AfishaEvents.FirstOrDefaultAsync(e => e.Id == id, ct) : new AfishaEvent { CreatedAt = Now };
        if (ev == null) return Error("Мероприятие не найдено.", 404);
        ev.Title = r.Title.Trim();
        ev.Tagline = string.IsNullOrWhiteSpace(r.Tagline) ? null : r.Tagline.Trim();
        ev.Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
        ev.ProductUrl = product;
        ev.ProductLabel = string.IsNullOrWhiteSpace(r.ProductLabel) ? null : r.ProductLabel.Trim();
        ev.ImageUrl = image;
        ev.EventDate = r.EventDate;
        ev.EventTime = r.EventDate.HasValue ? r.EventTime : null;
        ev.AllowBooking = r.AllowBooking;
        ev.DefaultFormat = r.DefaultFormat;
        ev.IsActive = r.IsActive;
        ev.SortOrder = r.SortOrder;
        if (!id.HasValue) _db.AfishaEvents.Add(ev);
        await SaveWithAuditAsync(StaffAuditActions.AfishaChanged, $"Афиша «{ev.Title}» {(id.HasValue ? "изменена" : "создана")}", ct);
        return AfishaEventDto.From(ev);
    }

    [HttpDelete("events/{id:int}")]
    public async Task<IActionResult> DeleteEvent(int id, CancellationToken ct)
    {
        var ev = await _db.AfishaEvents.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (ev == null) return Error("Мероприятие не найдено.", 404);
        _db.AfishaEvents.Remove(ev);
        await SaveWithAuditAsync(StaffAuditActions.AfishaChanged, $"Афиша «{ev.Title}» удалена", ct);
        return NoContent();
    }

    // ---------------- Каталог подарков ----------------

    [HttpGet("shop/items")]
    public async Task<IEnumerable<ShopItemDto>> ShopItems(CancellationToken ct) =>
        (await _db.ShopItems.AsNoTracking().OrderBy(i => i.SortOrder).ThenBy(i => i.Id).ToListAsync(ct)).Select(ShopItemDto.From);

    [HttpPost("shop/items")]
    public Task<ActionResult<ShopItemDto>> CreateShopItem([FromBody] UpsertShopItemRequest r, CancellationToken ct) => UpsertShopItem(null, r, ct);

    [HttpPut("shop/items/{id:int}")]
    public Task<ActionResult<ShopItemDto>> UpdateShopItem(int id, [FromBody] UpsertShopItemRequest r, CancellationToken ct) => UpsertShopItem(id, r, ct);

    private async Task<ActionResult<ShopItemDto>> UpsertShopItem(int? id, UpsertShopItemRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Title) || r.Title.Trim().Length > 120) return Error("Название подарка: от 1 до 120 символов.");
        if (r.PriceCoins < 1 || r.PriceCoins > 1_000_000) return Error("Цена в баллах — от 1 до 1 000 000.");
        if (r.StockCount < 0 || r.StockCount > 100_000) return Error("Остаток — от 0 до 100 000.");

        var item = id.HasValue ? await _db.ShopItems.FirstOrDefaultAsync(i => i.Id == id, ct) : new ShopItem { CreatedAt = Now };
        if (item == null) return Error("Подарок не найден.", 404);
        item.Title = r.Title.Trim();
        item.Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
        if (r.Icon is { Length: > 32 }) return Error("Ключ иконки — не длиннее 32 символов.");
        item.Icon = string.IsNullOrWhiteSpace(r.Icon) ? "gift" : r.Icon.Trim();
        // Фото — превью из каталога /prizes/ приложения или внешняя https-ссылка
        var image = r.ImageUrl?.Trim();
        if (!string.IsNullOrEmpty(image) && !IsPrizeImagePath(image) && ValidUrl(image) == "invalid")
            return Error("Фото: путь /prizes/<имя>.webp или ссылка https://");
        item.ImageUrl = string.IsNullOrEmpty(image) ? null : IsPrizeImagePath(image) ? image : ValidUrl(image);
        item.PriceCoins = r.PriceCoins;
        item.StockCount = r.StockCount;
        item.AgeRestricted = r.AgeRestricted;
        item.IsActive = r.IsActive;
        item.SortOrder = r.SortOrder;
        if (!id.HasValue) _db.ShopItems.Add(item);
        await SaveWithAuditAsync(StaffAuditActions.CatalogChanged,
            $"Подарок «{item.Title}»: {item.PriceCoins} баллов, остаток {item.StockCount}{(item.IsActive ? "" : " (скрыт)")}", ct);
        return ShopItemDto.From(item);
    }

    // ---------------- Фото подарков ----------------

    /// <summary>
    /// Загрузка фото файлом (Data URL): хранится в базе и отдаётся по /api/media/{id}. В каталоге остаётся только адрес,
    /// поэтому список подарков не раздувается картинками и не забивает кэш телефона.
    /// </summary>
    [HttpPost("media")]
    public async Task<ActionResult<MediaUploadResultDto>> UploadMedia([FromBody] UploadMediaRequest request, CancellationToken ct)
    {
        var parsed = MediaFiles.ParseDataUrl(request.DataUrl);
        if (parsed == null) return Error($"Нужно фото JPEG, PNG или WebP размером до {MediaFiles.MaxBytes / 1024 / 1024} МБ.");

        var file = new MediaFile { ContentType = parsed.Value.ContentType, Bytes = parsed.Value.Bytes, CreatedAt = Now };
        _db.MediaFiles.Add(file);
        await _db.SaveChangesAsync(ct);
        await SaveWithAuditAsync(StaffAuditActions.CatalogChanged, $"Загружено фото №{file.Id}, {file.Bytes.Length / 1024} КБ", ct);
        return new MediaUploadResultDto($"/api/media/{file.Id}");
    }

    // ---------------- Гости ----------------

    [HttpGet("guests")]
    public async Task<IEnumerable<AdminGuestDto>> Guests([FromQuery] string? q, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 100);
        var query = _db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var lowered = term.ToLowerInvariant();
            var digits = new string(term.Where(char.IsDigit).ToArray());
            query = query.Where(u => u.CardNumber == term || u.VkId == term ||
                                     (digits.Length >= 4 && u.Phone != null && u.Phone.Contains(digits)) ||
                                     u.FirstName.ToLower().Contains(lowered) || u.LastName.ToLower().Contains(lowered) ||
                                     (u.FirstName + " " + u.LastName).ToLower().Contains(lowered));
        }

        var levels = await _settings.GetLevelsAsync(ct);
        var users = await query.OrderByDescending(u => u.LastSeenAt).ThenByDescending(u => u.Id).Skip(Math.Max(skip, 0)).Take(take).ToListAsync(ct);
        return users.Select(u => new AdminGuestDto(u.Id, u.VkId, u.CardNumber, u.DisplayName, u.PhotoUrl, u.Phone,
            u.DieselCoinsBalance, u.WeeklyRating, u.TotalRating, u.VisitsCount,
            GuestLevels.Compute(u.TotalRating, levels).Title, u.ConsentAcceptedAt, u.LastSeenAt));
    }

    [HttpGet("guests/{id:int}/transactions")]
    public async Task<IEnumerable<PointsTransactionDto>> GuestTransactions(int id, CancellationToken ct) =>
        await _db.PointsTransactions.AsNoTracking()
            .Where(t => t.UserId == id)
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Take(30)
            .Select(t => new PointsTransactionDto(t.Id, t.Coins, t.Rating, t.Type, t.Description, t.CreatedAt))
            .ToListAsync(ct);

    /// <summary>Ручная корректировка баллов и рейтинга: только с причиной, запись в истории гостя и в журнале аудита.</summary>
    [HttpPost("guests/{id:int}/adjust")]
    public async Task<ActionResult<AdminGuestDto>> AdjustGuest(int id, [FromBody] AdjustGuestRequest r, CancellationToken ct)
    {
        var reason = r.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 3 or > 200) return Error("Укажите причину корректировки (от 3 до 200 символов).");
        if (r.Coins == 0 && r.Rating == 0) return Error("Укажите, сколько баллов или очков изменить.");
        if (Math.Abs(r.Coins) > 100_000 || Math.Abs(r.Rating) > 100_000) return Error("Изменение — не больше 100 000 за раз.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.LockRowAsync("Users", id, ct);

        var guest = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (guest == null) return Error("Гость не найден.", 404);
        if (Admin.IsSelf(guest.VkId)) return Error(StaffIdentity.SelfServiceError, 403);

        if (!await Ledger.AdjustAsync(_db, id, r.Coins, r.Rating, $"Корректировка: {reason}", Now, ct))
        {
            return Error($"Нельзя списать {-r.Coins}: на балансе гостя {guest.DieselCoinsBalance}.", 409);
        }

        StaffAudit.Add(_db, Admin, StaffAuditActions.GuestAdjusted, guest,
            details: $"Баллы {r.Coins:+0;-0;0}, рейтинг {r.Rating:+0;-0;0}. Причина: {reason}", now: Now);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var fresh = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == id, ct);
        var levels = await _settings.GetLevelsAsync(ct);
        return new AdminGuestDto(fresh.Id, fresh.VkId, fresh.CardNumber, fresh.DisplayName, fresh.PhotoUrl, fresh.Phone,
            fresh.DieselCoinsBalance, fresh.WeeklyRating, fresh.TotalRating, fresh.VisitsCount,
            GuestLevels.Compute(fresh.TotalRating, levels).Title, fresh.ConsentAcceptedAt, fresh.LastSeenAt);
    }

    // ---------------- Уровни гостя ----------------

    [HttpGet("levels")]
    public Task<IReadOnlyList<LevelRule>> Levels(CancellationToken ct) => _settings.GetLevelsAsync(ct);

    [HttpPut("levels")]
    public async Task<ActionResult<IReadOnlyList<LevelRule>>> UpdateLevels([FromBody] List<LevelRule> levels, CancellationToken ct)
    {
        var errors = GuestLevels.Validate(levels).Distinct().ToList();
        if (errors.Count > 0) return Error(string.Join(" ", errors));
        var clean = levels.Select(l => l with { Title = l.Title.Trim() }).ToList();
        await _settings.SaveLevelsAsync(clean, ct);
        await SaveWithAuditAsync(StaffAuditActions.SettingsChanged,
            "Уровни гостя: " + string.Join(", ", clean.Select(l => $"{l.Title} от {l.MinRating}")), ct);
        return clean;
    }

    // ---------------- Ставки начисления ----------------

    [HttpGet("rates")]
    public Task<LoyaltyRates> Rates(CancellationToken ct) => _settings.GetRatesAsync(ct);

    [HttpPut("rates")]
    public async Task<ActionResult<LoyaltyRates>> UpdateRates([FromBody] LoyaltyRates rates, CancellationToken ct)
    {
        var errors = rates.Validate().ToList();
        if (errors.Count > 0) return Error(string.Join(" ", errors));
        await _settings.SaveRatesAsync(rates, ct);
        await SaveWithAuditAsync(StaffAuditActions.SettingsChanged, $"Ставки начисления: {JsonSerializer.Serialize(rates)}", ct);
        return rates;
    }

    // ---------------- Сотрудники ----------------

    [HttpGet("staff")]
    public async Task<IEnumerable<StaffMemberDto>> StaffList(CancellationToken ct) =>
        (await _db.StaffMembers.AsNoTracking().OrderByDescending(s => s.IsActive).ThenBy(s => s.Name).ToListAsync(ct)).Select(StaffMemberDto.From);

    [HttpPost("staff")]
    public Task<ActionResult<StaffMemberDto>> CreateStaff([FromBody] UpsertStaffMemberRequest r, CancellationToken ct) => UpsertStaff(null, r, ct);

    [HttpPut("staff/{id:int}")]
    public Task<ActionResult<StaffMemberDto>> UpdateStaff(int id, [FromBody] UpsertStaffMemberRequest r, CancellationToken ct) => UpsertStaff(id, r, ct);

    private async Task<ActionResult<StaffMemberDto>> UpsertStaff(int? id, UpsertStaffMemberRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length > 100) return Error("Имя сотрудника: от 1 до 100 символов.");
        if (r.Role is not (StaffRole.Staff or StaffRole.Admin)) return Error("Роль: Сотрудник или Администратор.");
        var vkId = string.IsNullOrWhiteSpace(r.VkId) ? null : StaffAuthService.NormalizeVkId(r.VkId);
        if (vkId != null && !vkId.All(char.IsDigit)) return Error("VK ID — только цифры (например, 123456789).");
        var pin = string.IsNullOrWhiteSpace(r.Pin) ? null : r.Pin.Trim();
        if (pin != null && !StaffAuthService.IsValidPinFormat(pin)) return Error("ПИН-код — от 4 до 8 цифр.");

        var member = id.HasValue ? await _db.StaffMembers.FirstOrDefaultAsync(s => s.Id == id, ct) : new StaffMember { CreatedAt = Now };
        if (member == null) return Error("Сотрудник не найден.", 404);
        if (vkId == null && pin == null && member.PinHash == null) return Error("Укажите VK ID или ПИН-код сотрудника.");

        if (pin != null)
        {
            var hash = _staffAuth.HashPin(pin);
            if (await _db.StaffMembers.AnyAsync(s => s.PinHash == hash && s.Id != member.Id, ct)) return Error("Такой ПИН-код уже у другого сотрудника.", 409);
            member.PinHash = hash;
        }
        member.Name = r.Name.Trim();
        member.Role = r.Role;
        member.VkId = vkId;
        member.IsActive = r.IsActive;
        if (!id.HasValue) _db.StaffMembers.Add(member);
        await SaveWithAuditAsync(StaffAuditActions.StaffChanged,
            $"Сотрудник «{member.Name}»: {(member.Role == StaffRole.Admin ? "администратор" : "сотрудник")}" +
            $"{(vkId != null ? $", VK {vkId}" : "")}{(pin != null ? ", ПИН обновлён" : "")}{(member.IsActive ? "" : ", отключён")}", ct);
        return StaffMemberDto.From(member);
    }

    /// <summary>Удаление сотрудника. Записи журнала остаются: в них имя сохранено отдельно. Удалить самого себя нельзя.</summary>
    [HttpDelete("staff/{id:int}")]
    public async Task<IActionResult> DeleteStaff(int id, CancellationToken ct)
    {
        var member = await _db.StaffMembers.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (member == null) return Error("Сотрудник не найден.", 404);
        if (Admin.StaffMemberId == id) return Error("Нельзя удалить самого себя.", 409);

        _db.StaffMembers.Remove(member);
        await SaveWithAuditAsync(StaffAuditActions.StaffChanged, $"Сотрудник «{member.Name}» удалён", ct);
        return NoContent();
    }

    // ---------------- Журнал аудита ----------------

    /// <summary>Кто из сотрудников, когда, кому и по какому чеку начислил визит или выдал подарок / привилегию.</summary>
    [HttpGet("audit")]
    public async Task<IEnumerable<StaffAuditDto>> Audit([FromQuery] string? action, [FromQuery] string? q,
        [FromQuery] long? beforeId, [FromQuery] int take = 100, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 500);
        var query = _db.StaffAuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(a => a.Action == action);
        if (beforeId.HasValue) query = query.Where(a => a.Id < beforeId.Value);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(a => a.StaffName.ToLower().Contains(term) ||
                                     (a.GuestName != null && a.GuestName.ToLower().Contains(term)) ||
                                     (a.ReceiptNumber != null && a.ReceiptNumber.ToLower().Contains(term)));
        }
        var list = await query.OrderByDescending(a => a.Id).Take(take).ToListAsync(ct);
        return list.Select(StaffAuditDto.From);
    }
}
