using System.Text;
using System.Text.Json;
using NotificationService.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NotificationService;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly string _rabbitHost;
    private const string QueueName = "orders";

    public Worker(ILogger<Worker> logger, IConfiguration config)
    {
        _logger = logger;
        _rabbitHost = config["RabbitMQ:Host"] ?? "localhost";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Aguarda o RabbitMQ subir (importante no Docker — os containers sobem em paralelo)
        await WaitForRabbitMqAsync(stoppingToken);

        var factory = new ConnectionFactory { HostName = _rabbitHost };
        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // Declara a mesma fila que o OrderService usa
        // Se ela já existe, não faz nada — se não existe, cria
        await channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken
        );

        // Processa uma mensagem por vez (não pega a próxima antes de confirmar a atual)
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);

        consumer.ReceivedAsync += async (_, eventArgs) =>
        {
            try
            {
                var json = Encoding.UTF8.GetString(eventArgs.Body.ToArray());
                var message = JsonSerializer.Deserialize<OrderCreatedMessage>(json);

                if (message is not null)
                {
                    _logger.LogInformation(
                        "Notificação enviada! Pedido {OrderId} do cliente '{CustomerName}' no valor de {TotalAmount:C}",
                        message.OrderId,
                        message.CustomerName,
                        message.TotalAmount
                    );
                }

                // Confirma para o RabbitMQ que a mensagem foi processada com sucesso
                // Só após isso o RabbitMQ remove a mensagem da fila
                await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao processar mensagem");

                // Rejeita a mensagem e coloca ela de volta na fila para tentar de novo
                await channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: true);
            }
        };

        await channel.BasicConsumeAsync(queue: QueueName, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        _logger.LogInformation("NotificationService aguardando mensagens...");

        // Mantém o worker vivo enquanto a aplicação não for cancelada
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task WaitForRabbitMqAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory { HostName = _rabbitHost };

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await factory.CreateConnectionAsync(stoppingToken);
                _logger.LogInformation("Conectado ao RabbitMQ!");
                return;
            }
            catch
            {
                _logger.LogWarning("RabbitMQ não está pronto ainda. Tentando novamente em 3s...");
                await Task.Delay(3000, stoppingToken);
            }
        }
    }
}
