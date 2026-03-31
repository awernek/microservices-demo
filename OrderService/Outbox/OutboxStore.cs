using System.Collections.Concurrent;
using Contracts;

namespace OrderService.Outbox;

// Armazena mensagens pendentes de publicação em memória.
// Produção: tabela no mesmo banco de dados da entidade, dentro da mesma transação —
// garante que o pedido e a mensagem são gravados ou descartados juntos (atomicidade).
// Aqui: in-memory para simplicidade. Perde dados se o processo reiniciar.
public class OutboxStore
{
    private readonly ConcurrentQueue<OrderCreatedMessage> _queue = new();

    public void Enqueue(OrderCreatedMessage message) => _queue.Enqueue(message);

    public bool TryPeek(out OrderCreatedMessage? message) => _queue.TryPeek(out message);

    public bool TryDequeue(out OrderCreatedMessage? message) => _queue.TryDequeue(out message);

    public int Count => _queue.Count;
}
