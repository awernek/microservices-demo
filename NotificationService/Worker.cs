using NotificationService.Messaging;
using RabbitMQ.Client;

namespace NotificationService;

public class Worker : BackgroundService
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _rabbitHost;

    public Worker(ILoggerFactory loggerFactory, IConfiguration config)
    {
        _loggerFactory = loggerFactory;
        _rabbitHost    = config["RabbitMQ:Host"] ?? "localhost";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var logger = _loggerFactory.CreateLogger<Worker>();

        await WaitForRabbitMqAsync(logger, stoppingToken);

        var factory = new ConnectionFactory { HostName = _rabbitHost };
        await using var connection = await factory.CreateConnectionAsync(stoppingToken);

        // Dois canais: um para consumir pedidos, outro para publicar notificações
        await using var setupChannel        = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await using var orderConsumeChannel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await using var orderPublishChannel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // Declara topologia mínima: notifications.exchange + fila "orders"
        await RabbitMqTopology.DeclareAsync(setupChannel);
        logger.LogInformation("Topologia RabbitMQ declarada com sucesso.");

        // Consumer de pedidos — transforma 1 pedido em 3 notificações roteadas por tipo
        var orderConsumer = new OrderConsumer(
            orderConsumeChannel,
            orderPublishChannel,
            _loggerFactory.CreateLogger<OrderConsumer>()
        );
        await orderConsumer.StartAsync(stoppingToken);

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
