using RabbitMQ.Client;

namespace PushService.Messaging;

// Topologia exclusiva do PushService.
// IMPORTANTE: x-dead-letter-routing-key deve ser "push" — routing key errada
// faz as mensagens desaparecerem silenciosamente após o TTL.
public static class RabbitMqTopology
{
    public const string NotificationsExchange = "notifications.exchange";
    public const string RetryExchange         = "notifications.push.retry.exchange";
    public const string ServiceQueue          = "notifications.push";
    public const string DlqQueue             = "notifications.push.dlq";
    public const string RoutingKey           = "push";

    // Atrasos do backoff exponencial (ms): 5s → 15s → 45s
    public static readonly int[] RetryDelaysMs = [5_000, 15_000, 45_000];

    public static async Task DeclareAsync(IChannel channel)
    {
        await channel.ExchangeDeclareAsync(
            exchange: NotificationsExchange,
            type: ExchangeType.Direct,
            durable: true
        );

        await channel.ExchangeDeclareAsync(
            exchange: RetryExchange,
            type: ExchangeType.Direct,
            durable: true
        );

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

        for (int i = 0; i < RetryDelaysMs.Length; i++)
        {
            var retryQueue = $"notifications.push.retry.{i + 1}";
            await channel.QueueDeclareAsync(
                queue: retryQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-message-ttl"]             = RetryDelaysMs[i],
                    ["x-dead-letter-exchange"]    = NotificationsExchange,
                    ["x-dead-letter-routing-key"] = RoutingKey   // "push" — crítico!
                }
            );
            await channel.QueueBindAsync(
                queue: retryQueue,
                exchange: RetryExchange,
                routingKey: $"retry.{i + 1}"
            );
        }

        await channel.QueueDeclareAsync(
            queue: DlqQueue,
            durable: true,
            exclusive: false,
            autoDelete: false
        );
    }
}
