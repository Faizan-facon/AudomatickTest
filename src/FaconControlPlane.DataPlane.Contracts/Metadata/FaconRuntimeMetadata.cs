using System.Text.Json.Serialization;

namespace FaconControlPlane.DataPlane.Contracts.Metadata;

/// <summary>
/// Authoritative runtime realization stamp exposed at `/.well-known/facon`.
/// Provides active observation evidence for the FACON Deployment Platform.
/// ZERO-SECRET INVARIANT: This payload must NEVER contain credentials, tokens, or connection strings.
/// </summary>
public sealed class FaconRuntimeMetadata
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.0";

    [JsonPropertyName("applicationId")]
    public Guid ApplicationId { get; set; }

    [JsonPropertyName("applicationVersion")]
    public string ApplicationVersion { get; set; } = "0.0.0";

    [JsonPropertyName("releaseId")]
    public Guid? ReleaseId { get; set; }

    [JsonPropertyName("deploymentId")]
    public Guid? DeploymentId { get; set; }

    [JsonPropertyName("environment")]
    public string Environment { get; set; } = "development";

    [JsonPropertyName("commitSha")]
    public string CommitSha { get; set; } = "unknown";

    [JsonPropertyName("buildTimestamp")]
    public DateTimeOffset BuildTimestamp { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("endpoints")]
    public Dictionary<string, string> Endpoints { get; set; } = new()
    {
        ["liveness"] = "/health/live",
        ["readiness"] = "/health/ready"
    };

    [JsonPropertyName("activeBindings")]
    public List<string> ActiveBindings { get; set; } = [];
}
