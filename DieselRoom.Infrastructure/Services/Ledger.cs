using DieselRoom.Domain.Entities;
using DieselRoom.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DieselRoom.Infrastructure.Services;

/// <summary>Результат операции сервиса: успех или понятная гостю/сотруднику ошибка.</summary>
public sealed record OpResult<T>(bool Ok, T? Value, string? Error, int StatusCode = 200)
{
    public static OpResult<T> Success(T value) => new(true, value, null);
    public static OpResult<T> Fail(string error, int statusCode = 400) => new(false, default, error, statusCode);
}

/// <summary>
/// Единственная точка изменения баланса и рейтинга. Каждая операция — атомарный SQL UPDATE (balance = balance + N),
/// поэтому параллельные начисления не теряются, и строка PointsTransactions в той же транзакции БД: изменить баланс
/// без записи (или наоборот) невозможно. Сумма записей гостя всегда равна его балансу и рейтингу «за всё время».
/// Вызывать только внутри транзакции вызывающего: без неё операция откажет, а не запишется наполовину.
/// </summary>
public static class Ledger
{
    private static void RequireTransaction(AppDbContext db)
    {
        if (db.Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException("Операция с баллами выполняется только внутри транзакции БД.");
        }
    }

    private static async Task WriteEntryAsync(AppDbContext db, int userId, int coins, int rating, string type, string description, DateTime now, CancellationToken ct)
    {
        db.PointsTransactions.Add(new PointsTransaction
        {
            UserId = userId,
            Coins = coins,
            Rating = rating,
            Type = type,
            Description = description,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Начисление за действие гостя: баллы и рейтинг растут, «заработано всего» тоже.</summary>
    public static async Task AwardAsync(
        AppDbContext db, int userId, int coins, int rating, string type, string description, DateTime now,
        CancellationToken ct = default)
    {
        RequireTransaction(db);
        ArgumentOutOfRangeException.ThrowIfNegative(coins);
        ArgumentOutOfRangeException.ThrowIfNegative(rating);

        await WriteEntryAsync(db, userId, coins, rating, type, description, now, ct);
        await db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s =>
            {
                s.SetProperty(u => u.DieselCoinsBalance, u => u.DieselCoinsBalance + coins);
                if (coins > 0)
                {
                    s.SetProperty(u => u.TotalCoinsEarned, u => u.TotalCoinsEarned + coins);
                }
                if (rating > 0)
                {
                    s.SetProperty(u => u.WeeklyRating, u => u.WeeklyRating + rating);
                    s.SetProperty(u => u.TotalRating, u => u.TotalRating + rating);
                    s.SetProperty(u => u.WeeklyRatingUpdatedAt, now);
                }
            }, ct);
    }

    /// <summary>Списание за подарок: рейтинг не меняется. false — баллов не хватает, ничего не записано.</summary>
    public static async Task<bool> DebitAsync(
        AppDbContext db, int userId, int coins, string type, string description, DateTime now, CancellationToken ct = default)
    {
        RequireTransaction(db);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(coins);

        var changed = await db.Users
            .Where(u => u.Id == userId && u.DieselCoinsBalance >= coins)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.DieselCoinsBalance, u => u.DieselCoinsBalance - coins), ct);
        if (changed != 1) return false;

        await WriteEntryAsync(db, userId, -coins, 0, type, description, now, ct);
        return true;
    }

    /// <summary>
    /// Ручная корректировка администратора: баланс не уходит в минус, рейтинг не опускается ниже нуля. Возвращает
    /// фактическое изменение рейтинга — оно же попадает в запись, чтобы журнал совпадал с тем, что произошло на самом
    /// деле; null — списать столько нельзя, ничего не записано. «Заработано всего» не меняется.
    /// </summary>
    public static async Task<int?> AdjustAsync(
        AppDbContext db, int userId, int coins, int rating, string description, DateTime now, CancellationToken ct = default)
    {
        RequireTransaction(db);

        var total = await db.Users.Where(u => u.Id == userId).Select(u => (int?)u.TotalRating).FirstOrDefaultAsync(ct);
        if (total == null) return null;
        var applied = Math.Max(rating, -total.Value);

        var changed = await db.Users
            .Where(u => u.Id == userId && u.DieselCoinsBalance + coins >= 0)
            .ExecuteUpdateAsync(s =>
            {
                s.SetProperty(u => u.DieselCoinsBalance, u => u.DieselCoinsBalance + coins);
                s.SetProperty(u => u.WeeklyRating, u => u.WeeklyRating + rating < 0 ? 0 : u.WeeklyRating + rating);
                s.SetProperty(u => u.TotalRating, u => u.TotalRating + applied);
                if (rating > 0) s.SetProperty(u => u.WeeklyRatingUpdatedAt, now);
            }, ct);
        if (changed != 1) return null;

        await WriteEntryAsync(db, userId, coins, applied, PointsTransactionTypes.ManualAdjust, description, now, ct);
        return applied;
    }

    /// <summary>Возврат баллов (отмена / истечение купона): баланс растёт, «заработано всего» и рейтинг — нет.</summary>
    public static async Task RefundAsync(
        AppDbContext db, int userId, int coins, string type, string description, DateTime now, CancellationToken ct = default)
    {
        if (coins <= 0) return;
        RequireTransaction(db);

        await WriteEntryAsync(db, userId, coins, 0, type, description, now, ct);
        await db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.DieselCoinsBalance, u => u.DieselCoinsBalance + coins), ct);
    }
}
