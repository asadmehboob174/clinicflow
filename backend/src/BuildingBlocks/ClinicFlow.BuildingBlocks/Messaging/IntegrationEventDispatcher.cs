using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicFlow.BuildingBlocks.Messaging;

/// <summary>Maps stable event names to CLR types, so a queue message never carries an assembly name.</summary>
public sealed class IntegrationEventRegistry
{
    private readonly ConcurrentDictionary<string, Type> _types = new();

    public void Scan(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(IntegrationEvent)) && !t.IsAbstract))
        {
            _types[type.Name] = type;
        }
    }

    public Type? Find(string name) => _types.GetValueOrDefault(name);
}

/// <summary>Runs every handler for one event, in its own DI scope. Used by both bus types.</summary>
public sealed class IntegrationEventDispatcher(IServiceScopeFactory scopes, IntegrationEventRegistry registry)
{
    private static readonly MethodInfo DispatchMethod = typeof(IntegrationEventDispatcher)
        .GetMethod(nameof(DispatchTyped), BindingFlags.NonPublic | BindingFlags.Static)!;

    public async Task Dispatch(EventEnvelope envelope, CancellationToken ct)
    {
        var type = registry.Find(envelope.Type)
            ?? throw new InvalidOperationException($"Unknown integration event '{envelope.Type}'.");

        var integrationEvent = JsonSerializer.Deserialize(envelope.Payload, type, OutboxJson.Options)!;

        await using var scope = scopes.CreateAsyncScope();
        var task = (Task)DispatchMethod.MakeGenericMethod(type)
            .Invoke(null, [scope.ServiceProvider, integrationEvent, ct])!;
        await task;
    }

    private static async Task DispatchTyped<TEvent>(IServiceProvider services, TEvent integrationEvent, CancellationToken ct)
        where TEvent : IntegrationEvent
    {
        foreach (var handler in services.GetServices<IIntegrationEventHandler<TEvent>>())
        {
            await handler.Handle(integrationEvent, ct);
        }
    }
}

public static class OutboxJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public sealed class InMemoryEventBus(IntegrationEventDispatcher dispatcher) : IEventBus
{
    public Task Publish(EventEnvelope envelope, CancellationToken ct) => dispatcher.Dispatch(envelope, ct);
}
