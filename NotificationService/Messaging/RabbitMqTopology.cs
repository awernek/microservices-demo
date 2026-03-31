using RabbitMQ.Client;

namespace NotificationService.Messaging;

// NotificationService é um dispatcher puro: lê "orders" e publica em notifications.exchange
// com routing key igual ao tipo da notificação ("email", "push", "sms").
// Cada serviço consumidor (EmailService, PushService, SmsService) declara suas próprias
// filas, retry queues e DLQ com o routing key correto.
public static class RabbitMqTopology
{
    // Exchange compartilhado — declarado idempotentemente por cada serviço consumidor também
    public const string NotificationsExchange = "notifications.exchange";

    public static async Task DeclareAsync(IChannel channel)
    {
        // Exchange principal — routing key é o tipo da notificação: "email", "push" ou "sms"
        await channel.ExchangeDeclareAsync(
            exchange: NotificationsExchange,
            type: ExchangeType.Direct,
            durable: true
        );

        // Fila de pedidos — declarada aqui para garantir existência mesmo que OrderService não tenha subido
        await channel.QueueDeclareAsync(
            queue: "orders",
            durable: true,
            exclusive: false,
            autoDelete: false
        );
    }
}
