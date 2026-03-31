using System.Text;
using System.Text.Json;
using Contracts;
using PushService.Services;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PushService.Messaging;

// Consome notificações do tipo "push" com retry exponencial, DLQ e idempotência.
// 20% de falha simulada (token de dispositivo expirado).
public class NotificationConsumer
{
    private readonly IChannel _consumeChannel;
    private readonly IChannel _publishChannel;
    private readonly IdempotencyService _idempotency;
    private readonly MetricsService _metrics;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _publishLock = new(1, 1);

    public NotificationConsumer(
        IChannel consumeChannel,
        IChannel publishChannel,
        IdempotencyService idempotency,
        MetricsService metrics,
        ILogger logger)
    {
        _consumeChannel = consumeChannel;
        _publishChannel = publishChannel;
        _idempotency    = idempotency;
        _metrics        = metrics;
        _logger         = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await _consumeChannel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: ct);

        var consumer = new AsyncEventingBasicConsumer(_consumeChannel);
        consumer.ReceivedAsync += HandleNotificationAsync;

        await _consumeChannel.BasicConsumeAsync(
            queue: RabbitMqTopology.ServiceQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: ct
        );

        _logger.LogInformation("[PushService] Aguardando notificações...");
    }

    private async Task HandleNotificationAsync(object _, BasicDeliverEventArgs args)
    {
        var json = Encoding.UTF8.GetString(args.Body.ToArray());
        var notification = JsonSerializer.Deserialize<NotificationMessage>(json)!;

        if (!_idempotency.TryMarkProcessed(notification.NotificationId))
        {
            _logger.LogWarning("[PushService] Duplicata ignorada: {Id}", notification.NotificationId);
            await _consumeChannel.BasicAckAsync(args.DeliveryTag, multiple: false);
            return;
        }

        var success = await SimulatePushAsync(notification);

        if (success)
        {
            _metrics.RecordSuccess("push");
            _logger.LogInformation(
                "[PushService] Enviado para {Recipient} (pedido {OrderId})",
                notification.Recipient, notification.OrderId
            );
            await _consumeChannel.BasicAckAsync(args.DeliveryTag, multiple: false);
            return;
        }

        var nextRetry = notification.RetryCount + 1;

        if (nextRetry <= RabbitMqTopology.RetryDelaysMs.Length)
        {
            _logger.LogWarning("[PushService] [RETRY {Retry}/3] {Id}", nextRetry, notification.NotificationId);
            _metrics.RecordFailure();

            var retryMsg = notification with { RetryCount = nextRetry };
            var body     = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(retryMsg));
            var props    = new BasicProperties { Persistent = true };

            await _publishLock.WaitAsync();
            try
            {
                await _publishChannel.BasicPublishAsync(
                    exchange: RabbitMqTopology.RetryExchange,
                    routingKey: $"retry.{nextRetry}",
                    mandatory: false,
                    basicProperties: props,
                    body: body
                );
            }
            finally { _publishLock.Release(); }
        }
        else
        {
            _logger.LogError("[PushService] [DLQ] Esgotadas tentativas: {Id}", notification.NotificationId);
            _metrics.RecordDlq();

            var body  = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(notification));
            var props = new BasicProperties { Persistent = true };

            await _publishLock.WaitAsync();
            try
            {
                await _publishChannel.BasicPublishAsync(
                    exchange: "",
                    routingKey: RabbitMqTopology.DlqQueue,
                    mandatory: false,
                    basicProperties: props,
                    body: body
                );
            }
            finally { _publishLock.Release(); }
        }

        await _consumeChannel.BasicAckAsync(args.DeliveryTag, multiple: false);
    }

    private static async Task<bool> SimulatePushAsync(NotificationMessage notification)
    {
        await Task.Delay(Random.Shared.Next(20, 80));
        // 20% de falha simulada (ex: token de dispositivo expirado)
        return Random.Shared.NextDouble() >= 0.20;
    }
}
