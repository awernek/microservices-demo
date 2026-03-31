using System.Collections.Concurrent;

namespace PushService.Services;

public class MetricsService
{
    private long _totalProcessed;
    private long _totalFailed;
    private long _totalDlq;
    private readonly ConcurrentDictionary<string, long> _successByType = new();

    public void RecordSuccess(string type)
    {
        Interlocked.Increment(ref _totalProcessed);
        _successByType.AddOrUpdate(type, 1, (_, v) => Interlocked.Increment(ref v));
    }

    public void RecordFailure() => Interlocked.Increment(ref _totalFailed);
    public void RecordDlq()     => Interlocked.Increment(ref _totalDlq);

    public void LogSummary(ILogger logger)
    {
        logger.LogInformation(
            "[Métricas] Processadas: {Processed} | Falhas: {Failed} | DLQ: {Dlq}",
            _totalProcessed, _totalFailed, _totalDlq
        );
    }
}
