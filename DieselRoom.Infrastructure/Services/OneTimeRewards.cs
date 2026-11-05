using DieselRoom.Domain.Entities;
using DieselRoom.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DieselRoom.Infrastructure.Services;

/// <summary>
/// Разовые бонусы: запись в VkActivityLogs + начисление в одной транзакции. Уникальный индекс (VkId, PostId, тип)
/// гарантирует одну выплату, даже если VK повторит событие или гость нажмёт кнопку дважды.
/// </summary>
public static class OneTimeRewards
{
    public const string Profile = "profile";
    public const string GroupJoin = "group_join";
    public const string MessageAllow = "message_allow";
    public const string Poll = "poll";

    /// <summary>Типы, выполненные раз и навсегда: у них PostId = 0.</summary>
    public static readonly string[] Permanent = [Profile, GroupJoin, MessageAllow];

    /// <summary>Возвращает true, если бонус начислен этим вызовом; false — он уже был выплачен раньше.</summary>
    public static async Task<bool> TryAwardAsync(AppDbContext db, User user, string activityType, int postId, int coins, int rating,
        string txType, string description, DateTime now, CancellationToken ct = default)
    {
        var exists = await db.VkActivityLogs.AnyAsync(l => l.VkId == user.VkId && l.PostId == postId && l.ActivityType == activityType, ct);
        if (exists) return false;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            db.VkActivityLogs.Add(new VkActivityLog
            {
                UserId = user.Id,
                VkId = user.VkId,
                ActivityType = activityType,
                PostId = postId,
                CreatedAt = now,
            });
            await db.SaveChangesAsync(ct);
            await Ledger.AwardAsync(db, user.Id, coins, rating, txType, description, now, ct);
            await tx.CommitAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
