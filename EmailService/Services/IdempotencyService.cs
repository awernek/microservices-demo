using System.Collections.Concurrent;

namespace EmailService.Services;

// Evita processar a mesma notificação mais de uma vez (em caso de retry do broker).
// Produção: Redis com TTL. Aqui: ConcurrentDictionary in-memory.
public class IdempotencyService
{
    private readonly ConcurrentDictionary<Guid, byte> _processed = new();

    public bool TryMarkProcessed(Guid notificationId) =>
        _processed.TryAdd(notificationId, 0);

    // Remove entradas antigas para evitar crescimento ilimitado da memória
    public void Cleanup() => _processed.Clear();
}
