using System.Collections.Concurrent;
using System.Net.Http.Json;
using FaconControlPlane.DataPlane.Contracts.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FaconControlPlane.DataPlane.Contracts.Usage;

public interface IFaconUsageReporter
{
    Task ReportUsageAsync(FaconUsageMeasurement measurement, CancellationToken ct = default);
    Task ReportBatchAsync(IEnumerable<FaconUsageMeasurement> measurements, CancellationToken ct = default);
    int PendingQueueCount { get; }
    Task FlushQueueAsync(CancellationToken ct = default);
}

public sealed class FaconUsageReporter : IFaconUsageReporter, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly FaconEnforcementOptions _options;
    private readonly ILogger<FaconUsageReporter> _logger;
    private readonly ConcurrentQueue<FaconUsageMeasurement> _queue = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _drainTask;

    public int PendingQueueCount => _queue.Count;

    public FaconUsageReporter(
        HttpClient httpClient,
        IOptions<FaconEnforcementOptions> options,
        ILogger<FaconUsageReporter> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _drainTask = Task.Run(DrainQueueLoopAsync);
    }

    public async Task ReportUsageAsync(FaconUsageMeasurement measurement, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(measurement);

        // Invariant USG-010: Usage payloads MUST NOT contain secrets
        ZeroSecretValidator.AssertNoSecrets(measurement);

        try
        {
            var url = $"{_options.ControlPlaneUrl.TrimEnd('/')}/api/v1/usage";
            var response = await _httpClient.PostAsJsonAsync(url, measurement, ct);

            if (response.IsSuccessStatusCode)
            {
                return;
            }

            _logger.LogWarning("Control Plane returned status {StatusCode} when reporting usage. Enqueuing for retry.", response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to submit usage measurement for '{UsageKey}'. Enqueuing for retry.", measurement.UsageKey);
        }

        // Enqueue for idempotent retry (USG-003, DESK-011)
        if (_queue.Count < _options.MaxQueueSize)
        {
            _queue.Enqueue(measurement);
        }
        else
        {
            _logger.LogError("Usage queue reached maximum capacity ({Max}). Dropping measurement to prevent memory leak.", _options.MaxQueueSize);
        }
    }

    public async Task ReportBatchAsync(IEnumerable<FaconUsageMeasurement> measurements, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(measurements);
        foreach (var m in measurements)
        {
            await ReportUsageAsync(m, ct);
        }
    }

    public async Task FlushQueueAsync(CancellationToken ct = default)
    {
        while (_queue.TryDequeue(out var measurement))
        {
            try
            {
                var url = $"{_options.ControlPlaneUrl.TrimEnd('/')}/api/v1/usage";
                var response = await _httpClient.PostAsJsonAsync(url, measurement, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _queue.Enqueue(measurement);
                    break;
                }
            }
            catch
            {
                _queue.Enqueue(measurement);
                break;
            }
        }
    }

    private async Task DrainQueueLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.RetryInterval, _cts.Token);
                if (!_queue.IsEmpty)
                {
                    await FlushQueueAsync(_cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error while draining usage queue.");
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
