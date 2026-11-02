using DieselRoom.Domain.Entities;
using DieselRoom.Domain.Enums;
using DieselRoom.Domain.Services;
using DieselRoom.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DieselRoom.Infrastructure.Services;

/// <summary>
/// Магазин подарков: атомарное списание баллов и резерв остатка, купон #DR-XXXX на 7 дней,
/// отмена гостем и автоистечение с возвратом баллов на баланс и товара на склад.
/// </summary>
public class ShopService
{
    public static readonly TimeSpan CouponLifetime = TimeSpan.FromDays(7);
    public static readonly TimeSpan PrizeCouponLifetime = TimeSpan.FromDays(14);
    private const int MaxCodeAttempts = 20;

    private readonly AppDbContext _db;
    private readonly ILogger<ShopService> _logger;
    private readonly Random _random;

    public ShopService(AppDbContext db, ILogger<ShopService> logger, Random? random = null)
    {
        _db = db;
        _logger = logger;
        _random = random ?? Random.Shared;
    }

    public async Task<OpResult<ShopOrder>> PurchaseAsync(int userId, int itemId, DateTime now, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // SELECT ... FOR UPDATE: параллельные покупки одного гостя / последнего экземпляра товара идут по очереди
        await _db.LockRowAsync("Users", userId, ct);
        await _db.LockRowAsync("ShopItems", itemId, ct);

        var item = await _db.ShopItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item == null || !item.IsActive) return OpResult<ShopOrder>.Fail("Подарок не найден или снят с витрины.", 404);
        if (item.StockCount <= 0) return OpResult<ShopOrder>.Fail("Этот подарок закончился — загляните позже!", 409);

        var balance = await _db.Users.Where(u => u.Id == userId).Select(u => (int?)u.DieselCoinsBalance).FirstOrDefaultAsync(ct);
        if (balance == null) return OpResult<ShopOrder>.Fail("Гость не найден.", 404);
        if (balance < item.PriceCoins)
        {
            return OpResult<ShopOrder>.Fail($"Недостаточно баллов: нужно {item.PriceCoins}, у вас {balance}.", 409);
        }

