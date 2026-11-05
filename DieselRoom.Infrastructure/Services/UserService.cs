using DieselRoom.Domain.Entities;
using DieselRoom.Domain.Services;
using DieselRoom.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DieselRoom.Infrastructure.Services;

public sealed record GuestSyncRequest(string? FirstName, string? LastName, string? PhotoUrl, string? ReferralCode);

/// <summary>Регистрация гостя при первом открытии приложения и привязка к пригласившему (#ref=CardNumber).</summary>
public class UserService
{
    public const int CardNumberBase = 100_000;
    /// <summary>Промокод друга можно привязать в первые сутки после регистрации и только до первого визита.</summary>
    public static readonly TimeSpan ReferralAttachWindow = TimeSpan.FromHours(24);

    private readonly AppDbContext _db;

    public UserService(AppDbContext db)
    {
        _db = db;
    }

    public static string CardNumberFor(int userId) => (CardNumberBase + userId).ToString();

    public Task<User?> FindByVkIdAsync(string vkId, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.VkId == vkId, ct);

    /// <summary>Создаёт гостя (если его ещё нет), обновляет имя/аватар и привязывает реферальный код.</summary>
    public async Task<User> SyncAsync(string vkId, GuestSyncRequest request, DateTime now, CancellationToken ct = default)
    {
        var user = await FindByVkIdAsync(vkId, ct);
        if (user == null)
        {
            user = new User
            {
                VkId = vkId,
                // Временный уникальный номер: настоящий зависит от Id и проставляется сразу после вставки
                CardNumber = "T" + Guid.NewGuid().ToString("N")[..12],
                // Имя и фото сохраняем только после согласия на обработку персональных данных (152-ФЗ)
                FirstName = "Гость",
                LastName = string.Empty,
                CreatedAt = now,
                LastSeenAt = now,
            };
            _db.Users.Add(user);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
            {
                // Параллельный первый запрос уже создал гостя
                _db.ChangeTracker.Clear();
                user = await FindByVkIdAsync(vkId, ct) ?? throw new InvalidOperationException("Гость не найден после конфликта создания.");
                return await UpdateProfileAsync(user, request, now, ct);
            }

            user.CardNumber = CardNumberFor(user.Id);
            await _db.SaveChangesAsync(ct);
        }

        return await UpdateProfileAsync(user, request, now, ct);
    }

    private async Task<User> UpdateProfileAsync(User user, GuestSyncRequest request, DateTime now, CancellationToken ct)
    {
        if (user.ConsentAcceptedAt != null)
        {
            // Имя, которое гость указал в профиле сам, данные ВК не перезаписывают
            if (user.ProfileCompletedAt == null)
            {
                var first = Clean(request.FirstName, 100);
                var last = Clean(request.LastName, 100);
                if (first != null) user.FirstName = first;
                if (last != null) user.LastName = last;
            }
            var photo = CleanUrl(request.PhotoUrl);
            if (photo != null) user.PhotoUrl = photo;
        }
        user.LastSeenAt = now;

        await TryAttachReferralAsync(user, request.ReferralCode, now, ct);
        await _db.SaveChangesAsync(ct);
        return user;
    }

    /// <summary>Фиксирует согласие: условия программы, персональные данные (152-ФЗ) и возраст 18+ принимаются вместе.</summary>
    public async Task<OpResult<User>> AcceptConsentAsync(User user, bool terms, bool personalData, bool adult, DateTime now, CancellationToken ct = default)
    {
        if (!terms || !personalData) return OpResult<User>.Fail("Для участия нужно принять условия программы и согласие на обработку персональных данных.");
        if (!adult) return OpResult<User>.Fail("Программа лояльности DIESEL ROOM доступна только гостям старше 18 лет.");

        user.ConsentAcceptedAt = now;
        user.ConsentVersion = LegalInfo.CurrentVersion;
        user.AdultConfirmedAt = now;
        await _db.SaveChangesAsync(ct);
        return OpResult<User>.Success(user);
    }

