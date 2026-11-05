using DieselRoom.Domain.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DieselRoom.Infrastructure.Services;

/// <summary>
/// Фоновые задачи программы лояльности (раз в минуту):
/// 1) просроченные купоны → Expired с возвратом баллов и товара на склад;
/// 2) уведомления «Вас сместили» при выпадении из призового тира и «До приза осталось X очков» при приближении к нему;
/// 3) в 23:59 по Перми — срез лидера дня (трекер 7 дней на 1-м месте), затем в воскресенье — сброс недели.
/// </summary>
public sealed class LoyaltyBackgroundWorker : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    /// <summary>Срез лидера дня: окно с 23:55 — с запасом, чтобы минутный таймер не проскочил его до сброса в 23:59.</summary>
    public static readonly TimeOnly DailySnapshotLocalTime = new(23, 55);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LoyaltyBackgroundWorker> _logger;

    public LoyaltyBackgroundWorker(IServiceScopeFactory scopeFactory, ILogger<LoyaltyBackgroundWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Даём приложению стартовать и применить миграции
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            await RunOnceAsync(DateTime.UtcNow, stoppingToken);
        }
        while (await WaitNextAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitNextAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public async Task RunOnceAsync(DateTime now, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;

        await SafeAsync("истечение купонов", async () =>
        {
            var expired = await services.GetRequiredService<ShopService>().ExpireOverdueAsync(now, ct);
            if (expired > 0) _logger.LogInformation("Истекло купонов: {Count} (баллы и товар возвращены)", expired);
        });

        var rating = services.GetRequiredService<RatingService>();
        var notifier = services.GetRequiredService<IGuestNotifier>();

        var displaced = new HashSet<int>();
        await SafeAsync("уведомления о смещении", async () =>
        {
            foreach (var notice in await rating.DetectDisplacementsAsync(now, ct))
            {
                displaced.Add(notice.UserId);
                notifier.Send(notice.VkId, notice.Message, $"смещение #{notice.OldRank}→#{notice.NewRank}");
            }
        });

        await SafeAsync("уведомления о приближении к призу", async () =>
        {
            foreach (var notice in await rating.DetectApproachesAsync(now, displaced, ct))
            {
                notifier.Send(notice.VkId, notice.Message, $"приближение к {notice.Goal}: {notice.PointsLeft}");
            }
        });

        if (TimeOnly.FromDateTime(PermTime.ToLocal(now)) >= DailySnapshotLocalTime)
        {
            await SafeAsync("срез лидера дня", () => rating.TakeDailySnapshotAsync(now, ct));
        }

        await SafeAsync("еженедельный сброс", () => rating.RunWeeklyResetIfDueAsync(now, ct));
    }

    private async Task SafeAsync(string name, Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка фоновой задачи лояльности: {Task}", name);
        }
    }
}
