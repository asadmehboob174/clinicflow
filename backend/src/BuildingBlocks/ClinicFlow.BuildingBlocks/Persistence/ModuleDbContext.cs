using System.Text.Json;
using ClinicFlow.BuildingBlocks.Domain;
using ClinicFlow.BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;

namespace ClinicFlow.BuildingBlocks.Persistence;

public sealed class OutboxMessage
{
    public Guid Id { get; init; }
    public required string Type { get; init; }
    public required string Payload { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? ProcessedAtUtc { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

/// <summary>
/// One DbContext and one SQL schema per module. On save, domain events become outbox rows in the
/// same transaction as the state change, so an event is never lost and never sent for a failed save.
/// </summary>
public abstract class ModuleDbContext(DbContextOptions options) : DbContext(options)
{
    protected abstract string Schema { get; }

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>Returns the public event for a domain event, or null if it stays inside the module.</summary>
    protected virtual IntegrationEvent? ToIntegrationEvent(IDomainEvent domainEvent) => null;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<OutboxMessage>(outbox =>
        {
            outbox.ToTable("OutboxMessages");
            outbox.HasKey(m => m.Id);
            outbox.Property(m => m.Type).HasMaxLength(200);
            outbox.Property(m => m.LastError).HasMaxLength(2000);
            outbox.HasIndex(m => new { m.ProcessedAtUtc, m.CreatedAtUtc });
        });
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var aggregates = ChangeTracker.Entries<IHasDomainEvents>().Select(e => e.Entity).ToList();

        foreach (var domainEvent in aggregates.SelectMany(a => a.DomainEvents))
        {
            if (ToIntegrationEvent(domainEvent) is { } integrationEvent)
            {
                OutboxMessages.Add(new OutboxMessage
                {
                    Id = integrationEvent.EventId,
                    Type = integrationEvent.GetType().Name,
                    Payload = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), OutboxJson.Options),
                    CreatedAtUtc = integrationEvent.OccurredAtUtc,
                });
            }
        }

        var saved = await base.SaveChangesAsync(cancellationToken);
        aggregates.ForEach(a => a.ClearDomainEvents());
        return saved;
    }
}
