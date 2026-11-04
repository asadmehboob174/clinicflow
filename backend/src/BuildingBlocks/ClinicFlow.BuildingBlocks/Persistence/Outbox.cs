using ClinicFlow.BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClinicFlow.BuildingBlocks.Persistence;

public interface IOutboxPublisher
{
    Task<int> PublishPending(CancellationToken ct);
}

/// <summary>At-least-once delivery. A consumer can see an event twice, so consumers must be idempotent.</summary>
public sealed class OutboxPublisher<TContext>(TContext db, IEventBus bus, ILogger<OutboxPublisher<TContext>> logger)
    : IOutboxPublisher
    where TContext : ModuleDbContext
{
    private const int BatchSize = 50;
    private const int MaxAttempts = 10;

    public async Task<int> PublishPending(CancellationToken ct)
    {
        var batch = await db.OutboxMessages
            .Where(m => m.ProcessedAtUtc == null && m.Attempts < MaxAttempts)
            .OrderBy(m => m.CreatedAtUtc)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var message in batch)
        {
            try
            {
                await bus.Publish(new EventEnvelope(message.Id, message.Type, message.Payload), ct);
                message.ProcessedAtUtc = DateTime.UtcNow;
                message.LastError = null;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                message.Attempts++;
                message.LastError = exception.Message[..Math.Min(exception.Message.Length, 2000)];
                logger.LogWarning(exception, "Could not publish outbox message {MessageId}", message.Id);
            }
        }

        await db.SaveChangesAsync(ct);
        return batch.Count;
    }
}

public sealed class OutboxWorker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<OutboxWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = configuration.GetValue("Outbox:IntervalSeconds", 2);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                foreach (var publisher in scope.ServiceProvider.GetServices<IOutboxPublisher>())
                {
                    await publisher.PublishPending(stoppingToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Outbox run failed. It will try again on the next tick");
            }
        }
    }
}
