using DieselRoom.Domain.Entities;
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
                FirstName = Clean(request.FirstName, 100) ?? "Гость",
                LastName = Clean(request.LastName, 100) ?? string.Empty,
                PhotoUrl = CleanUrl(request.PhotoUrl),
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
        var first = Clean(request.FirstName, 100);
        var last = Clean(request.LastName, 100);
        if (first != null) user.FirstName = first;
        if (last != null) user.LastName = last;
        var photo = CleanUrl(request.PhotoUrl);
        if (photo != null) user.PhotoUrl = photo;
        user.LastSeenAt = now;

        await TryAttachReferralAsync(user, request.ReferralCode, now, ct);
        await _db.SaveChangesAsync(ct);
        return user;
    }

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
