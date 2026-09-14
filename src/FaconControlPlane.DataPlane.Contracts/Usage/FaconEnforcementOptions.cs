namespace FaconControlPlane.DataPlane.Contracts.Usage;

public sealed class FaconEnforcementOptions
{
    public string ControlPlaneUrl { get; set; } = "http://localhost:5000";
    public TimeSpan OfflineCacheTtl { get; set; } = TimeSpan.FromMinutes(5);
    public bool FailOpenForSoftLimits { get; set; } = true;
    public bool FailClosedForHardLimits { get; set; } = true;
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(5);
    public int MaxQueueSize { get; set; } = 1000;
}
