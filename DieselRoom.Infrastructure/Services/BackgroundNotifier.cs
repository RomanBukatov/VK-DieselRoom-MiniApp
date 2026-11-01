using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DieselRoom.Infrastructure.Services;

/// <summary>
/// Запуск вспомогательных задач (уведомления, вебхуки) в фоне, чтобы они не задерживали HTTP-ответ пользователю
/// и не удерживали соединение с БД. Ошибки фоновых задач только логируются. Активные задачи отслеживаются, чтобы
/// их можно было дождаться (<see cref="DrainAsync"/>) — при остановке приложения и в тестах.
/// </summary>
public static class BackgroundNotifier
{
    private static readonly ConcurrentDictionary<Task, byte> Pending = new();

    public static int PendingCount => Pending.Count;

    public static void Run(Func<Task> work, ILogger? logger = null, string description = "фоновая задача")
    {
        var task = Task.Run(async () =>
        {
            try
            {
                await work();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Ошибка в фоновой задаче: {Description}", description);
            }
        });

        Pending[task] = 0;
        task.ContinueWith(t => Pending.TryRemove(t, out _), TaskScheduler.Default);
    }

    /// <summary>Ждёт завершения всех запущенных фоновых задач (но не дольше <paramref name="timeout"/>).</summary>
    public static async Task DrainAsync(TimeSpan? timeout = null)
    {
        var tasks = Pending.Keys.ToArray();
        if (tasks.Length == 0)
        {
            return;
        }

        var all = Task.WhenAll(tasks);
        var limit = timeout ?? TimeSpan.FromSeconds(30);
        await Task.WhenAny(all, Task.Delay(limit));
    }
}
