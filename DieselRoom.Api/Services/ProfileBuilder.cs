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
        var levels = await _settings.GetLevelsAsync(ct);
        var achievementRules = await _settings.GetAchievementsAsync(ct);
        var race = await _rating.GetRaceProgressAsync(user, privilege.Rank, ct);
        var tasks = await BuildTasksAsync(user, rates, ct);

        return new ProfileDto(
            user.Id, user.VkId, user.CardNumber, user.FirstName, user.LastName, user.PhotoUrl, user.Phone,
            user.DieselCoinsBalance, user.TotalCoinsEarned, user.WeeklyRating, user.TotalRating,
            privilege.Rank, totalRank, user.VisitsCount,
            PrivilegeDto.From(privilege),
            Achievements.Compute(user.VisitsCount, rewarded, giftsReceived, bestRank, achievementRules),
            new ReferralInfoDto(user.CardNumber, invited, rewarded, rates.ReferralCoins, rates.ReferralRating,
                user.ReferredByUserId != null, UserService.CanEnterReferralCode(user, now)),
            staff.Role, staff.IsStaff ? staff.Name : null,
            PermTime.NextWeeklyReset(now),
            !LegalInfo.HasCurrentConsent(user), user.ConsentAcceptedAt, user.ProfileCompletedAt != null,
            GuestLevels.Compute(user.TotalRating, levels), race, tasks);
    }

    /// <summary>«Как заработать баллы»: сначала разовые задания со статусом, затем то, за что начисляется постоянно.</summary>
    private async Task<List<GuestTaskDto>> BuildTasksAsync(User user, LoyaltyRates r, CancellationToken ct)
    {
        var done = await _db.VkActivityLogs.AsNoTracking()
            .Where(l => l.UserId == user.Id && OneTimeRewards.Permanent.Contains(l.ActivityType))
            .Select(l => l.ActivityType)
            .ToListAsync(ct);

        return
        [
            new("profile", "Заполнить профиль", "Имя и телефон, один раз", r.ProfileBonusCoins, r.ProfileBonusRating, true, done.Contains(OneTimeRewards.Profile), "profile"),
            new("group_join", "Подписаться на группу", "Один раз", r.GroupJoinCoins, r.GroupJoinRating, true, done.Contains(OneTimeRewards.GroupJoin), "join"),
            new("message_allow", "Разрешить сообщения от группы", "Один раз: брони и рейтинг придут в личные", r.MessageAllowCoins, r.MessageAllowRating, true, done.Contains(OneTimeRewards.MessageAllow), "messages"),
            new("like", "Лайк записи группы", "Один раз на запись", r.LikeCoins, r.LikeRating, false, false, null),
            new("comment", "Комментарий к записи", $"От 2 слов и 10 букв, до {r.MaxPaidCommentsPer24h} в сутки", r.CommentCoins, r.CommentRating, false, false, null),
            new("repost", "Репост записи группы", "Один раз на запись", r.RepostCoins, r.RepostRating, false, false, null),
            new("poll", "Участие в опросе группы", "Один раз на опрос", r.PollCoins, r.PollRating, false, false, null),
            new("visit", "Визит по чеку", "Покажите карту гостя при закрытии чека", r.VisitCoins, r.VisitRating, false, false, null),
            new("friend", "Приведите друга", "После его первого визита по чеку", r.ReferralCoins, r.ReferralRating, false, false, null),
        ];
    }

    public async Task<GuestCardDto> BuildGuestCardAsync(User user, DateTime now, CancellationToken ct = default)
    {
        var privilege = await _privileges.GetActiveAsync(user.Id, now, ct);
        return new GuestCardDto(user.Id, user.DisplayName, user.CardNumber, user.Phone, user.PhotoUrl,
            user.DieselCoinsBalance, user.WeeklyRating, user.VisitsCount, PrivilegeDto.From(privilege));
    }
}
