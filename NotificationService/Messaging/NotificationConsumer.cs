using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NotificationService.Models;
using NotificationService.Notifications;
using NotificationService.Services;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NotificationService.Messaging;

public class NotificationConsumer
{
    private readonly IChannel _consumeChannel;
    private readonly IChannel _publishChannel;
    private readonly SemaphoreSlim _publishLock = new(1, 1); // BasicPublish não é thread-safe
    private readonly Dictionary<string, INotificationHandler> _handlers;
    private readonly IdempotencyService _idempotency;
    private readonly MetricsService _metrics;
    private readonly ILogger _logger;

    private const int MaxRetries = 3; // 3 tentativas extras após a inicial = 4 tentativas no total

    public NotificationConsumer(
        IChannel consumeChannel,
        IChannel publishChannel,
        IEnumerable<INotificationHandler> handlers,
        IdempotencyService idempotency,
        MetricsService metrics,
        ILogger logger)
    {
        _consumeChannel = consumeChannel;
        _publishChannel = publishChannel;
        _handlers       = handlers.ToDictionary(h => h.Type);
        _idempotency    = idempotency;
        _metrics        = metrics;
        _logger         = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        // prefetchCount: 5 = até 5 mensagens sendo processadas em paralelo por este consumer
        // Aumentar esse número escala o throughput; diminuir reduz a pressão sobre handlers lentos
        await _consumeChannel.BasicQosAsync(prefetchSize: 0, prefetchCount: 5, global: false, cancellationToken: ct);

        var consumer = new AsyncEventingBasicConsumer(_consumeChannel);
        consumer.ReceivedAsync += HandleNotificationAsync;

        await _consumeChannel.BasicConsumeAsync(
            queue: RabbitMqTopology.NotificationsQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: ct
        );

        _logger.LogInformation("[NotificationConsumer] Aguardando notificações... (prefetch: 5, max retries: {Max})", MaxRetries);
    }

    private async Task HandleNotificationAsync(object _, BasicDeliverEventArgs args)
    {
        var json    = Encoding.UTF8.GetString(args.Body.ToArray());
        var message = JsonSerializer.Deserialize<NotificationMessage>(json)!;
        var sw      = Stopwatch.StartNew();

        // ── Fase 3: Idempotência ──────────────────────────────────────────────
        if (_idempotency.AlreadyProcessed(message.NotificationId))
        {
            _logger.LogWarning(
                "[DUPLICATA] Notificação {Id} ({Type}) já processada. Descartando.",
                message.NotificationId, message.Type
            );
            _metrics.RecordDuplicate();
            await _consumeChannel.BasicAckAsync(args.DeliveryTag, multiple: false);
            return;
        }

        if (!_handlers.TryGetValue(message.Type, out var handler))
        {
            _logger.LogError("[ERRO] Nenhum handler para o tipo '{Type}'. Descartando.", message.Type);
            await _consumeChannel.BasicAckAsync(args.DeliveryTag, multiple: false);
            return;
        }

        try
        {
            await handler.HandleAsync(message);

            sw.Stop();

            // ── Fase 3: Registra como processado ─────────────────────────────
            _idempotency.Register(message.NotificationId);

            // ── Fase 4: Métricas de sucesso ───────────────────────────────────
            _metrics.RecordSuccess(message.Type, sw.ElapsedMilliseconds);

            await _consumeChannel.BasicAckAsync(args.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            sw.Stop();

            // ── Fase 4: Métricas de falha ─────────────────────────────────────
            _metrics.RecordFailure(message.Type);

            // ── Fase 2: Retry com backoff exponencial ─────────────────────────
            if (message.RetryCount < MaxRetries)
            {
                var retryNumber = message.RetryCount + 1;
                var delayMs     = RabbitMqTopology.RetryDelaysMs[message.RetryCount];

                _logger.LogWarning(
                    "[RETRY {Attempt}/{Max}] {Type} falhou: {Error}. Aguardando {Delay}s antes de tentar novamente.",
                    retryNumber, MaxRetries, message.Type, ex.Message, delayMs / 1000
                );

                await PublishToRetryAsync(message with { RetryCount = retryNumber }, retryNumber);
            }
            else
            {
                // ── Fase 2: DLQ — esgotou todas as tentativas ─────────────────
                _logger.LogError(
                    "[DLQ] {Type} falhou após {Max} tentativas. Notificação {Id} enviada para dead letter queue. Último erro: {Error}",
                    message.Type, MaxRetries, message.NotificationId, ex.Message
                );

                _metrics.RecordDlq();
                await PublishToDlqAsync(message, ex.Message);
            }

            // Ack mesmo em falha — o retry/DLQ foi feito via republish explícito,
            // não via nack. Isso dá controle total sobre o fluxo de retry.
            await _consumeChannel.BasicAckAsync(args.DeliveryTag, multiple: false);
        }
    }

    private async Task PublishToRetryAsync(NotificationMessage message, int retryNumber)
    {
        var body  = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        var props = new BasicProperties { Persistent = true };

        await _publishLock.WaitAsync();
        try
        {
            await _publishChannel.BasicPublishAsync(
                exchange: RabbitMqTopology.RetryExchange,
                routingKey: $"retry.{retryNumber}",
                mandatory: false,
                basicProperties: props,
                body: body
            );
        }
        finally { _publishLock.Release(); }
    }

    private async Task PublishToDlqAsync(NotificationMessage message, string errorReason)
    {
        var envelope = new { message, errorReason, failedAt = DateTime.UtcNow };
        var body     = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope));
        var props    = new BasicProperties { Persistent = true };

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
}
