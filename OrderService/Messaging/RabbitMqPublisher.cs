using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace OrderService.Messaging;

// Implementa IAsyncDisposable para fechar a conexão corretamente quando o serviço parar
public class RabbitMqPublisher : IRabbitMqPublisher, IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private const string QueueName = "orders";

    // Construtor privado — use o método estático CreateAsync para instanciar
    private RabbitMqPublisher(IConnection connection, IChannel channel)
    {
        _connection = connection;
        _channel = channel;
    }

    public static async Task<RabbitMqPublisher> CreateAsync(string host, int port = 5672)
    {
        var factory = new ConnectionFactory { HostName = host, Port = port };
        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();

        // Declara a fila. "durable: true" significa que ela sobrevive a um restart do RabbitMQ
        await channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false
        );

        return new RabbitMqPublisher(connection, channel);
    }

    public async Task PublishAsync<T>(T message)
    {
        // Serializa o objeto para JSON e converte para bytes
        var json = JsonSerializer.Serialize(message);
        var body = Encoding.UTF8.GetBytes(json);

        var props = new BasicProperties
        {
            // "Persistent" garante que a mensagem não seja perdida se o RabbitMQ reiniciar
            Persistent = true
        };

        await _channel.BasicPublishAsync(
            exchange: "",        // Exchange padrão — entrega direto na fila pelo nome
            routingKey: QueueName,
            mandatory: false,
            basicProperties: props,
            body: body
        );
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