        // Условные UPDATE — вторая линия защиты: баланс и остаток не уйдут в минус даже без блокировки строк
        var debited = await _db.Users
            .Where(u => u.Id == userId && u.DieselCoinsBalance >= item.PriceCoins)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.DieselCoinsBalance, u => u.DieselCoinsBalance - item.PriceCoins), ct);
        if (debited != 1) return OpResult<ShopOrder>.Fail("Недостаточно баллов.", 409);

        var reserved = await _db.ShopItems
            .Where(i => i.Id == itemId && i.IsActive && i.StockCount > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.StockCount, i => i.StockCount - 1), ct);
        if (reserved != 1)
        {
            await tx.RollbackAsync(ct);
            return OpResult<ShopOrder>.Fail("Этот подарок закончился — загляните позже!", 409);
        }

        var order = new ShopOrder
        {
            UserId = userId,
            ShopItemId = item.Id,
            Code = await GenerateUniqueCodeAsync(ct),
            Title = item.Title,
            Icon = item.Icon,
            ImageUrl = item.ImageUrl,
            PriceCoins = item.PriceCoins,
            Status = ShopOrderStatus.Pending,
            CreatedAt = now,
            ExpiresAt = now + CouponLifetime,
        };
        _db.ShopOrders.Add(order);
        _db.PointsTransactions.Add(new PointsTransaction
        {
            UserId = userId,
            Coins = -item.PriceCoins,
            Rating = 0,
            Type = PointsTransactionTypes.ShopPurchase,
            Description = $"Подарок «{item.Title}» (#{order.Code})",
            CreatedAt = now,
        });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("[SHOP_AUDIT] Action=Purchase UserId={UserId} ItemId={ItemId} Price={Price} Code={Code}",
            userId, item.Id, item.PriceCoins, order.Code);
        return OpResult<ShopOrder>.Success(order);
    }

    /// <summary>Гость отменяет купон «К получению»: баллы мгновенно возвращаются, остаток товара +1.</summary>
    public async Task<OpResult<ShopOrder>> CancelByGuestAsync(int userId, int orderId, DateTime now, CancellationToken ct = default)
    {
        var order = await _db.ShopOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId, ct);
        if (order == null) return OpResult<ShopOrder>.Fail("Заказ не найден.", 404);
        if (order.Status != ShopOrderStatus.Pending) return OpResult<ShopOrder>.Fail("Отменить можно только подарок «К получению».", 409);

        var closed = await CloseWithRefundAsync(order, ShopOrderStatus.Cancelled, now, ct);
        return closed == null
            ? OpResult<ShopOrder>.Fail("Заказ уже выдан или закрыт.", 409)
            : OpResult<ShopOrder>.Success(closed);
    }

    /// <summary>Фоновая проверка: просроченные купоны → Expired, баллы гостю, товар на склад. Возвращает число закрытых.</summary>
    public async Task<int> ExpireOverdueAsync(DateTime now, CancellationToken ct = default)
    {
        var overdue = await _db.ShopOrders.AsNoTracking()
            .Where(o => o.Status == ShopOrderStatus.Pending && o.ExpiresAt < now)
            .OrderBy(o => o.ExpiresAt)
            .Take(200)
            .ToListAsync(ct);

        var count = 0;
        foreach (var order in overdue)
        {
            if (await CloseWithRefundAsync(order, ShopOrderStatus.Expired, now, ct) != null)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Перевод Pending → Cancelled/Expired условным UPDATE (WHERE Status = Pending): отмена гостем, фоновая
    /// просрочка и выдача официантом не могут сработать дважды — возврат делает только тот, кто закрыл заказ.
    /// </summary>
    private async Task<ShopOrder?> CloseWithRefundAsync(ShopOrder order, ShopOrderStatus status, DateTime now, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var changed = await _db.ShopOrders
            .Where(o => o.Id == order.Id && o.Status == ShopOrderStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, status)
                .SetProperty(o => o.ClosedAt, now), ct);
        if (changed != 1) return null;

        var (type, description) = status == ShopOrderStatus.Expired
            ? (PointsTransactionTypes.ShopExpiredRefund, $"Возврат: истёк срок купона «{order.Title}» (#{order.Code})")
            : (PointsTransactionTypes.ShopRefund, $"Возврат: отмена подарка «{order.Title}» (#{order.Code})");
        await Ledger.RefundAsync(_db, order.UserId, order.PriceCoins, type, description, now, ct);

        if (order.ShopItemId.HasValue)
        {
            await _db.ShopItems
                .Where(i => i.Id == order.ShopItemId.Value)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.StockCount, i => i.StockCount + 1), ct);
        }

        await tx.CommitAsync(ct);
        _logger.LogInformation("[SHOP_AUDIT] Action={Action} OrderId={OrderId} UserId={UserId} Refund={Refund}",
            status, order.Id, order.UserId, order.PriceCoins);

        order.Status = status;
        order.ClosedAt = now;
        return order;
    }

    /// <summary>Официант гасит купон. Просроченный купон не выдаётся — он сразу закрывается с возвратом.</summary>
    public async Task<OpResult<ShopOrder>> IssueAsync(StaffIdentity staff, string code, DateTime now, CancellationToken ct = default)
    {
        var normalized = Codes.NormalizeOrderCode(code);
        if (normalized == null) return OpResult<ShopOrder>.Fail("Неверный формат кода. Пример: #DR-7K2M.");

        var order = await _db.ShopOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Code == normalized, ct);
        if (order == null) return OpResult<ShopOrder>.Fail($"Купон #{normalized} не найден.", 404);

        switch (order.Status)
        {
            case ShopOrderStatus.Issued:
                return OpResult<ShopOrder>.Fail($"Купон #{normalized} уже выдан {FormatLocal(order.IssuedAt)} ({order.IssuedByStaffName}).", 409);
            case ShopOrderStatus.Cancelled:
                return OpResult<ShopOrder>.Fail($"Купон #{normalized} отменён гостем.", 409);
            case ShopOrderStatus.Expired:
                return OpResult<ShopOrder>.Fail($"Срок купона #{normalized} истёк — баллы возвращены гостю.", 409);
        }

        if (order.ExpiresAt < now)
        {
            await CloseWithRefundAsync(order, ShopOrderStatus.Expired, now, ct);
            return OpResult<ShopOrder>.Fail($"Срок купона #{normalized} истёк — баллы возвращены гостю.", 409);
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var changed = await _db.ShopOrders
            .Where(o => o.Id == order.Id && o.Status == ShopOrderStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, ShopOrderStatus.Issued)
                .SetProperty(o => o.IssuedAt, now)
                .SetProperty(o => o.IssuedByStaffName, staff.Name)
                .SetProperty(o => o.ClosedAt, now), ct);
        if (changed != 1) return OpResult<ShopOrder>.Fail("Купон уже закрыт.", 409);

        var guest = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == order.UserId, ct);
        StaffAudit.Add(_db, staff, StaffAuditActions.GiftIssued, guest, details: $"{order.Title} · #{order.Code}", now: now);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        order.Status = ShopOrderStatus.Issued;
        order.IssuedAt = now;
        order.IssuedByStaffName = staff.Name;
        order.ClosedAt = now;
        return OpResult<ShopOrder>.Success(order);
    }

    /// <summary>Бесплатный купон-приз (спецприз за 7 дней на 1-м месте) — гасится официантом как обычный подарок.</summary>
    public async Task<ShopOrder> CreatePrizeOrderAsync(int userId, string title, string icon, DateTime now, CancellationToken ct = default)
    {
        var order = new ShopOrder
        {
            UserId = userId,
            ShopItemId = null,
            Code = await GenerateUniqueCodeAsync(ct),
            Title = title,
            Icon = icon,
            PriceCoins = 0,
            Status = ShopOrderStatus.Pending,
            CreatedAt = now,
            ExpiresAt = now + PrizeCouponLifetime,
        };
        _db.ShopOrders.Add(order);
        await _db.SaveChangesAsync(ct);
        return order;
    }

    private async Task<string> GenerateUniqueCodeAsync(CancellationToken ct)
    {
        // 31^4 ≈ 923 тыс. кодов; повтор с действующим купоном почти невозможен, но проверяем (уникальный индекс — последняя линия)
        for (var attempt = 0; attempt < MaxCodeAttempts; attempt++)
        {
            var code = Codes.GenerateOrderCode(_random);
            if (!await _db.ShopOrders.AnyAsync(o => o.Code == code, ct)) return code;
        }
        throw new InvalidOperationException("Не удалось подобрать свободный код купона.");
    }

    private static string FormatLocal(DateTime? utc) =>
        utc.HasValue ? PermTime.ToLocal(utc.Value).ToString("dd.MM HH:mm") : "ранее";
}
