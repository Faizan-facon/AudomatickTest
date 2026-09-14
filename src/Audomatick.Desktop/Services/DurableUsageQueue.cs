using System.Text.Json;
using FaconControlPlane.DataPlane.Contracts.Metadata;
using FaconControlPlane.DataPlane.Contracts.Usage;

namespace Audomatick.Desktop.Services;

public interface IDurableUsageQueue
{
    int Count { get; }
    void Enqueue(FaconUsageMeasurement measurement);
    IReadOnlyList<FaconUsageMeasurement> PeekAll();
    Task<int> FlushAsync(IFaconUsageReporter reporter, CancellationToken ct = default);
    void Clear();
}

public sealed class DurableUsageQueue : IDurableUsageQueue
{
    private readonly string _queueFilePath;
    private readonly object _lock = new();
    private readonly List<FaconUsageMeasurement> _items = new();

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _items.Count;
            }
        }
    }

    public DurableUsageQueue(string? queueFilePath = null)
    {
        _queueFilePath = queueFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FaconDesktop",
            "usage_queue.json");

        LoadFromDisk();
    }

    public void Enqueue(FaconUsageMeasurement measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);

        // Invariant USG-010: Zero secrets assertion
        ZeroSecretValidator.AssertNoSecrets(measurement);

        lock (_lock)
        {
            _items.Add(measurement);
            SaveToDiskInternal();
        }
    }

    public IReadOnlyList<FaconUsageMeasurement> PeekAll()
    {
        lock (_lock)
        {
            return _items.ToList();
        }
    }

    public async Task<int> FlushAsync(IFaconUsageReporter reporter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reporter);

        List<FaconUsageMeasurement> pending;
        lock (_lock)
        {
            if (_items.Count == 0) return 0;
            pending = _items.ToList();
        }

        var successfullyFlushed = 0;
        var failed = new List<FaconUsageMeasurement>();

        foreach (var item in pending)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await reporter.ReportUsageAsync(item, ct);
                successfullyFlushed++;
            }
            catch
            {
                failed.Add(item);
            }
        }

        lock (_lock)
        {
            _items.Clear();
            _items.AddRange(failed);
            SaveToDiskInternal();
        }

        return successfullyFlushed;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _items.Clear();
            SaveToDiskInternal();
        }
    }

    private void SaveToDiskInternal()
    {
        try
        {
            var dir = Path.GetDirectoryName(_queueFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(_items, new JsonSerializerOptions { WriteIndented = true });
            var tempFile = $"{_queueFilePath}.tmp.{Guid.NewGuid():N}";
            File.WriteAllText(tempFile, json);
            File.Move(tempFile, _queueFilePath, overwrite: true);
        }
        catch
        {
            // Disk persistence failures should not crash in-memory operations
        }
    }

    private void LoadFromDisk()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_queueFilePath))
                {
                    var json = File.ReadAllText(_queueFilePath);
                    var items = JsonSerializer.Deserialize<List<FaconUsageMeasurement>>(json);
                    if (items != null)
                    {
                        _items.Clear();
                        _items.AddRange(items);
                    }
                }
            }
            catch
            {
                _items.Clear();
            }
        }
    }
}
