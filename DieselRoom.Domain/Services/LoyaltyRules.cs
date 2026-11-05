using System.Text.RegularExpressions;

namespace DieselRoom.Domain.Services;

/// <summary>Ставки начисления (настраиваются в админке). Значения по умолчанию — из ТЗ.</summary>
public sealed record LoyaltyRates
{
    public int LikeCoins { get; init; } = 1;
    public int LikeRating { get; init; } = 1;
    public int CommentCoins { get; init; } = 3;
    public int CommentRating { get; init; } = 3;
    public int RepostCoins { get; init; } = 10;
    public int RepostRating { get; init; } = 10;
    public int VisitCoins { get; init; } = 20;
    public int VisitRating { get; init; } = 10;
    public int ReferralCoins { get; init; } = 100;
    public int ReferralRating { get; init; } = 5;
    /// <summary>Оплачиваемых комментариев за скользящие 24 часа.</summary>
    public int MaxPaidCommentsPer24h { get; init; } = 2;
    /// <summary>Потолок баллов за соцактивность (лайки/комменты/репосты) за сутки.</summary>
    public int DailySocialCoinsCap { get; init; } = 30;
    /// <summary>Записи стены с меньшим post_id считаются архивными: за них ничего не начисляется.</summary>
    public int MinRewardPostId { get; init; } = 0;
    /// <summary>Разовый бонус за заполненный профиль (имя и телефон).</summary>
    public int ProfileBonusCoins { get; init; } = 30;
    public int ProfileBonusRating { get; init; } = 5;
    /// <summary>Разовый бонус за подписку на группу.</summary>
    public int GroupJoinCoins { get; init; } = 20;
    public int GroupJoinRating { get; init; } = 5;
    /// <summary>Разовый бонус за разрешение сообщений от группы.</summary>
    public int MessageAllowCoins { get; init; } = 20;
    public int MessageAllowRating { get; init; } = 5;
    /// <summary>За участие в опросе группы: один раз на опрос.</summary>
    public int PollCoins { get; init; } = 5;
    public int PollRating { get; init; } = 2;
    /// <summary>Предупреждать «До приза осталось X очков», когда до следующей призовой зоны не больше стольких очков; 0 — выключено.</summary>
    public int ApproachNotifyPoints { get; init; } = 15;

    public static readonly LoyaltyRates Default = new();

    public IEnumerable<string> Validate()
    {
        foreach (var (name, value) in new (string, int)[]
                 {
                     (nameof(LikeCoins), LikeCoins), (nameof(LikeRating), LikeRating),
                     (nameof(CommentCoins), CommentCoins), (nameof(CommentRating), CommentRating),
                     (nameof(RepostCoins), RepostCoins), (nameof(RepostRating), RepostRating),
                     (nameof(VisitCoins), VisitCoins), (nameof(VisitRating), VisitRating),
                     (nameof(ReferralCoins), ReferralCoins), (nameof(ReferralRating), ReferralRating),
                     (nameof(MaxPaidCommentsPer24h), MaxPaidCommentsPer24h), (nameof(DailySocialCoinsCap), DailySocialCoinsCap),
                     (nameof(ProfileBonusCoins), ProfileBonusCoins), (nameof(ProfileBonusRating), ProfileBonusRating),
                     (nameof(GroupJoinCoins), GroupJoinCoins), (nameof(GroupJoinRating), GroupJoinRating),
                     (nameof(MessageAllowCoins), MessageAllowCoins), (nameof(MessageAllowRating), MessageAllowRating),
                     (nameof(PollCoins), PollCoins), (nameof(PollRating), PollRating),
                     (nameof(ApproachNotifyPoints), ApproachNotifyPoints),
                 })
        {
            if (value < 0 || value > 10_000) yield return $"{name}: значение должно быть от 0 до 10000.";
        }
        if (MinRewardPostId < 0) yield return $"{nameof(MinRewardPostId)}: не может быть отрицательным.";
    }
}

