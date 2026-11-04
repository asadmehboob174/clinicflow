using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ClinicFlow.BuildingBlocks.Messaging;

public sealed class RabbitMqConnection(IConfiguration configuration) : IAsyncDisposable
{
    public const string Exchange = "clinicflow.events";
    public const string Queue = "clinicflow.host";
    public const string DeadLetterExchange = "clinicflow.dead";
    public const string DeadLetterQueue = "clinicflow.host.dead";

    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;

    public async Task<IChannel> OpenChannel(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_connection is not { IsOpen: true })
            {
                var factory = new ConnectionFactory
                {
                    Uri = new Uri(configuration["Messaging:RabbitMq"]
                        ?? throw new InvalidOperationException("Messaging:RabbitMq is not set.")),
                    AutomaticRecoveryEnabled = true,
                };
                _connection = await factory.CreateConnectionAsync(ct);
            }
        }
        finally
        {
            _lock.Release();
        }

        var channel = await _connection.CreateChannelAsync(cancellationToken: ct);

        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Fanout, durable: true, cancellationToken: ct);
        await channel.QueueDeclareAsync(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(DeadLetterQueue, DeadLetterExchange, string.Empty, cancellationToken: ct);
        await channel.QueueDeclareAsync(
            Queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = DeadLetterExchange },
            cancellationToken: ct);
        await channel.QueueBindAsync(Queue, Exchange, "#", cancellationToken: ct);

        return channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}

public sealed class RabbitMqEventBus(RabbitMqConnection connection) : IEventBus
{
    public async Task Publish(EventEnvelope envelope, CancellationToken ct)
    {
        await using var channel = await connection.OpenChannel(ct);

        var properties = new BasicProperties
        {
            MessageId = envelope.Id.ToString(),
            Type = envelope.Type,
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
        };

        await channel.BasicPublishAsync(
            RabbitMqConnection.Exchange,
            envelope.Type,
            mandatory: false,
            properties,
            Encoding.UTF8.GetBytes(envelope.Payload),
            ct);
    }
}

public sealed class RabbitMqConsumer(
    RabbitMqConnection connection,
    IntegrationEventDispatcher dispatcher,
    ILogger<RabbitMqConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var channel = await ConnectWithRetry(stoppingToken);
        await channel.BasicQosAsync(0, 10, false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, message) =>
        {
            var envelope = new EventEnvelope(
                Guid.Parse(message.BasicProperties.MessageId!),
                message.BasicProperties.Type!,
                Encoding.UTF8.GetString(message.Body.Span));

            try
            {
                await dispatcher.Dispatch(envelope, stoppingToken);
                await channel.BasicAckAsync(message.DeliveryTag, false, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Event {EventType} {EventId} went to the dead letter queue", envelope.Type, envelope.Id);
                await channel.BasicNackAsync(message.DeliveryTag, false, requeue: false, stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(RabbitMqConnection.Queue, autoAck: false, consumer, stoppingToken);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task<IChannel> ConnectWithRetry(CancellationToken ct)
    {
        while (true)
        {
            try
            {
                return await connection.OpenChannel(ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning("RabbitMQ is not ready yet: {Reason}", exception.Message);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
    }
}
