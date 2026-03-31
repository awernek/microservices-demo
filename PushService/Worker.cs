using PushService.Messaging;
using PushService.Services;
using RabbitMQ.Client;

namespace PushService;

public class Worker : BackgroundService
{
    private readonly IdempotencyService _idempotency;
    private readonly MetricsService _metrics;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _rabbitHost;

    public Worker(
        IdempotencyService idempotency,
        MetricsService metrics,
        ILoggerFactory loggerFactory,
        IConfiguration config)
    {
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

        await using var setupChannel   = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await using var consumeChannel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await using var publishChannel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await RabbitMqTopology.DeclareAsync(setupChannel);
        logger.LogInformation("[PushService] Topologia declarada.");

        var consumer = new NotificationConsumer(
            consumeChannel,
            publishChannel,
            _idempotency,
            _metrics,
            _loggerFactory.CreateLogger<NotificationConsumer>()
        );
        await consumer.StartAsync(stoppingToken);

        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                _metrics.LogSummary(logger);
                _idempotency.Cleanup();
            }
        }, stoppingToken);

        logger.LogInformation("[PushService] Pronto. Aguardando notificações de push...");
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
                logger.LogInformation("[PushService] Conectado ao RabbitMQ!");
                return;
            }
            catch
            {
                logger.LogWarning("[PushService] RabbitMQ não está pronto. Tentando novamente em 3s...");
                await Task.Delay(3_000, ct);
            }
        }
    }
}
