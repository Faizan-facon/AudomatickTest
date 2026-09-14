using System.Collections.Concurrent;
using System.Text.Json;
using FaconControlPlane.DataPlane.Contracts.Usage;

namespace Audomatick.Desktop.Services;

public interface IOfflineLimitCache
{
    void Store(string limitKey, FaconLimitEvaluation evaluation, TimeSpan? ttl = null);
    bool TryGet(string limitKey, out FaconLimitEvaluation? evaluation);
    void Invalidate(string? limitKey = null);
    void SaveToDisk();
    void LoadFromDisk();
}

public sealed class OfflineLimitCache : IOfflineLimitCache
{
    private sealed class CacheEntry
    {
        public FaconLimitEvaluation Evaluation { get; set; } = new();
        public DateTimeOffset ExpiresAt { get; set; }
    }

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _cacheFilePath;
    private readonly TimeSpan _defaultTtl;

    public OfflineLimitCache(string? cacheFilePath = null, TimeSpan? defaultTtl = null)
    {
        _cacheFilePath = cacheFilePath;
        _defaultTtl = defaultTtl ?? TimeSpan.FromHours(1);

        if (!string.IsNullOrEmpty(_cacheFilePath) && File.Exists(_cacheFilePath))
        {
            LoadFromDisk();
        }
    }

    public void Store(string limitKey, FaconLimitEvaluation evaluation, TimeSpan? ttl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(limitKey);
        ArgumentNullException.ThrowIfNull(evaluation);

        var effectiveTtl = ttl ?? _defaultTtl;
        var entry = new CacheEntry
        {
            Evaluation = evaluation,
            ExpiresAt = DateTimeOffset.UtcNow.Add(effectiveTtl)
        };

        _cache[limitKey.Trim().ToLowerInvariant()] = entry;

        if (!string.IsNullOrEmpty(_cacheFilePath))
        {
            SaveToDisk();
        }
    }

    public bool TryGet(string limitKey, out FaconLimitEvaluation? evaluation)
    {
        evaluation = null;
        if (string.IsNullOrWhiteSpace(limitKey)) return false;

        var key = limitKey.Trim().ToLowerInvariant();
        if (_cache.TryGetValue(key, out var entry))
        {
            if (DateTimeOffset.UtcNow <= entry.ExpiresAt)
            {
                evaluation = entry.Evaluation;
                return true;
            }

            // Stale entry: return stale evaluation with note in Reason
            evaluation = new FaconLimitEvaluation
            {
                Decision = entry.Evaluation.Decision,
                Limit = entry.Evaluation.Limit,
                Current = entry.Evaluation.Current,
                Projected = entry.Evaluation.Projected,
                Remaining = entry.Evaluation.Remaining,
                Reason = $"{entry.Evaluation.Reason} [Stale Offline Cache]"
            };
            return true;
        }

        return false;
    }

    public void Invalidate(string? limitKey = null)
    {
        if (string.IsNullOrEmpty(limitKey))
        {
            _cache.Clear();
        }
        else
        {
            _cache.TryRemove(limitKey.Trim().ToLowerInvariant(), out _);
        }

        if (!string.IsNullOrEmpty(_cacheFilePath))
        {
            SaveToDisk();
        }
    }

    public void SaveToDisk()
    {
        if (string.IsNullOrEmpty(_cacheFilePath)) return;

        try
        {
            var directory = Path.GetDirectoryName(_cacheFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var snapshot = _cache.ToDictionary(k => k.Key, v => v.Value);
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_cacheFilePath, json);
        }
        catch
        {
            // Disk persistence failures should not crash in-memory operations
        }
    }

    public void LoadFromDisk()
    {
        if (string.IsNullOrEmpty(_cacheFilePath) || !File.Exists(_cacheFilePath)) return;

        try
        {
            var json = File.ReadAllText(_cacheFilePath);
            var deserialized = JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(json);
            if (deserialized != null)
            {
                _cache.Clear();
                foreach (var (k, v) in deserialized)
                {
                    _cache[k] = v;
                }
            }
        }
        catch
        {
            // Disk read failures should not prevent cache startup
        }
    }
}