/// <summary>Антифрод комментариев: минимум 10 букв (char.IsLetter) и 2 слова, нормализация для анти-копипаста.</summary>
public static class CommentFilter
{
    public const int MinLetters = 10;
    public const int MinWords = 2;
    public const int MaxStoredLength = 500;

    // «Слово» — подряд идущие буквы (не меньше двух); эмодзи, цифры и знаки препинания словами не считаются
    private static readonly Regex LetterWordRegex = new(@"\p{L}{2,}", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public static bool IsMeaningful(string? text, out int letters, out int words)
    {
        text ??= string.Empty;
        // string.Length считает UTF-16 code units (3 эмодзи дают 6), поэтому считаем именно буквы
        letters = text.Count(char.IsLetter);
        words = LetterWordRegex.Matches(text).Count;
        return letters >= MinLetters && words >= MinWords;
    }

    /// <summary>Текст для сравнения на копипаст: без регистра и лишних пробелов, с ограничением длины колонки.</summary>
    public static string Normalize(string text)
    {
        var normalized = WhitespaceRegex.Replace(text.Trim(), " ").ToLowerInvariant();
        return normalized.Length > MaxStoredLength ? normalized[..MaxStoredLength] : normalized;
    }
}

/// <summary>Коды купонов #DR-XXXX и номера чеков.</summary>
public static class Codes
{
    // Без похожих символов (0/O, 1/I/L), чтобы официант не ошибся при ручном вводе
    public const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const string CodePrefix = "DR-";
    public const int CodeLength = 4;

    private static readonly Regex CodeRegex = new(@"^#?\s*(?:DR)?\s*-?\s*([A-Z0-9]{4})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ReceiptRegex = new(@"^[A-ZА-ЯЁ0-9\-/]{1,32}$", RegexOptions.Compiled);

    public static string GenerateOrderCode(Random random)
    {
        Span<char> chars = stackalloc char[CodeLength];
        for (var i = 0; i < CodeLength; i++)
        {
            chars[i] = CodeAlphabet[random.Next(CodeAlphabet.Length)];
        }
        return CodePrefix + new string(chars);
    }

    /// <summary>«#DR-ab3k», «dr ab3k», «AB3K» → «DR-AB3K»; иначе null.</summary>
    public static string? NormalizeOrderCode(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var match = CodeRegex.Match(input.Trim());
        return match.Success ? CodePrefix + match.Groups[1].Value.ToUpperInvariant() : null;
    }

    /// <summary>Номер чека: без пробелов, в верхнем регистре; null — если формат недопустим.</summary>
    public static string? NormalizeReceiptNumber(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var compact = new string(input.Where(c => !char.IsWhiteSpace(c)).ToArray()).TrimStart('#').ToUpperInvariant();
        return ReceiptRegex.IsMatch(compact) ? compact : null;
    }

    /// <summary>Телефон → только цифры в формате 7XXXXXXXXXX; null — если номер некорректный.</summary>
    public static string? NormalizePhone(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var digits = new string(input.Where(char.IsDigit).ToArray());
        if (digits.Length == 10) digits = "7" + digits;
        if (digits.Length == 11 && digits[0] == '8') digits = "7" + digits[1..];
        return digits.Length == 11 && digits[0] == '7' ? digits : null;
    }

    private static readonly Regex PrivilegeCodeRegex = new(@"^(?<prefix>[PpРрПп]\s*-?\s*)?(?<a>\d{3})[\s-]?(?<b>\d{3})$", RegexOptions.Compiled);

    /// <summary>«P-482913», «482 913» → «482913»; иначе null. <paramref name="prefixed"/> — код из QR, а не просто шесть цифр.</summary>
    public static string? NormalizePrivilegeCode(string? input, out bool prefixed)
    {
        prefixed = false;
        if (string.IsNullOrWhiteSpace(input)) return null;
        var match = PrivilegeCodeRegex.Match(input.Trim());
        if (!match.Success) return null;
        prefixed = match.Groups["prefix"].Success;
        return match.Groups["a"].Value + match.Groups["b"].Value;
    }
}

/// <summary>Склонения в сообщениях гостям.</summary>
public static class RuText
{
    public static string Points(int n)
    {
        var abs = Math.Abs(n) % 100;
        var last = abs % 10;
        if (abs is > 10 and < 20) return $"{n} очков";
        return last switch
        {
            1 => $"{n} очко",
            >= 2 and <= 4 => $"{n} очка",
            _ => $"{n} очков",
        };
    }

