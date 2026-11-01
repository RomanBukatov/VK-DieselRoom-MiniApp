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
/// Изменения баланса и рейтинга — ТОЛЬКО атомарными SQL UPDATE (balance = balance + N), без перезаписи абсолютного
/// значения: параллельные начисления (лайки, визиты, магазин) не теряются. Вызывать внутри транзакции вызывающего.
/// </summary>
public static class Ledger
{
    public static async Task AwardAsync(
        AppDbContext db, int userId, int coins, int rating, string type, string description, DateTime now,
        CancellationToken ct = default)
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

    /// <summary>Возврат баллов (отмена / истечение купона): баланс растёт, «заработано всего» и рейтинг — нет.</summary>
    public static async Task RefundAsync(
        AppDbContext db, int userId, int coins, string type, string description, DateTime now, CancellationToken ct = default)
    {
        if (coins <= 0) return;
        db.PointsTransactions.Add(new PointsTransaction
        {
            UserId = userId,
            Coins = coins,
            Rating = 0,
            Type = type,
            Description = description,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        await db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.DieselCoinsBalance, u => u.DieselCoinsBalance + coins), ct);
    }
}
