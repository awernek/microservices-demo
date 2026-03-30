using System.Collections.Concurrent;

namespace NotificationService.Services;

// Garante que a mesma notificação não seja processada duas vezes
// Em produção: trocar por Redis para funcionar com múltiplas instâncias
public class IdempotencyService
{
    // Armazena: NotificationId → momento do processamento
    private readonly ConcurrentDictionary<Guid, DateTime> _processed = new();

    public bool AlreadyProcessed(Guid notificationId) =>
        _processed.ContainsKey(notificationId);

    public void Register(Guid notificationId) =>
        _processed.TryAdd(notificationId, DateTime.UtcNow);

    // Remove entradas com mais de 1 hora para evitar crescimento infinito de memória
    public void Cleanup()
    {
        var cutoff = DateTime.UtcNow.AddHours(-1);
        foreach (var key in _processed.Keys)
            if (_processed.TryGetValue(key, out var processedAt) && processedAt < cutoff)
                _processed.TryRemove(key, out _);
    }
}
