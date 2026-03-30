using System.Text;
using System.Text.Json;
using NotificationService.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NotificationService.Messaging;

// Lê pedidos da fila "orders" e gera uma NotificationMessage por canal (email, push, sms)
public class OrderConsumer
{
    private readonly IChannel _consumeChannel;
    private readonly IChannel _publishChannel;
    private readonly ILogger _logger;

    public OrderConsumer(IChannel consumeChannel, IChannel publishChannel, ILogger logger)
    {
        _consumeChannel = consumeChannel;
        _publishChannel = publishChannel;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        // Processa um pedido por vez — cada pedido gera 3 notificações
        await _consumeChannel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: ct);

        var consumer = new AsyncEventingBasicConsumer(_consumeChannel);
        consumer.ReceivedAsync += HandleOrderAsync;

        await _consumeChannel.BasicConsumeAsync(
            queue: "orders",
            autoAck: false,
            consumer: consumer,
            cancellationToken: ct
        );

        _logger.LogInformation("[OrderConsumer] Aguardando pedidos...");
    }

    private async Task HandleOrderAsync(object _, BasicDeliverEventArgs args)
    {
        var json = Encoding.UTF8.GetString(args.Body.ToArray());
        var order = JsonSerializer.Deserialize<OrderCreatedMessage>(json)!;

        // Um pedido dispara 3 notificações independentes — cada uma tem seu próprio ID e ciclo de retry
        var notifications = new[]
        {
            new NotificationMessage
            {
                OrderId   = order.OrderId,
                Type      = "email",
                Recipient = $"{order.CustomerName.ToLower().Replace(" ", ".")}@example.com",
                Content   = $"Pedido #{order.OrderId} confirmado. Total: {order.TotalAmount:C}"
            },
            new NotificationMessage
            {
                OrderId   = order.OrderId,
                Type      = "push",
                Recipient = $"device-{order.OrderId:N}"[..16],
                Content   = $"Seu pedido de {order.TotalAmount:C} foi recebido!"
            },
            new NotificationMessage
            {
                OrderId   = order.OrderId,
                Type      = "sms",
                Recipient = "+55 11 9 0000-0000",
                Content   = $"Pedido recebido. Total: R$ {order.TotalAmount:F2}"
            }
        };

        foreach (var notification in notifications)
        {
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(notification));
            var props = new BasicProperties { Persistent = true };

            await _publishChannel.BasicPublishAsync(
                exchange: RabbitMqTopology.NotificationsExchange,
                routingKey: "notification",
                mandatory: false,
                basicProperties: props,
                body: body
            );
        }

        _logger.LogInformation(
            "[OrderConsumer] Pedido {OrderId} → {Count} notificações despachadas",
            order.OrderId,
            notifications.Length
        );

        await _consumeChannel.BasicAckAsync(args.DeliveryTag, multiple: false);
    }
}
