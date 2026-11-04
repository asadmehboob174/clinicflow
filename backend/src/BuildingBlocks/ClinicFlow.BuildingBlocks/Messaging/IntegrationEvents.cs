namespace ClinicFlow.BuildingBlocks.Messaging;

/// <summary>A fact one module publishes for other modules. Lives in a module's Contracts project.</summary>
public abstract record IntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
}

public interface IIntegrationEventHandler<in TEvent> where TEvent : IntegrationEvent
{
    Task Handle(TEvent integrationEvent, CancellationToken ct);
}

public sealed record EventEnvelope(Guid Id, string Type, string Payload);

public interface IEventBus
{
    Task Publish(EventEnvelope envelope, CancellationToken ct);
}
