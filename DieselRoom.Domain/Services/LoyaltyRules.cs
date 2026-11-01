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
}

/// <summary>Достижения гостя: вычисляются из статистики, отдельной таблицы не требуют.</summary>
public sealed record Achievement(string Key, string Emoji, string Title, string Description, bool Unlocked, int Progress, int Target);

public static class Achievements
{
    public static IReadOnlyList<Achievement> Compute(int visits, int paidReferrals, int giftsReceived, int bestWeeklyRank)
    {
        return
        [
            new("first_visit", "🚪", "Первый визит", "Первый визит по чеку", visits >= 1, Math.Min(visits, 1), 1),
            new("visits_5", "🔥", "5 посещений", "5 визитов по чекам", visits >= 5, Math.Min(visits, 5), 5),
            new("visits_10", "🐜", "Свой в Diesel Room", "10 визитов по чекам", visits >= 10, Math.Min(visits, 10), 10),
            new("first_friend", "🤝", "Привёл друга", "Друг пришёл по вашей ссылке", paidReferrals >= 1, Math.Min(paidReferrals, 1), 1),
            new("first_gift", "🎁", "Первый подарок", "Получили подарок из магазина", giftsReceived >= 1, Math.Min(giftsReceived, 1), 1),
            new("podium", "🏆", "Пьедестал", "Топ-3 недельной гонки", bestWeeklyRank is >= 1 and <= 3, bestWeeklyRank is >= 1 and <= 3 ? 1 : 0, 1),
        ];
    }
}
