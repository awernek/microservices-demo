using RabbitMQ.Client;

namespace EmailService.Messaging;

// Topologia exclusiva do EmailService.
// IMPORTANTE: x-dead-letter-routing-key deve ser "email" (não "notification") —
// routing key errada faz as mensagens desaparecerem silenciosamente após o TTL.
public static class RabbitMqTopology
{
    public const string NotificationsExchange = "notifications.exchange";
    public const string RetryExchange         = "notifications.email.retry.exchange";
    public const string ServiceQueue          = "notifications.email";
    public const string DlqQueue             = "notifications.email.dlq";
    public const string RoutingKey           = "email";

    // Atrasos do backoff exponencial (ms): 5s → 15s → 45s
    public static readonly int[] RetryDelaysMs = [5_000, 15_000, 45_000];

    public static async Task DeclareAsync(IChannel channel)
    {
        // Exchange compartilhado — declarado idempotentemente (também declarado pelo NotificationService)
        await channel.ExchangeDeclareAsync(
            exchange: NotificationsExchange,
            type: ExchangeType.Direct,
            durable: true
        );

        // Exchange de retry exclusivo deste serviço
        await channel.ExchangeDeclareAsync(
            exchange: RetryExchange,
            type: ExchangeType.Direct,
            durable: true
        );

        // Fila principal — binding com routing key "email"
        await channel.QueueDeclareAsync(
            queue: ServiceQueue,
            durable: true,
            exclusive: false,
            autoDelete: false
        );
        await channel.QueueBindAsync(
            queue: ServiceQueue,
            exchange: NotificationsExchange,
            routingKey: RoutingKey
        );

        // Filas de retry com TTL crescente — ao expirar, volta para NotificationsExchange
        // com routing key "email", retornando à fila principal deste serviço
        for (int i = 0; i < RetryDelaysMs.Length; i++)
        {
            var retryQueue = $"notifications.email.retry.{i + 1}";
            await channel.QueueDeclareAsync(
                queue: retryQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-message-ttl"]             = RetryDelaysMs[i],
                    ["x-dead-letter-exchange"]    = NotificationsExchange,
                    ["x-dead-letter-routing-key"] = RoutingKey   // "email" — crítico!
                }
            );
            await channel.QueueBindAsync(
                queue: retryQueue,
                exchange: RetryExchange,
                routingKey: $"retry.{i + 1}"
            );
        }

        // Dead Letter Queue — destino final após esgotar todas as tentativas
        await channel.QueueDeclareAsync(
            queue: DlqQueue,
            durable: true,
            exclusive: false,
            autoDelete: false
        );
    }
}
