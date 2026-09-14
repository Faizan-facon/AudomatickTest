using FaconControlPlane.DataPlane.Contracts.Usage;

namespace Audomatick.Desktop.Services;

public sealed record DesktopOperationResult(
    bool Success,
    string Message,
    FaconLimitEvaluation? Evaluation,
    string? ReportContent = null
);

public sealed class DesktopMeteringFacade
{
    private readonly IFaconLimitClient _limitClient;
    private readonly IFaconUsageReporter _usageReporter;
    private readonly IOfflineLimitCache _offlineCache;
    private readonly IDurableUsageQueue _durableQueue;

    public DesktopMeteringFacade(
        IFaconLimitClient limitClient,
        IFaconUsageReporter usageReporter,
        IOfflineLimitCache offlineCache,
        IDurableUsageQueue durableQueue)
    {
        _limitClient = limitClient ?? throw new ArgumentNullException(nameof(limitClient));
        _usageReporter = usageReporter ?? throw new ArgumentNullException(nameof(usageReporter));
        _offlineCache = offlineCache ?? throw new ArgumentNullException(nameof(offlineCache));
        _durableQueue = durableQueue ?? throw new ArgumentNullException(nameof(durableQueue));
    }

    public async Task<FaconLimitEvaluation> CheckCapacityAsync(string limitKey, CancellationToken ct = default)
    {
        // 1. Try remote limit check
        try
        {
            var eval = await _limitClient.EvaluateLimitAsync(limitKey, 0m, ct);
            _offlineCache.Store(limitKey, eval);
            return eval;
        }
        catch
        {
            // 2. Fall back to offline cache
            if (_offlineCache.TryGet(limitKey, out var cached) && cached != null)
            {
                return cached;
            }

            // 3. Graceful offline fallback
            return new FaconLimitEvaluation
            {
                Decision = "AllowWithWarning",
                Limit = 100m,
                Current = 0m,
                Projected = 0m,
                Remaining = 100m,
                Reason = "Offline mode: capacity check degraded gracefully to local fallback."
            };
        }
    }

    public async Task<DesktopOperationResult> GenerateReportAsync(Guid tenantId, string reportType = "ExecutiveSummary", CancellationToken ct = default)
    {
        const string limitKey = "reports.generated.daily";

        // 1. Evaluate Limit Pre-Check
        FaconLimitEvaluation evaluation;
        try
        {
            evaluation = await _limitClient.EvaluateLimitAsync(limitKey, 1m, ct);
            _offlineCache.Store(limitKey, evaluation);
        }
        catch
        {
            if (!_offlineCache.TryGet(limitKey, out var cached) || cached == null)
            {
                // Uncached offline fallback: soft allow
                evaluation = new FaconLimitEvaluation
                {
                    Decision = "AllowWithWarning",
                    Limit = 100m,
                    Current = 0m,
                    Projected = 1m,
                    Remaining = 99m,
                    Reason = "Offline fallback evaluation."
                };
            }
            else
            {
                evaluation = cached;
            }
        }

        // 2. Enforce Decision
        if (evaluation.Decision.Equals("Deny", StringComparison.OrdinalIgnoreCase))
        {
            return new DesktopOperationResult(
                Success: false,
                Message: $"Operation blocked by FACON limit policy: {evaluation.Reason ?? "Daily report quota exceeded."}",
                Evaluation: evaluation
            );
        }

        // 3. Perform the actual work
        var reportContent = $"FACON Desktop Report [{reportType}] generated at {DateTimeOffset.UtcNow:u} for Tenant {tenantId}";

        // 4. Report Usage Measurement
        var measurement = new FaconUsageMeasurement
        {
            UsageKey = limitKey,
            Quantity = 1.0m,
            OccurredAt = DateTimeOffset.UtcNow,
            SourceEventId = $"desktop-rep-{Guid.NewGuid()}",
            Dimensions = new Dictionary<string, string>
            {
                ["tenantId"] = tenantId.ToString(),
                ["reportType"] = reportType,
                ["clientType"] = "desktop"
            }
        };

        var reported = false;
        try
        {
            await _usageReporter.ReportUsageAsync(measurement, ct);
            reported = true;
        }
        catch
        {
            reported = false;
        }

        if (!reported)
        {
            // Offline or network failure: enqueue into durable queue for resilient retry
            _durableQueue.Enqueue(measurement);
        }

        var warning = evaluation.Decision.Equals("AllowWithWarning", StringComparison.OrdinalIgnoreCase)
            ? $" (Warning: {evaluation.Reason ?? "Approaching quota limit"})"
            : string.Empty;

        return new DesktopOperationResult(
            Success: true,
            Message: $"Report generated successfully{warning}. {(reported ? "Usage reported to control plane." : "Usage queued locally for offline retry.")}",
            Evaluation: evaluation,
            ReportContent: reportContent
        );
    }

    public async Task<int> FlushOfflineQueueAsync(CancellationToken ct = default)
    {
        return await _durableQueue.FlushAsync(_usageReporter, ct);
    }
}
