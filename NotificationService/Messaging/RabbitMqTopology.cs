using RabbitMQ.Client;

namespace NotificationService.Messaging;

// Centraliza toda a topologia RabbitMQ do NotificationService
// Todas as declarações são idempotentes — podem ser chamadas várias vezes com segurança
public static class RabbitMqTopology
{
    // Exchanges
    public const string NotificationsExchange = "notifications.exchange";
    public const string RetryExchange         = "notifications.retry.exchange";

    // Filas
    public const string NotificationsQueue = "notifications";
    public const string DlqQueue           = "notifications.dlq";

    // Atrasos do backoff exponencial (ms): 5s → 15s → 45s
    public static readonly int[] RetryDelaysMs = [5_000, 15_000, 45_000];

    public static async Task DeclareAsync(IChannel channel)
    {
        // Exchange principal — recebe notificações novas e retornos do retry
        await channel.ExchangeDeclareAsync(
            exchange: NotificationsExchange,
            type: ExchangeType.Direct,
            durable: true
        );

        // Exchange de retry — recebe mensagens falhas e as direciona para a fila de espera certa
        await channel.ExchangeDeclareAsync(
            exchange: RetryExchange,
            type: ExchangeType.Direct,
            durable: true
        );

        // Fila principal de notificações
        await channel.QueueDeclareAsync(
            queue: NotificationsQueue,
            durable: true,
            exclusive: false,
            autoDelete: false
        );
        await channel.QueueBindAsync(
            queue: NotificationsQueue,
            exchange: NotificationsExchange,
            routingKey: "notification"
        );

        // Filas de retry com TTL crescente (backoff exponencial)
        // Quando o TTL expira, a mensagem volta para o NotificationsExchange → fila principal
        for (int i = 0; i < RetryDelaysMs.Length; i++)
        {
            var retryQueue = $"notifications.retry.{i + 1}";
            await channel.QueueDeclareAsync(
                queue: retryQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-message-ttl"]             = RetryDelaysMs[i],
                    ["x-dead-letter-exchange"]    = NotificationsExchange,
                    ["x-dead-letter-routing-key"] = "notification"
                }
            );
            await channel.QueueBindAsync(
                queue: retryQueue,
                exchange: RetryExchange,
                routingKey: $"retry.{i + 1}"
            );
        }

        // Dead Letter Queue — destino final de mensagens que esgotaram todas as tentativas
        await channel.QueueDeclareAsync(
            queue: DlqQueue,
            durable: true,
            exclusive: false,
            autoDelete: false
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
