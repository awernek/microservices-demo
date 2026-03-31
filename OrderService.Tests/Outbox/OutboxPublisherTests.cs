using Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrderService.Messaging;
using OrderService.Outbox;

namespace OrderService.Tests.Outbox;

public class OutboxPublisherTests
{
    private readonly Mock<IRabbitMqPublisher> _publisherMock = new();
    private readonly OutboxStore _store = new();

    [Fact]
    public async Task Publisher_PublicaMensagem_ERemoveDoStore()
    {
        var message = new OrderCreatedMessage { OrderId = Guid.NewGuid() };
        _store.Enqueue(message);

        _publisherMock
            .Setup(p => p.PublishAsync(It.IsAny<OrderCreatedMessage>()))
            .Returns(Task.CompletedTask);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var publisher = new OutboxPublisher(_store, _publisherMock.Object, NullLogger<OutboxPublisher>.Instance);

        _ = publisher.StartAsync(cts.Token);

        // Aguarda a mensagem ser processada
        await Task.Delay(1_500, CancellationToken.None);

        _publisherMock.Verify(p => p.PublishAsync(It.IsAny<OrderCreatedMessage>()), Times.Once);
        Assert.Equal(0, _store.Count);

        await publisher.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Publisher_QuandoPublishFalha_NaoRemoveMensagemDoStore()
    {
        // Ponto crítico do Outbox: TryDequeue ANTES do publish = risco de perda.
        // O OutboxPublisher faz TryPeek → publish → TryDequeue (só após sucesso).
        // Se publicar falhar, a mensagem permanece no store para retry.
        var message = new OrderCreatedMessage { OrderId = Guid.NewGuid() };
        _store.Enqueue(message);

        _publisherMock
            .Setup(p => p.PublishAsync(It.IsAny<OrderCreatedMessage>()))
            .ThrowsAsync(new Exception("RabbitMQ indisponível"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var publisher = new OutboxPublisher(_store, _publisherMock.Object, NullLogger<OutboxPublisher>.Instance);

        _ = publisher.StartAsync(cts.Token);

        await Task.Delay(2_500, CancellationToken.None);

        // Mensagem deve permanecer no store — não foi descartada após falha
        Assert.Equal(1, _store.Count);

        await publisher.StopAsync(CancellationToken.None);
    }
}
