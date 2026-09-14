using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FaconControlPlane.DataPlane.Contracts.Usage;

public interface IFaconLimitClient
{
    Task<FaconLimitEvaluation> EvaluateLimitAsync(string limitKey, decimal delta = 0m, CancellationToken ct = default);
    Task<bool> CanConsumeAsync(string limitKey, decimal delta = 1m, CancellationToken ct = default);
}

public sealed class FaconLimitClient : IFaconLimitClient
{
    private readonly HttpClient _httpClient;
    private readonly FaconEnforcementOptions _options;
    private readonly ILogger<FaconLimitClient> _logger;
    private readonly ConcurrentDictionary<string, (FaconLimitEvaluation Evaluation, DateTimeOffset CachedAt)> _cache = new();

    public FaconLimitClient(
        HttpClient httpClient,
        IOptions<FaconEnforcementOptions> options,
        ILogger<FaconLimitClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<FaconLimitEvaluation> EvaluateLimitAsync(string limitKey, decimal delta = 0m, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(limitKey);
        var key = limitKey.Trim().ToLowerInvariant();
        var cacheKey = $"{key}:delta={delta}";

        // 1. Check fresh cache first (TMP-003)
        if (_cache.TryGetValue(cacheKey, out var cached) &&
            DateTimeOffset.UtcNow - cached.CachedAt < _options.OfflineCacheTtl)
        {
            _logger.LogInformation("Using cached limit evaluation for '{LimitKey}' (Age: {Age}s).",
                key, (DateTimeOffset.UtcNow - cached.CachedAt).TotalSeconds);
            return cached.Evaluation;
        }

        try
        {
            var url = $"{_options.ControlPlaneUrl.TrimEnd('/')}/api/v1/limits/{Uri.EscapeDataString(key)}/evaluate?delta={delta}";
            var response = await _httpClient.GetAsync(url, ct);

            if (response.IsSuccessStatusCode)
            {
                var eval = await response.Content.ReadFromJsonAsync<FaconLimitEvaluation>(cancellationToken: ct);
                if (eval != null)
                {
                    _cache[cacheKey] = (eval, DateTimeOffset.UtcNow);
                    return eval;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query Control Plane for limit '{LimitKey}'. Falling back to offline policy.", key);
        }

        // 2. Offline fallback (TMP-003): use expired cache if available
        if (_cache.TryGetValue(cacheKey, out var staleCached))
        {
            _logger.LogInformation("Using stale cached limit evaluation for '{LimitKey}'.", key);
            return staleCached.Evaluation;
        }

        if (_options.FailClosedForHardLimits)
        {
            return new FaconLimitEvaluation
            {
                Decision = "Deny",
                Limit = 0m,
                Current = 0m,
                Projected = delta,
                Remaining = 0m,
                Reason = "Control Plane unavailable and offline fail-closed policy is active (TMP-003)."
            };
        }

        return new FaconLimitEvaluation
        {
            Decision = "AllowWithWarning",
            Limit = decimal.MaxValue,
            Current = 0m,
            Projected = delta,
            Remaining = decimal.MaxValue,
            Reason = "Control Plane unavailable; failing open per policy (TMP-003)."
        };
    }

    public async Task<bool> CanConsumeAsync(string limitKey, decimal delta = 1m, CancellationToken ct = default)
    {
        var eval = await EvaluateLimitAsync(limitKey, delta, ct);
        if (!eval.IsAllowed)
        {
            throw new FaconLimitExceededException(limitKey, eval.Limit, eval.Current, eval.Projected, eval.Reason);
        }
        return true;
    }
}
