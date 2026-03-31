using System.Text;
using System.Text.Json;
using Contracts;
using EmailService.Services;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace EmailService.Messaging;

// Consome notificações do tipo "email" com retry exponencial, DLQ e idempotência.
// SemaphoreSlim(1,1): BasicPublish não é thread-safe; com prefetchCount > 1 poderíamos
// ter múltiplos handlers simultâneos tentando publicar no mesmo canal.
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

        _logger.LogInformation("[EmailService] Aguardando notificações...");
    }

    private async Task HandleNotificationAsync(object _, BasicDeliverEventArgs args)
    {
        var json = Encoding.UTF8.GetString(args.Body.ToArray());
        var notification = JsonSerializer.Deserialize<NotificationMessage>(json)!;

        // Idempotência: descarta duplicatas que chegam por retry do broker
        if (!_idempotency.TryMarkProcessed(notification.NotificationId))
        {
            _logger.LogWarning("[EmailService] Duplicata ignorada: {Id}", notification.NotificationId);
            await _consumeChannel.BasicAckAsync(args.DeliveryTag, multiple: false);
            return;
        }

        // Simula envio de email: 30% de falha, latência 50-150ms
        var success = await SimulateEmailAsync(notification);

        if (success)
        {
            _metrics.RecordSuccess("email");
            _logger.LogInformation(
                "[EmailService] Enviado para {Recipient} (pedido {OrderId})",
                notification.Recipient, notification.OrderId
            );
            await _consumeChannel.BasicAckAsync(args.DeliveryTag, multiple: false);
            return;
        }

        // Falha: decide entre retry ou DLQ
        var nextRetry = notification.RetryCount + 1;

        if (nextRetry <= RabbitMqTopology.RetryDelaysMs.Length)
        {
            _logger.LogWarning("[EmailService] [RETRY {Retry}/3] {Id}", nextRetry, notification.NotificationId);
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
            _logger.LogError("[EmailService] [DLQ] Esgotadas tentativas: {Id}", notification.NotificationId);
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

    private static async Task<bool> SimulateEmailAsync(NotificationMessage notification)
    {
        await Task.Delay(Random.Shared.Next(50, 150));
        // 30% de falha simulada (ex: servidor SMTP indisponível)
        return Random.Shared.NextDouble() >= 0.30;
    }
}
