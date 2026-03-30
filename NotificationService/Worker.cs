using NotificationService.Messaging;
using NotificationService.Notifications;
using NotificationService.Services;
using RabbitMQ.Client;

namespace NotificationService;

public class Worker : BackgroundService
{
    private readonly IEnumerable<INotificationHandler> _handlers;
    private readonly IdempotencyService _idempotency;
    private readonly MetricsService _metrics;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _rabbitHost;

    public Worker(
        IEnumerable<INotificationHandler> handlers,
        IdempotencyService idempotency,
        MetricsService metrics,
        ILoggerFactory loggerFactory,
        IConfiguration config)
    {
        _handlers      = handlers;
        _idempotency   = idempotency;
        _metrics       = metrics;
        _loggerFactory = loggerFactory;
        _rabbitHost    = config["RabbitMQ:Host"] ?? "localhost";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var logger = _loggerFactory.CreateLogger<Worker>();

        await WaitForRabbitMqAsync(logger, stoppingToken);

        var factory = new ConnectionFactory { HostName = _rabbitHost };
        await using var connection = await factory.CreateConnectionAsync(stoppingToken);

        // Cada canal tem uma função específica — separar evita interferência entre operações
        await using var setupChannel        = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await using var orderConsumeChannel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await using var orderPublishChannel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await using var notifConsumeChannel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await using var notifPublishChannel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // Declara toda a topologia (idempotente — seguro chamar múltiplas vezes)
        await RabbitMqTopology.DeclareAsync(setupChannel);
        logger.LogInformation("Topologia RabbitMQ declarada com sucesso.");

        // Consumer de pedidos — transforma 1 pedido em 3 notificações (email, push, sms)
        var orderConsumer = new OrderConsumer(
            orderConsumeChannel,
            orderPublishChannel,
            _loggerFactory.CreateLogger<OrderConsumer>()
        );
        await orderConsumer.StartAsync(stoppingToken);

        // Consumer de notificações — aplica retry com backoff, DLQ e idempotência
        var notifConsumer = new NotificationConsumer(
            notifConsumeChannel,
            notifPublishChannel,
            _handlers,
            _idempotency,
            _metrics,
            _loggerFactory.CreateLogger<NotificationConsumer>()
        );
        await notifConsumer.StartAsync(stoppingToken);

        // Fase 4: log de métricas a cada 30 segundos
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                _metrics.LogSummary(logger);
                _idempotency.Cleanup();
            }
        }, stoppingToken);

        logger.LogInformation("NotificationService pronto. Aguardando mensagens...");
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task WaitForRabbitMqAsync(ILogger logger, CancellationToken ct)
    {
        var factory = new ConnectionFactory { HostName = _rabbitHost };

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var connection = await factory.CreateConnectionAsync(ct);
                logger.LogInformation("Conectado ao RabbitMQ!");
                return;
            }
            catch
            {
                logger.LogWarning("RabbitMQ não está pronto. Tentando novamente em 3s...");
                await Task.Delay(3_000, ct);
            }
        }
    }
}
