namespace FaconControlPlane.DataPlane.Contracts.Manifest;

/// <summary>
/// Top-level declaration for the FACON Data-Plane application manifest (facon.yaml) v1.0.
/// </summary>
public sealed class FaconManifest
{
    public string SchemaVersion { get; set; } = "1.0";
    public string Name { get; set; } = string.Empty;
    public Guid ApplicationId { get; set; }
    public string Type { get; set; } = string.Empty; // service, worker, webapp, desktop
    public ManifestProbes Probes { get; set; } = new();
    public ManifestMetadata Metadata { get; set; } = new();
    public List<ManifestResourceRequirement> Resources { get; set; } = [];
    public ManifestTelemetry Telemetry { get; set; } = new();
    public List<ManifestLimit> Limits { get; set; } = [];
}

public sealed class ManifestProbes
{
    public ProbeConfig Liveness { get; set; } = new() { Path = "/health/live" };
    public ProbeConfig Readiness { get; set; } = new() { Path = "/health/ready" };
}

public sealed class ProbeConfig
{
    public string Path { get; set; } = string.Empty;
    public int Port { get; set; } = 0;
    public int InitialDelaySeconds { get; set; } = 3;
    public int PeriodSeconds { get; set; } = 10;
    public int TimeoutSeconds { get; set; } = 3;
}

public sealed class ManifestMetadata
{
    public string Endpoint { get; set; } = "/.well-known/facon";
    public string? Packaging { get; set; }
    public string? VelopackVersion { get; set; }
    public List<string> SupportedChannels { get; set; } = [];
}

/// <summary>
/// Preserves the author's original source declaration without mutation.
/// </summary>
public sealed class ManifestResourceRequirement
{
    public string LogicalName { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string Engine { get; set; } = string.Empty;
    public string? Purpose { get; set; }
    public bool Required { get; set; } = true;
}

/// <summary>
/// Canonical normalized resource identity derived from author declaration.
/// </summary>
public sealed record NormalizedResource(
    string LogicalName,
    string ResourceType,
    string Engine,
    string CanonicalResource,
    bool Required,
    string? Purpose = null);

public sealed class ManifestTelemetry
{
    public string? ServiceName { get; set; }
    public bool TracingEnabled { get; set; } = true;
    public bool MetricsEnabled { get; set; } = true;
}

public sealed class ManifestLimit
{
    public string Key { get; set; } = string.Empty;
    public string MeasurementKind { get; set; } = string.Empty; // Counter, Gauge
    public string AggregationPeriod { get; set; } = string.Empty; // None, Minute, Hour, Day
    public string EnforcementMode { get; set; } = string.Empty; // Soft, Hard
    public string? Description { get; set; }
}

public enum ManifestValidationCategory
{
    Syntax,
    Contract,
    Semantic
}

public sealed record ManifestValidationError(
    ManifestValidationCategory Category,
    string Code,
    string Path,
    string Message)
{
    public override string ToString() =>
        string.IsNullOrWhiteSpace(Path) ? Message : $"{Path}: {Message}";
}

public sealed class ManifestValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<ManifestValidationError> Errors { get; } = [];
    public List<NormalizedResource> NormalizedResources { get; } = [];
    public FaconManifest? Manifest { get; set; }

    public void AddError(ManifestValidationCategory category, string code, string path, string message)
    {
        Errors.Add(new ManifestValidationError(category, code, path, message));
    }
}

public sealed class ManifestValidationException : FormatException
{
    public ManifestValidationResult ValidationResult { get; }

    public ManifestValidationException(ManifestValidationResult validationResult)
        : base(string.Join("; ", validationResult.Errors.Select(e => e.ToString())))
    {
        ValidationResult = validationResult;
    }

    public ManifestValidationException(string message) : base(message)
    {
        ValidationResult = new ManifestValidationResult();
        ValidationResult.AddError(ManifestValidationCategory.Syntax, "SyntaxError", string.Empty, message);
    }
}
