using DieselRoom.Api.DTOs;
using DieselRoom.Domain.Entities;
using DieselRoom.Domain.Enums;
using DieselRoom.Domain.Services;
using DieselRoom.Infrastructure.Data;
using DieselRoom.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace DieselRoom.Api.Services;

/// <summary>Собирает профиль гостя и карточку гостя для персонала.</summary>
public class ProfileBuilder
{
    private readonly AppDbContext _db;
    private readonly RatingService _rating;
    private readonly PrivilegeService _privileges;
    private readonly SettingsService _settings;

    public ProfileBuilder(AppDbContext db, RatingService rating, PrivilegeService privileges, SettingsService settings)
    {
        _db = db;
        _rating = rating;
        _privileges = privileges;
        _settings = settings;
    }

    public async Task<ProfileDto> BuildAsync(User user, StaffIdentity staff, DateTime now, CancellationToken ct = default)
    {
        var privilege = await _privileges.GetActiveAsync(user.Id, now, ct);
        var totalRank = await _rating.GetTotalRankAsync(user.Id, ct);
        var invited = await _db.Users.CountAsync(u => u.ReferredByUserId == user.Id, ct);
        var rewarded = await _db.Users.CountAsync(u => u.ReferredByUserId == user.Id && u.ReferralRewardPaid, ct);
        var giftsReceived = await _db.ShopOrders.CountAsync(o => o.UserId == user.Id && o.Status == ShopOrderStatus.Issued, ct);
        var bestArchived = await _db.WeeklyRaceResults.Where(r => r.UserId == user.Id).MinAsync(r => (int?)r.Rank, ct);
        var bestRank = new[] { bestArchived, privilege.Rank }.Where(r => r.HasValue).Select(r => r!.Value).DefaultIfEmpty(0).Min();
        var rates = await _settings.GetRatesAsync(ct);

        return new ProfileDto(
            user.Id, user.VkId, user.CardNumber, user.FirstName, user.LastName, user.PhotoUrl, user.Phone,
            user.DieselCoinsBalance, user.TotalCoinsEarned, user.WeeklyRating, user.TotalRating,
            privilege.Rank, totalRank, user.VisitsCount,
            PrivilegeDto.From(privilege),
            Achievements.Compute(user.VisitsCount, rewarded, giftsReceived, bestRank),
            new ReferralInfoDto(user.CardNumber, invited, rewarded, rates.ReferralCoins, rates.ReferralRating),
            staff.Role, staff.IsStaff ? staff.Name : null,
            PermTime.NextWeeklyReset(now));
    }

    public async Task<GuestCardDto> BuildGuestCardAsync(User user, DateTime now, CancellationToken ct = default)
    {
        var privilege = await _privileges.GetActiveAsync(user.Id, now, ct);
        return new GuestCardDto(user.Id, user.DisplayName, user.CardNumber, user.Phone, user.PhotoUrl,
            user.DieselCoinsBalance, user.WeeklyRating, user.VisitsCount, PrivilegeDto.From(privilege));
    }
}
