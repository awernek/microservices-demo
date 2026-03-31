using System.Collections.Concurrent;

namespace EmailService.Services;

// Contadores de métricas in-memory. Produção: Prometheus/OpenTelemetry.
// Ponto de discussão em entrevista: Interlocked.Increment(ref v) no AddOrUpdate
// opera numa cópia local de v — funciona mas é uma race condition discutível.
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
