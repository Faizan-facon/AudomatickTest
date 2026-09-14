namespace FaconControlPlane.DataPlane.Contracts.Probes;

public sealed class ReadinessCheckResult
{
    public string Name { get; }
    public bool IsReady { get; }
    public string? Description { get; }
    public IReadOnlyDictionary<string, object>? Data { get; }

    private ReadinessCheckResult(string name, bool isReady, string? description, IReadOnlyDictionary<string, object>? data)
    {
        Name = name;
        IsReady = isReady;
        Description = description;
        Data = data;
    }

    public static ReadinessCheckResult Ready(string name, string? description = null, IReadOnlyDictionary<string, object>? data = null)
        => new(name, true, description, data);

    public static ReadinessCheckResult NotReady(string name, string description, IReadOnlyDictionary<string, object>? data = null)
        => new(name, false, description, data);
}

public interface IFaconReadinessCheck
{
    string Name { get; }
    Task<ReadinessCheckResult> CheckReadinessAsync(CancellationToken cancellationToken = default);
}