    /// <summary>«со 2-го места», «с 3-го места».</summary>
    public static string FromPlace(int rank) => $"{(rank == 2 ? "со" : "с")} {rank}-го места";
}

/// <summary>Редакция условий программы и согласия на обработку персональных данных. Новая редакция — новое согласие.</summary>
public static class LegalInfo
{
    public const string CurrentVersion = "2026-11";

    public static bool HasCurrentConsent(Entities.User user) =>
        user.ConsentAcceptedAt != null && user.AdultConfirmedAt != null && user.ConsentVersion == CurrentVersion;
}

/// <summary>Порог уровня гостя: от скольких очков «за всё время» он начинается.</summary>
public sealed record LevelRule(string Title, int MinRating);

/// <summary>Уровень гостя и прогресс до следующего.</summary>
public sealed record GuestLevel(int Index, string Title, int MinRating, string? NextTitle, int? NextMinRating, int ProgressPercent, int? PointsToNext);

/// <summary>Уровни гостя считаются по очкам «за всё время»: они не обнуляются и не уменьшаются при покупках.</summary>
public static class GuestLevels
{
    public const int MaxTitleLength = 40;

    public static readonly IReadOnlyList<LevelRule> Default =
    [
        new("Гость", 0),
        new("Свой", 100),
        new("Резидент", 500),
        new("Легенда Дизеля", 1500),
    ];

    public static IEnumerable<string> Validate(IReadOnlyList<LevelRule>? levels)
    {
        if (levels is not { Count: >= 2 and <= 8 })
        {
            yield return "Нужно от 2 до 8 уровней.";
            yield break;
        }
        if (levels[0].MinRating != 0) yield return "Первый уровень начинается с 0 очков.";
        for (var i = 0; i < levels.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(levels[i].Title) || levels[i].Title.Trim().Length > MaxTitleLength)
                yield return $"Название уровня: от 1 до {MaxTitleLength} символов.";
            if (i > 0 && levels[i].MinRating <= levels[i - 1].MinRating)
                yield return "Каждый следующий уровень должен начинаться с большего числа очков.";
        }
    }

    public static GuestLevel Compute(int totalRating, IReadOnlyList<LevelRule> levels)
    {
        var index = 0;
        for (var i = 0; i < levels.Count; i++)
        {
            if (totalRating >= levels[i].MinRating) index = i;
        }

        var current = levels[index];
        if (index == levels.Count - 1)
        {
            return new GuestLevel(index, current.Title, current.MinRating, null, null, 100, null);
        }

        var next = levels[index + 1];
        var percent = (totalRating - current.MinRating) * 100 / (next.MinRating - current.MinRating);
        return new GuestLevel(index, current.Title, current.MinRating, next.Title, next.MinRating,
            Math.Clamp(percent, 0, 100), next.MinRating - totalRating);
    }
}

/// <summary>Достижения гостя: вычисляются из статистики, отдельной таблицы не требуют.</summary>
public sealed record Achievement(string Key, string Icon, string Title, string Description, bool Unlocked, int Progress, int Target);

/// <summary>
/// Правило достижения, настраиваемое в админке. Metric — что считается: visits (визиты по чекам), referrals (друзья,
/// ставшие гостями), gifts (полученные подарки), podium (гость хотя бы раз входил в Топ-Target недельной гонки).
/// </summary>
public sealed record AchievementRule(string Key, string Icon, string Title, string Description, string Metric, int Target, bool Enabled = true);

public static class Achievements
{
    public const string MetricVisits = "visits";
    public const string MetricReferrals = "referrals";
    public const string MetricGifts = "gifts";
    public const string MetricPodium = "podium";
    public static readonly string[] Metrics = [MetricVisits, MetricReferrals, MetricGifts, MetricPodium];

