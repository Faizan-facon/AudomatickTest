namespace FaconControlPlane.DataPlane.Contracts.Manifest;

/// <summary>
/// Top-level declaration for the FACON Data-Plane application manifest (facon.yaml).
/// </summary>
public sealed class FaconManifest
{
    public string SchemaVersion { get; set; } = "1.0";
    public string Name { get; set; } = string.Empty;
    public Guid ApplicationId { get; set; }
    public string Type { get; set; } = "service"; // service, worker, webapp, desktop
    public ManifestProbes Probes { get; set; } = new();
    public ManifestMetadata Metadata { get; set; } = new();
    public List<ManifestResourceRequirement> Resources { get; set; } = [];
    public ManifestTelemetry Telemetry { get; set; } = new();
}

public sealed class ManifestProbes
{
    public ProbeConfig Liveness { get; set; } = new() { Path = "/health/live" };
    public ProbeConfig Readiness { get; set; } = new() { Path = "/health/ready" };
}

public sealed class ProbeConfig
{
    public string Path { get; set; } = "/health/live";
    public int? Port { get; set; }
    public int InitialDelaySeconds { get; set; } = 3;
    public int PeriodSeconds { get; set; } = 10;
    public int TimeoutSeconds { get; set; } = 3;
}

public sealed class ManifestMetadata
{
    public string Endpoint { get; set; } = "/.well-known/facon";
}

public sealed class ManifestResourceRequirement
{
    public string LogicalName { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty; // postgres, rabbitmq, redis, s3
    public string Purpose { get; set; } = string.Empty;
    public bool Required { get; set; } = true;
}

public sealed class ManifestTelemetry
{
    public string ServiceName { get; set; } = string.Empty;
    public bool TracingEnabled { get; set; } = true;
    public bool MetricsEnabled { get; set; } = true;
}
