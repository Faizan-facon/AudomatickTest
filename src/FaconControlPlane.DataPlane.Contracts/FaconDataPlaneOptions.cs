namespace FaconControlPlane.DataPlane.Contracts;

public sealed class FaconDataPlaneOptions
{
    public Guid ApplicationId { get; set; }
    public string ApplicationName { get; set; } = string.Empty;
    public string ApplicationVersion { get; set; } = "1.0.0";
    public Guid? ReleaseId { get; set; }
    public Guid? DeploymentId { get; set; }
    public string Environment { get; set; } = "development";
    public string CommitSha { get; set; } = "dev";
    public DateTimeOffset BuildTimestamp { get; set; } = DateTimeOffset.UtcNow;

    public string LiveEndpoint { get; set; } = "/health/live";
    public string ReadyEndpoint { get; set; } = "/health/ready";
    public string MetadataEndpoint { get; set; } = "/.well-known/facon";
}
