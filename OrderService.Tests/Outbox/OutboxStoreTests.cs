using Contracts;
using OrderService.Outbox;

namespace OrderService.Tests.Outbox;

public class OutboxStoreTests
{
    [Fact]
    public void Enqueue_AdicionaMensagem_CountAumenta()
    {
        var store = new OutboxStore();
        var message = new OrderCreatedMessage { OrderId = Guid.NewGuid() };

        store.Enqueue(message);

        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void TryPeek_RetornaMensagem_SemRemover()
    {
        var store = new OutboxStore();
        var message = new OrderCreatedMessage { OrderId = Guid.NewGuid() };
        store.Enqueue(message);

        var found = store.TryPeek(out var peeked);

        Assert.True(found);
        Assert.Equal(message.OrderId, peeked!.OrderId);
        Assert.Equal(1, store.Count);  // não removeu
    }

    [Fact]
    public void TryDequeue_RetornaMensagem_ERemove()
    {
        var store = new OutboxStore();
        var message = new OrderCreatedMessage { OrderId = Guid.NewGuid() };
        store.Enqueue(message);

        var found = store.TryDequeue(out var dequeued);

        Assert.True(found);
        Assert.Equal(message.OrderId, dequeued!.OrderId);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void TryPeek_FilaVazia_RetornaFalse()
    {
        var store = new OutboxStore();

        var found = store.TryPeek(out var message);

        Assert.False(found);
        Assert.Null(message);
    }

    [Fact]
    public void Enqueue_MultiplasMensagens_MantemOrdem()
    {
        var store = new OutboxStore();
        var ids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();

        foreach (var id in ids)
            store.Enqueue(new OrderCreatedMessage { OrderId = id });

        foreach (var id in ids)
        {
            store.TryDequeue(out var msg);
            Assert.Equal(id, msg!.OrderId);
        }
    }

    [Fact]
    public void ThreadSafety_EnqueueConcorrente_NaoPerdeEntradas()
    {
        var store = new OutboxStore();
        var threads = 10;
        var messagesPerThread = 100;

        Parallel.For(0, threads, _ =>
        {
            for (int i = 0; i < messagesPerThread; i++)
                store.Enqueue(new OrderCreatedMessage { OrderId = Guid.NewGuid() });
        });

        Assert.Equal(threads * messagesPerThread, store.Count);
    }
}
