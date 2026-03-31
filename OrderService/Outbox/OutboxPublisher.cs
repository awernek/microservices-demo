using OrderService.Messaging;

namespace OrderService.Outbox;

// Drena o OutboxStore em direção ao RabbitMQ a cada 1 segundo.
// Lógica crítica: TryPeek antes de publicar — só descarta (TryDequeue) após confirmação
// de sucesso. Se publicar e descartar antes de confirmar, uma falha de rede perde a mensagem.
public class OutboxPublisher : BackgroundService
{
    private readonly OutboxStore _store;
    private readonly IRabbitMqPublisher _publisher;
    private readonly ILogger<OutboxPublisher> _logger;

    public OutboxPublisher(
        OutboxStore store,
        IRabbitMqPublisher publisher,
        ILogger<OutboxPublisher> logger)
    {
        _store     = store;
        _publisher = publisher;
        _logger    = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            // Drena todas as mensagens acumuladas neste ciclo
            while (_store.TryPeek(out var message))
            {
                try
                {
                    await _publisher.PublishAsync(message!);
                    _store.TryDequeue(out _);   // descarta só após confirmação de envio
                    _logger.LogDebug("[Outbox] Mensagem {OrderId} publicada", message!.OrderId);
                }
                catch (Exception ex)
                {
                    // Não descarta — tenta novamente no próximo ciclo
                    _logger.LogWarning("[Outbox] Falha ao publicar {OrderId}: {Error}. Retry em 1s.",
                        message!.OrderId, ex.Message);
                    break;
                }
            }
        }
    }
}
