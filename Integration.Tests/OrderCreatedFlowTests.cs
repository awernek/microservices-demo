using System.Text;
using System.Text.Json;
using OrderService.Messaging;
using Contracts;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Testcontainers.RabbitMq;

namespace Integration.Tests;

// IAsyncLifetime: xUnit chama InitializeAsync antes dos testes e DisposeAsync depois
public class OrderCreatedFlowTests : IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbitMqContainer;
    private const string QueueName = "orders";

    public OrderCreatedFlowTests()
    {
        // Configura o container — mesma imagem que usamos no docker-compose
        _rabbitMqContainer = new RabbitMqBuilder("rabbitmq:3-management")
            .WithUsername("guest")
            .WithPassword("guest")
            .Build();
    }

    // Sobe o container antes de cada teste
    public async Task InitializeAsync() =>
        await _rabbitMqContainer.StartAsync();

    // Derruba o container após todos os testes da classe
    public async Task DisposeAsync() =>
        await _rabbitMqContainer.DisposeAsync();

    [Fact]
    public async Task Publicar_UmaMensagem_DeveChegar_NaFila()
    {
        // Arrange
        var host = _rabbitMqContainer.Hostname;
        var port = _rabbitMqContainer.GetMappedPublicPort(5672);

        var publisher = await RabbitMqPublisher.CreateAsync(host, port);

        var mensagem = new OrderCreatedMessage
        {
            OrderId = Guid.NewGuid(),
            CustomerName = "Teste Integração",
            TotalAmount = 100m,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await publisher.PublishAsync(mensagem);

        // Assert — conecta um consumer e verifica que a mensagem chegou
        var factory = new ConnectionFactory { HostName = host, Port = port };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false);

        var tcs = new TaskCompletionSource<OrderCreatedMessage>();
        var consumer = new AsyncEventingBasicConsumer(channel);

        consumer.ReceivedAsync += (_, args) =>
        {
            var json = Encoding.UTF8.GetString(args.Body.ToArray());
            var received = JsonSerializer.Deserialize<OrderCreatedMessage>(json)!;
            tcs.SetResult(received);
            return Task.CompletedTask;
        };

        await channel.BasicConsumeAsync(QueueName, autoAck: true, consumer: consumer);

        // Aguarda até 5 segundos pela mensagem
        var mensagemRecebida = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(mensagem.OrderId, mensagemRecebida.OrderId);
        Assert.Equal(mensagem.CustomerName, mensagemRecebida.CustomerName);
        Assert.Equal(mensagem.TotalAmount, mensagemRecebida.TotalAmount);

        await publisher.DisposeAsync();
    }

    [Fact]
    public async Task Publicar_MultiplasMensagens_TodasDevemChegar_NaFila()
    {
        // Arrange
        var host = _rabbitMqContainer.Hostname;
        var port = _rabbitMqContainer.GetMappedPublicPort(5672);

        var publisher = await RabbitMqPublisher.CreateAsync(host, port);
        var mensagens = Enumerable.Range(1, 3).Select(i => new OrderCreatedMessage
        {
            OrderId = Guid.NewGuid(),
            CustomerName = $"Cliente {i}",
            TotalAmount = i * 10m,
            CreatedAt = DateTime.UtcNow
        }).ToList();

        // Act
        foreach (var msg in mensagens)
            await publisher.PublishAsync(msg);

        // Assert
        var factory = new ConnectionFactory { HostName = host, Port = port };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false);

        var recebidas = new List<OrderCreatedMessage>();
        var tcs = new TaskCompletionSource();
        var consumer = new AsyncEventingBasicConsumer(channel);

        consumer.ReceivedAsync += (_, args) =>
        {
            var json = Encoding.UTF8.GetString(args.Body.ToArray());
            recebidas.Add(JsonSerializer.Deserialize<OrderCreatedMessage>(json)!);
            if (recebidas.Count == mensagens.Count) tcs.SetResult();
            return Task.CompletedTask;
        };

        await channel.BasicConsumeAsync(QueueName, autoAck: true, consumer: consumer);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, recebidas.Count);
        Assert.All(mensagens, m => Assert.Contains(recebidas, r => r.OrderId == m.OrderId));

        await publisher.DisposeAsync();
    }
}
