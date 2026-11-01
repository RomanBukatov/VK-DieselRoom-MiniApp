using DieselRoom.Domain.Entities;
using DieselRoom.Domain.Enums;
using DieselRoom.Domain.Services;
using DieselRoom.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DieselRoom.Infrastructure.Services;

public sealed record VisitResult(Visit Visit, User Guest, bool ReferralRewardPaid, User? Inviter);

/// <summary>
/// Начисление за визит по закрытому чеку: 1 чек = 1 раз (уникальный индекс Visits.ReceiptNumber), баллы и рейтинг
/// гостю, счётчик визитов для достижений и бонус пригласившему — только после ПЕРВОГО визита друга.
/// </summary>
public class VisitService
{
    private readonly AppDbContext _db;
    private readonly SettingsService _settings;
    private readonly IGuestNotifier _notifier;
    private readonly ILogger<VisitService> _logger;

    public VisitService(AppDbContext db, SettingsService settings, IGuestNotifier notifier, ILogger<VisitService> logger)
    {
        _db = db;
        _settings = settings;
        _notifier = notifier;
        _logger = logger;
    }

    public static string VisitTypeLabel(VisitType type) => type switch
    {
        VisitType.Regular => "Обычный визит",
        VisitType.BusinessLunch => "Бизнес-ланч",
        VisitType.PartyKaraoke => "Вечеринка / Караоке",
        _ => "Визит",
    };

    public async Task<OpResult<VisitResult>> AccrueAsync(StaffIdentity staff, int guestUserId, string? receiptNumber,
        VisitType type, DateTime now, CancellationToken ct = default)
    {
        var receipt = Codes.NormalizeReceiptNumber(receiptNumber);
        if (receipt == null) return OpResult<VisitResult>.Fail("Укажите номер закрытого чека (буквы, цифры, «-» или «/», до 32 символов).");
        if (!Enum.IsDefined(type)) return OpResult<VisitResult>.Fail("Выберите тип визита.");

        var existing = await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.ReceiptNumber == receipt, ct);
        if (existing != null) return DuplicateReceipt(receipt, existing);

        var rates = await _settings.GetRatesAsync(ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.LockRowAsync("Users", guestUserId, ct);

        var guest = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == guestUserId, ct);
        if (guest == null) return OpResult<VisitResult>.Fail("Гость не найден.", 404);

        var visit = new Visit
        {
            UserId = guest.Id,
            ReceiptNumber = receipt,
            Type = type,
            CoinsAwarded = rates.VisitCoins,
            RatingAwarded = rates.VisitRating,
            StaffMemberId = staff.StaffMemberId,
            StaffName = staff.Name,
            CreatedAt = now,
        };
        _db.Visits.Add(visit);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // Тот же чек параллельно провёл другой сотрудник: уникальный индекс не дал провести его дважды
            _db.ChangeTracker.Clear();
            await tx.RollbackAsync(ct);
            var winner = await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.ReceiptNumber == receipt, ct);
            return winner != null ? DuplicateReceipt(receipt, winner) : OpResult<VisitResult>.Fail($"Чек №{receipt} уже проведён.", 409);
        }

        await Ledger.AwardAsync(_db, guest.Id, rates.VisitCoins, rates.VisitRating, PointsTransactionTypes.Visit,
            $"Визит по чеку №{receipt} · {VisitTypeLabel(type)}", now, ct);
        await _db.Users.Where(u => u.Id == guest.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.VisitsCount, u => u.VisitsCount + 1), ct);

        // Бонус пригласившему — один раз: условный UPDATE (ReferralRewardPaid = false → true)
        User? inviter = null;
        var referralPaid = false;
        if (guest.VisitsCount == 0 && guest.ReferredByUserId.HasValue && !guest.ReferralRewardPaid)
        {
            var marked = await _db.Users
                .Where(u => u.Id == guest.Id && !u.ReferralRewardPaid)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.ReferralRewardPaid, true), ct);
            inviter = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == guest.ReferredByUserId.Value, ct);
            if (marked == 1 && inviter != null)
            {
                await Ledger.AwardAsync(_db, inviter.Id, rates.ReferralCoins, rates.ReferralRating, PointsTransactionTypes.Referral,
                    $"Друг {guest.FirstName} впервые посетил DIESEL ROOM", now, ct);
                referralPaid = true;
            }
        }

        StaffAudit.Add(_db, staff, StaffAuditActions.VisitAccrued, guest, receipt,
            $"{VisitTypeLabel(type)}: +{rates.VisitCoins} баллов, +{rates.VisitRating} в рейтинг" +
            (referralPaid ? $"; реферальный бонус пригласившему {inviter!.DisplayName} (+{rates.ReferralCoins})" : string.Empty),
            now);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var fresh = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == guest.Id, ct);
        _logger.LogInformation("[VISIT_AUDIT] Receipt={Receipt} UserId={UserId} Staff={Staff} Referral={Referral}",
            receipt, guest.Id, staff.Name, referralPaid);

        _notifier.Send(fresh.VkId,
            $"🧾 Спасибо за визит в DIESEL ROOM! +{rates.VisitCoins} баллов и +{rates.VisitRating} в рейтинг. " +
            $"Баланс: {fresh.DieselCoinsBalance} баллов. 🐜", $"визит {receipt}");
        if (referralPaid)
        {
            _notifier.Send(inviter!.VkId,
                $"🤝 Ваш друг {guest.FirstName} впервые посетил DIESEL ROOM! Вам начислено +{rates.ReferralCoins} баллов " +
                $"и +{rates.ReferralRating} в рейтинг.", $"реферальный бонус за {guest.Id}");
        }

        return OpResult<VisitResult>.Success(new VisitResult(visit, fresh, referralPaid, inviter));
    }

    private static OpResult<VisitResult> DuplicateReceipt(string receipt, Visit existing) =>
        OpResult<VisitResult>.Fail(
            $"Чек №{receipt} уже проведён {PermTime.ToLocal(existing.CreatedAt):dd.MM.yyyy HH:mm} (сотрудник: {existing.StaffName}). Один чек — одно начисление.",
            409);
}
