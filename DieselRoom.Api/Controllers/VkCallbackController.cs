using System.Collections.Concurrent;
using System.Text.Json;
using DieselRoom.Api.DTOs;
using DieselRoom.Api.Models;
using DieselRoom.Domain.Entities;
using DieselRoom.Domain.Services;
using DieselRoom.Infrastructure.Data;
using DieselRoom.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DieselRoom.Api.Controllers;

/// <summary>
/// Шлюз Callback API группы DIESEL ROOM: лайк, осмысленный комментарий, репост, опрос, подписка и разрешение
/// сообщений → баллы и рейтинг. Антифрод: секретный ключ, только своя группа, минимум 10 букв и 2 слова, не больше 2 оплаченных комментариев
/// за 24 часа, анти-копипаст, отсечка архивных постов, суточный потолок и уникальный индекс от двойных начислений.
/// </summary>
[ApiController]
[Route("api/vk")]
public class VkCallbackController : ControllerBase
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    private readonly AppDbContext _context;
    private readonly SettingsService _settings;
    private readonly VkOptions _options;
    private readonly ILogger<VkCallbackController> _logger;
    private readonly TimeProvider _clock;

    public VkCallbackController(AppDbContext context, SettingsService settings, IOptions<VkOptions> options,
        ILogger<VkCallbackController> logger, TimeProvider clock)
    {
        _context = context;
        _settings = settings;
        _options = options.Value;
        _logger = logger;
        _clock = clock;
    }

    private ContentResult Ok200() => Content("ok", "text/plain");

    [HttpPost("callback")]
    public async Task<IActionResult> HandleCallback([FromBody] VkCallbackRequest? request)
    {
        if (request == null)
        {
            return BadRequest("Отсутствует тело запроса.");
        }

        // А. Секретный ключ Callback API
        if (!string.IsNullOrWhiteSpace(_options.CallbackSecretKey) &&
            !string.Equals(request.Secret, _options.CallbackSecretKey, StringComparison.Ordinal))
        {
            _logger.LogWarning("[VK_CALLBACK_AUDIT] Action=SecretMismatch");
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        // Б. События только нашего сообщества
        if (_options.GroupId > 0 && request.GroupId.HasValue && request.GroupId.Value != _options.GroupId)
        {
            _logger.LogWarning("[VK_CALLBACK_AUDIT] Action=ForeignGroup GroupId={GroupId}", request.GroupId);
            return Ok200();
        }

        if (string.Equals(request.Type, "confirmation", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("[VK_CALLBACK_AUDIT] Action=ConfirmationHandled GroupId={GroupId}", request.GroupId);
            return Content(_options.CallbackConfirmationCode ?? string.Empty, "text/plain");
        }

        var element = ToJsonElement(request.Object);
        if (!element.HasValue) return Ok200();

        var rates = await _settings.GetRatesAsync();
        return request.Type?.ToLowerInvariant() switch
        {
            "wall_reply_new" => await HandleCommentAsync(element.Value, rates),
            "like_add" => await HandleLikeAsync(element.Value, rates),
            "wall_repost" => await HandleRepostAsync(element.Value, rates),
            "group_join" => await HandleOneTimeAsync(element.Value, OneTimeRewards.GroupJoin, rates.GroupJoinCoins, rates.GroupJoinRating,
                PointsTransactionTypes.GroupJoinBonus, "Подписка на группу DIESEL ROOM"),
            "message_allow" => await HandleOneTimeAsync(element.Value, OneTimeRewards.MessageAllow, rates.MessageAllowCoins, rates.MessageAllowRating,
                PointsTransactionTypes.MessageAllowBonus, "Разрешены сообщения от DIESEL ROOM"),
            "poll_vote_new" => await HandlePollVoteAsync(element.Value, rates),
            _ => Ok200(),
        };
    }

    private async Task<IActionResult> HandleOneTimeAsync(JsonElement obj, string activityType, int coins, int rating, string txType, string description)
    {
        var vkUserId = GetStringOrNumberProperty(obj, "user_id");
        if (!IsValidActor(vkUserId)) return Ok200();

        return await WithUserLockAsync(vkUserId!, async user =>
        {
            var awarded = await OneTimeRewards.TryAwardAsync(_context, user, activityType, 0, coins, rating, txType, description, Now);
            _logger.LogInformation("[VK_CALLBACK_AUDIT] Action={Action} Type={Type} VkId={VkId}",
                awarded ? "RewardAwarded" : "Skipped_AlreadyAwarded", activityType, vkUserId);
        });
    }

    private async Task<IActionResult> HandlePollVoteAsync(JsonElement obj, LoyaltyRates rates)
    {
        var vkUserId = GetStringOrNumberProperty(obj, "user_id");
        var pollId = GetIntProperty(obj, "poll_id");
        var ownerId = GetLongProperty(obj, "owner_id");
        if (!IsValidActor(vkUserId) || pollId is not > 0) return Ok200();

        // Опрос должен принадлежать нашей группе: на чужие VK события не шлёт, но проверяем
        if (_options.GroupId > 0 && ownerId.HasValue && ownerId.Value != -_options.GroupId)
        {
            _logger.LogInformation("[VK_CALLBACK_AUDIT] Action=Skipped_ForeignPoll VkId={VkId} Owner={Owner}", vkUserId, ownerId);
            return Ok200();
        }

        return await WithUserLockAsync(vkUserId!, async user =>
        {
            var awarded = await OneTimeRewards.TryAwardAsync(_context, user, OneTimeRewards.Poll, pollId.Value, rates.PollCoins, rates.PollRating,
                PointsTransactionTypes.PollVote, "Участие в опросе DIESEL ROOM", Now);
            _logger.LogInformation("[VK_CALLBACK_AUDIT] Action={Action} Type=Poll VkId={VkId} PollId={PollId}",
                awarded ? "RewardAwarded" : "Skipped_AlreadyAwarded", vkUserId, pollId);
        });
    }

    private async Task<IActionResult> HandleCommentAsync(JsonElement obj, LoyaltyRates rates)
    {
        var vkUserId = GetStringOrNumberProperty(obj, "from_id", "user_id");
        var postId = GetIntProperty(obj, "post_id");
        var text = GetStringProperty(obj, "text") ?? string.Empty;
        if (!IsValidActor(vkUserId) || postId is not > 0) return Ok200();

        if (postId < rates.MinRewardPostId)
        {
            _logger.LogInformation("[VK_CALLBACK_AUDIT] Action=Skipped_ArchivedPost Type=Comment VkId={VkId} PostId={PostId}", vkUserId, postId);
            return Ok200();
        }

        if (!CommentFilter.IsMeaningful(text, out var letters, out var words))
        {
            _logger.LogInformation("[VK_CALLBACK_AUDIT] Action=Skipped_NotEnoughLetters VkId={VkId} PostId={PostId} Letters={Letters} Words={Words}",
                vkUserId, postId, letters, words);
            return Ok200();
        }

        var normalized = CommentFilter.Normalize(text);
        return await WithUserLockAsync(vkUserId!, async user =>
        {
            if (await AlreadyAwardedAsync(user, vkUserId!, postId.Value, "comment")) return;

            var since = Now.AddHours(-24);
            var recent = await _context.VkActivityLogs
                .Where(l => (l.VkId == vkUserId || l.UserId == user.Id) && l.ActivityType == "comment" && l.CreatedAt >= since)
                .Select(l => l.CommentText)
                .ToListAsync();

            if (recent.Count >= rates.MaxPaidCommentsPer24h)
            {
                _logger.LogWarning("[VK_CALLBACK_AUDIT] Action=Skipped_CommentDailyLimit VkId={VkId} PaidLast24h={Count}", vkUserId, recent.Count);
                return;
            }
            if (recent.Contains(normalized))
            {
                _logger.LogInformation("[VK_CALLBACK_AUDIT] Action=Skipped_DuplicateText VkId={VkId} PostId={PostId}", vkUserId, postId);
                return;
            }

            await TryAwardAsync(user, vkUserId!, postId.Value, "comment", rates.CommentCoins, rates.CommentRating,
                PointsTransactionTypes.VkComment, "Комментарий к записи DIESEL ROOM", rates, normalized);
        });
    }

    private async Task<IActionResult> HandleLikeAsync(JsonElement obj, LoyaltyRates rates)
    {
        if (!string.Equals(GetStringProperty(obj, "object_type"), "post", StringComparison.OrdinalIgnoreCase)) return Ok200();

        var vkUserId = GetStringOrNumberProperty(obj, "liker_id", "user_id");
        var postId = GetIntProperty(obj, "object_id", "post_id");
        if (!IsValidActor(vkUserId) || postId is not > 0) return Ok200();

        if (postId < rates.MinRewardPostId)
        {
            _logger.LogInformation("[VK_CALLBACK_AUDIT] Action=Skipped_ArchivedPost Type=Like VkId={VkId} PostId={PostId}", vkUserId, postId);
            return Ok200();
        }

        return await WithUserLockAsync(vkUserId!, async user =>
        {
            if (await AlreadyAwardedAsync(user, vkUserId!, postId.Value, "like")) return;
            await TryAwardAsync(user, vkUserId!, postId.Value, "like", rates.LikeCoins, rates.LikeRating,
                PointsTransactionTypes.VkLike, "Лайк записи DIESEL ROOM", rates);
        });
    }

    private async Task<IActionResult> HandleRepostAsync(JsonElement obj, LoyaltyRates rates)
    {
        var ownerId = GetLongProperty(obj, "owner_id") ?? 0;
        var fromId = GetLongProperty(obj, "from_id") ?? 0;
        var vkUserId = ownerId > 0 ? ownerId.ToString() : fromId > 0 ? fromId.ToString() : null;

        // Репостнутая запись — copy_history[0]; она должна принадлежать нашей группе (owner_id = -GroupId)
        int? postId = null;
        if (obj.TryGetProperty("copy_history", out var history) && history.ValueKind == JsonValueKind.Array && history.GetArrayLength() > 0)
        {
            var original = history[0];
            postId = GetIntProperty(original, "id", "post_id");
            var originalOwner = GetLongProperty(original, "owner_id", "from_id");
            if (_options.GroupId > 0 && originalOwner.HasValue && originalOwner.Value != -_options.GroupId)
            {
                _logger.LogInformation("[VK_CALLBACK_AUDIT] Action=Skipped_ForeignRepost VkId={VkId} Owner={Owner}", vkUserId, originalOwner);
                return Ok200();
            }
        }
        postId ??= GetIntProperty(obj, "post_id");

        if (!IsValidActor(vkUserId) || postId is not > 0) return Ok200();
        if (postId < rates.MinRewardPostId) return Ok200();

        return await WithUserLockAsync(vkUserId!, async user =>
        {
            if (await AlreadyAwardedAsync(user, vkUserId!, postId.Value, "repost")) return;
            await TryAwardAsync(user, vkUserId!, postId.Value, "repost", rates.RepostCoins, rates.RepostRating,
                PointsTransactionTypes.VkRepost, "Репост записи DIESEL ROOM", rates);
        });
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    private static bool IsValidActor(string? vkUserId) => long.TryParse(vkUserId, out var id) && id > 0;

    /// <summary>Находит гостя и выполняет обработку под замком на VK ID (одновременные события одного гостя — по очереди).</summary>
    private async Task<IActionResult> WithUserLockAsync(string vkUserId, Func<User, Task> work)
    {
        var trimmed = vkUserId.Trim();
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.VkId == trimmed);
        if (user == null)
        {
            _logger.LogInformation("[VK_CALLBACK_AUDIT] Action=Skipped_UserNotFound VkId={VkId}", vkUserId);
            return Ok200();
        }

        // Участие в программе начинается с принятия условий
        if (user.ConsentAcceptedAt == null)
        {
            _logger.LogInformation("[VK_CALLBACK_AUDIT] Action=Skipped_NoConsent VkId={VkId}", vkUserId);
            return Ok200();
        }

        var lockKey = $"vk_reward_{trimmed}";
        var sem = Locks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync();
        try
        {
            await work(user);
        }
        finally
        {
            sem.Release();
            Locks.TryRemove(new KeyValuePair<string, SemaphoreSlim>(lockKey, sem));
        }
        return Ok200();
    }

    private async Task<bool> AlreadyAwardedAsync(User user, string vkUserId, int postId, string activityType)
    {
        var exists = await _context.VkActivityLogs.AnyAsync(l =>
            (l.VkId == vkUserId || l.UserId == user.Id) && l.PostId == postId && l.ActivityType == activityType);
        if (exists)
        {
            _logger.LogInformation("[VK_CALLBACK_AUDIT] Action=Skipped_AlreadyAwarded Type={Type} VkId={VkId} PostId={PostId}", activityType, vkUserId, postId);
        }
        return exists;
    }

    /// <summary>
    /// Запись активности + начисление в одной транзакции. Уникальный индекс VkActivityLogs (VkId, PostId, тип)
    /// не даст начислить дважды даже при параллельной доставке одного события (VK повторяет Callback при таймауте).
    /// </summary>
    private async Task TryAwardAsync(User user, string vkUserId, int postId, string activityType, int coins, int rating,
        string txType, string description, LoyaltyRates rates, string? commentText = null)
    {
        var now = Now;
        // Сутки — по Перми (UTC+5): лимит обнуляется в полночь заведения, а не в 05:00
        var dayStartUtc = PermTime.ToUtc(PermTime.LocalDate(now), TimeOnly.MinValue);
        var earnedToday = await _context.PointsTransactions
            .Where(t => t.UserId == user.Id && t.CreatedAt >= dayStartUtc && PointsTransactionTypes.SocialTypes.Contains(t.Type))
            .SumAsync(t => (int?)t.Coins) ?? 0;
        if (earnedToday >= rates.DailySocialCoinsCap)
        {
            _logger.LogWarning("[VK_CALLBACK_AUDIT] Action=Skipped_DailyLimitExceeded VkId={VkId} EarnedToday={Earned}", vkUserId, earnedToday);
            return;
        }

        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            _context.VkActivityLogs.Add(new VkActivityLog
            {
                UserId = user.Id,
                VkId = vkUserId,
                ActivityType = activityType,
                PostId = postId,
                CommentText = commentText,
                CreatedAt = now,
            });
            await _context.SaveChangesAsync();
            await Ledger.AwardAsync(_context, user.Id, coins, rating, txType, description, now);
            await tx.CommitAsync();

            _logger.LogInformation("[VK_CALLBACK_AUDIT] Action=RewardAwarded Type={Type} VkId={VkId} UserId={UserId} PostId={PostId} Coins=+{Coins} Rating=+{Rating}",
                activityType, vkUserId, user.Id, postId, coins, rating);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            _context.ChangeTracker.Clear();
            _logger.LogInformation("[VK_CALLBACK_AUDIT] Action=Skipped_AlreadyAwarded Type={Type} VkId={VkId} PostId={PostId}", activityType, vkUserId, postId);
        }
    }

    private static JsonElement? ToJsonElement(object? obj)
    {
        if (obj == null) return null;
        if (obj is JsonElement je) return je.ValueKind == JsonValueKind.Object ? je : null;
        try
        {
            var el = JsonSerializer.SerializeToElement(obj);
            return el.ValueKind == JsonValueKind.Object ? el : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetStringOrNumberProperty(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var prop)) continue;
            if (prop.ValueKind == JsonValueKind.String) return prop.GetString();
            if (prop.ValueKind == JsonValueKind.Number) return prop.GetRawText();
        }
        return null;
    }

    private static string? GetStringProperty(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String) return prop.GetString();
        }
        return null;
    }

    private static int? GetIntProperty(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var prop)) continue;
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var val)) return val;
            if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static long? GetLongProperty(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var prop)) continue;
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var val)) return val;
            if (prop.ValueKind == JsonValueKind.String && long.TryParse(prop.GetString(), out var parsed)) return parsed;
        }
        return null;
    }
}