    public const int MaxRules = 20;

    private static readonly Regex KeyRegex = new(@"^[a-z0-9_]{1,32}$", RegexOptions.Compiled);
    private static readonly Regex IconRegex = new(@"^[a-z-]{1,32}$", RegexOptions.Compiled);

    public static readonly IReadOnlyList<AchievementRule> Default =
    [
        new("first_visit", "door", "Первый визит", "Первый визит по чеку", MetricVisits, 1),
        new("visits_5", "flame", "5 посещений", "5 визитов по чекам", MetricVisits, 5),
        new("visits_10", "ant", "10 посещений", "10 визитов по чекам", MetricVisits, 10),
        new("first_friend", "users", "Привёл друга", "Друг пришёл по вашей ссылке", MetricReferrals, 1),
        new("first_gift", "gift", "Первый подарок", "Получили подарок из магазина", MetricGifts, 1),
        new("podium", "trophy", "Пьедестал", "Топ-3 недельной гонки", MetricPodium, 3),
    ];

    public static IEnumerable<string> Validate(IReadOnlyList<AchievementRule>? rules)
    {
        if (rules is not { Count: <= MaxRules }) { yield return $"Достижений — не больше {MaxRules}."; yield break; }

        var seen = new HashSet<string>();
        foreach (var r in rules)
        {
            if (r.Key == null || !KeyRegex.IsMatch(r.Key)) yield return "Ключ достижения: латиница, цифры и «_», до 32 символов.";
            else if (!seen.Add(r.Key)) yield return $"Ключ «{r.Key}» повторяется.";
            if (string.IsNullOrWhiteSpace(r.Title) || r.Title.Trim().Length > 40) yield return "Название достижения: от 1 до 40 символов.";
            if (string.IsNullOrWhiteSpace(r.Description) || r.Description.Trim().Length > 100) yield return "Условие достижения: от 1 до 100 символов.";
            if (r.Icon == null || !IconRegex.IsMatch(r.Icon)) yield return "Иконка достижения: ключ из списка.";
            if (!Metrics.Contains(r.Metric)) yield return "Неизвестный показатель достижения.";
            else if (r.Target < 1 || r.Target > (r.Metric == MetricPodium ? PrivilegeCatalog.LastPrizeRank : 1000))
                yield return r.Metric == MetricPodium ? $"Место для пьедестала — от 1 до {PrivilegeCatalog.LastPrizeRank}." : "Цель достижения — от 1 до 1000.";
        }
    }

    public static IReadOnlyList<Achievement> Compute(int visits, int paidReferrals, int giftsReceived, int bestWeeklyRank,
        IReadOnlyList<AchievementRule>? rules = null)
    {
        return (rules ?? Default).Where(r => r.Enabled).Select(r =>
        {
            if (r.Metric == MetricPodium)
            {
                var reached = bestWeeklyRank >= 1 && bestWeeklyRank <= r.Target;
                return new Achievement(r.Key, r.Icon, r.Title, r.Description, reached, reached ? 1 : 0, 1);
            }
            var value = r.Metric switch { MetricVisits => visits, MetricReferrals => paidReferrals, _ => giftsReceived };
            return new Achievement(r.Key, r.Icon, r.Title, r.Description, value >= r.Target, Math.Min(value, r.Target), r.Target);
        }).ToList();
    }
}
