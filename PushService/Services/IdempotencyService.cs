using System.Collections.Concurrent;

namespace PushService.Services;

public class IdempotencyService
{
    private readonly ConcurrentDictionary<Guid, byte> _processed = new();

    public bool TryMarkProcessed(Guid notificationId) =>
        _processed.TryAdd(notificationId, 0);

    public void Cleanup() => _processed.Clear();
}