    /// <summary>Форма профиля: имя и телефон, за первое заполнение — разовый бонус.</summary>
    public async Task<OpResult<User>> SaveProfileAsync(User user, string? firstName, string? lastName, string? phone, LoyaltyRates rates, DateTime now, CancellationToken ct = default)
    {
        var first = Clean(firstName, 100);
        if (first == null || first.Length < 2) return OpResult<User>.Fail("Укажите имя (от 2 символов).");
        var normalizedPhone = Codes.NormalizePhone(phone);
        if (normalizedPhone == null) return OpResult<User>.Fail("Укажите телефон в формате +7 XXX XXX-XX-XX.");
        // Один номер — один гость: иначе бонус за профиль можно получать на разные учётные записи
        if (await _db.Users.AnyAsync(u => u.Phone == normalizedPhone && u.Id != user.Id, ct))
        {
            return OpResult<User>.Fail("Этот номер уже указан у другого гостя.", 409);
        }

        user.FirstName = first;
        user.LastName = Clean(lastName, 100) ?? string.Empty;
        user.Phone = normalizedPhone;
        user.ProfileCompletedAt ??= now;
        await _db.SaveChangesAsync(ct);

        await OneTimeRewards.TryAwardAsync(_db, user, OneTimeRewards.Profile, 0, rates.ProfileBonusCoins, rates.ProfileBonusRating,
            PointsTransactionTypes.ProfileBonus, "Заполнен профиль гостя", now, ct);
        return OpResult<User>.Success(user);
    }

    /// <summary>Промокод друга, введённый вручную: те же условия, что и у ссылки, но с понятной причиной отказа.</summary>
    public async Task<OpResult<User>> ApplyReferralAsync(User user, string? code, DateTime now, CancellationToken ct = default)
    {
        if (Clean(code, 16) == null) return OpResult<User>.Fail("Введите код друга.");
        if (user.ReferredByUserId != null) return OpResult<User>.Fail("Код друга уже привязан.", 409);
        if (user.VisitsCount > 0) return OpResult<User>.Fail("Код друга можно ввести только до первого визита.", 409);
        if (now - user.CreatedAt > ReferralAttachWindow) return OpResult<User>.Fail("Код друга можно ввести только в первые сутки после регистрации.", 409);

        if (!await TryAttachReferralAsync(user, code, now, ct)) return OpResult<User>.Fail("Код не подошёл: проверьте его. Свой код вводить нельзя.");
        await _db.SaveChangesAsync(ct);
        return OpResult<User>.Success(user);
    }

    public static bool CanEnterReferralCode(User user, DateTime now) =>
        user.ReferredByUserId == null && user.VisitsCount == 0 && now - user.CreatedAt <= ReferralAttachWindow;

    /// <summary>
    /// Привязка к пригласившему: только новый гость (первые сутки, ещё без визитов), не сам себя и без «кольца»
    /// (пригласивший не может быть приглашён этим же гостем). Бонус выплачивается позже — после первого визита по чеку.
    /// </summary>
    public async Task<bool> TryAttachReferralAsync(User user, string? referralCode, DateTime now, CancellationToken ct = default)
    {
        var code = Clean(referralCode, 16);
        if (code == null || user.ReferredByUserId != null || user.VisitsCount > 0) return false;
        if (now - user.CreatedAt > ReferralAttachWindow) return false;
        if (string.Equals(code, user.CardNumber, StringComparison.OrdinalIgnoreCase)) return false;

        var inviter = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.CardNumber == code, ct);
        if (inviter == null || inviter.Id == user.Id || inviter.ReferredByUserId == user.Id) return false;

        user.ReferredByUserId = inviter.Id;
        return true;
    }

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }

    private static string? CleanUrl(string? value)
    {
        var url = Clean(value, 500);
        return url != null && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? url : null;
    }
}
