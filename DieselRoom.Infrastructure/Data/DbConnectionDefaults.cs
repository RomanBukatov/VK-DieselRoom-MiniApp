using Npgsql;

namespace DieselRoom.Infrastructure.Data;

/// <summary>
/// Защита PostgreSQL от перегрузки со стороны API. Значения, явно заданные в строке подключения, не перетираются.
/// </summary>
public static class DbConnectionDefaults
{
    /// <summary>
    /// Пул API меньше max_connections базы (100): при затыке API не съест все соединения и память контейнера БД
    /// (лимит 400 МБ), а остаток останется для pg_dump, psql и миграций.
    /// </summary>
    public const int MaxPoolSize = 30;

    /// <summary>Сколько запрос ждёт свободное соединение из пула, прежде чем вернуть ошибку (а не висеть бесконечно).</summary>
    public const int PoolWaitSeconds = 15;

    /// <summary>Запрос дольше этого сервер базы прерывает сам (у нас обычные запросы — миллисекунды).</summary>
    public const int StatementTimeoutMs = 30_000;

    /// <summary>Транзакцию, открытую и брошенную без запросов, база закрывает и снимает её блокировки.</summary>
    public const int IdleInTransactionTimeoutMs = 60_000;

    public static string Apply(string? connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString ?? string.Empty);

        if (!HasKey(connectionString, "Maximum Pool Size", "Max Pool Size", "MaxPoolSize"))
            builder.MaxPoolSize = MaxPoolSize;

        if (!HasKey(connectionString, "Timeout"))
            builder.Timeout = PoolWaitSeconds;

        // Настройки сессии PostgreSQL: действуют только на соединения API (pg_dump и psql работают без ограничений)
        var options = builder.Options ?? string.Empty;
        if (!options.Contains("statement_timeout", StringComparison.OrdinalIgnoreCase))
            options += $" -c statement_timeout={StatementTimeoutMs}";
        if (!options.Contains("idle_in_transaction_session_timeout", StringComparison.OrdinalIgnoreCase))
            options += $" -c idle_in_transaction_session_timeout={IdleInTransactionTimeoutMs}";
        builder.Options = options.Trim();

        return builder.ConnectionString;
    }

    private static bool HasKey(string? connectionString, params string[] keys)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return false;
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = part.Split('=', 2)[0].Trim();
            if (keys.Any(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }
}
