using System.Collections.Concurrent;

namespace NotificationService.Services;

public class MetricsService
{
    private long _totalProcessed;
    private long _totalFailed;
    private long _totalDuplicates;
    private long _totalSentToDlq;
    private long _totalElapsedMs;
    private long _successCount;

    // Contagem por tipo: email, push, sms
    private readonly ConcurrentDictionary<string, long> _successByType = new();
    private readonly ConcurrentDictionary<string, long> _failureByType = new();

    public void RecordSuccess(string type, long elapsedMs)
    {
        Interlocked.Increment(ref _totalProcessed);
        Interlocked.Add(ref _totalElapsedMs, elapsedMs);
        Interlocked.Increment(ref _successCount);
        _successByType.AddOrUpdate(type, 1, (_, v) => Interlocked.Increment(ref v));
    }

    public void RecordFailure(string type)
    {
        Interlocked.Increment(ref _totalFailed);
        _failureByType.AddOrUpdate(type, 1, (_, v) => Interlocked.Increment(ref v));
    }

    public void RecordDuplicate() => Interlocked.Increment(ref _totalDuplicates);

    public void RecordDlq() => Interlocked.Increment(ref _totalSentToDlq);

    public void LogSummary(ILogger logger)
    {
        var avgMs = _successCount > 0 ? _totalElapsedMs / _successCount : 0;

        var successByType = string.Join(" | ", _successByType
            .Select(kv => $"{kv.Key}: {kv.Value}"));

        var failureByType = string.Join(" | ", _failureByType
            .Select(kv => $"{kv.Key}: {kv.Value}"));

        logger.LogInformation(
            "=== MÉTRICAS ===" +
            " Processadas: {Processed}" +
            " | Falhas: {Failed}" +
            " | DLQ: {Dlq}" +
            " | Duplicatas: {Dup}" +
            " | Tempo médio: {Avg}ms" +
            " | Sucesso por tipo: [{ByType}]" +
            " | Falha por tipo: [{FailType}]",
            _totalProcessed,
            _totalFailed,
            _totalSentToDlq,
            _totalDuplicates,
            avgMs,
            successByType,
            failureByType
        );
    }
}
