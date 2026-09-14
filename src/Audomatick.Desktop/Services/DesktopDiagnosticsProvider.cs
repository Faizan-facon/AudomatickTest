using FaconControlPlane.DataPlane.Contracts.Metadata;
using Audomatick.Desktop.Models;

namespace Audomatick.Desktop.Services;

public sealed class DesktopDiagnosticsReport
{
    public Guid ApplicationId { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public bool IsZeroSecretCompliant { get; set; }
    public IReadOnlyList<string> SecretViolations { get; set; } = Array.Empty<string>();
    public int QueuedUsageMeasurementsCount { get; set; }
    public string RuntimeFramework { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

public interface IDesktopDiagnosticsProvider
{
    DesktopDiagnosticsReport RunDiagnostics(Guid tenantId, string channel = "stable");
}

public sealed class DesktopDiagnosticsProvider : IDesktopDiagnosticsProvider
{
    private readonly IReleasePolicyClient _releasePolicyClient;
    private readonly IDurableUsageQueue _durableQueue;

    public DesktopDiagnosticsProvider(IReleasePolicyClient releasePolicyClient, IDurableUsageQueue durableQueue)
    {
        _releasePolicyClient = releasePolicyClient ?? throw new ArgumentNullException(nameof(releasePolicyClient));
        _durableQueue = durableQueue ?? throw new ArgumentNullException(nameof(durableQueue));
    }

    public DesktopDiagnosticsReport RunDiagnostics(Guid tenantId, string channel = "stable")
    {
        var metadata = _releasePolicyClient.GetRuntimeMetadata(tenantId: tenantId, channel: channel);
        var (isClean, violations) = ZeroSecretValidator.ValidateZeroSecrets(metadata);

        return new DesktopDiagnosticsReport
        {
            ApplicationId = _releasePolicyClient.ApplicationId,
            Version = _releasePolicyClient.CurrentVersion,
            Channel = channel,
            TenantId = tenantId,
            IsZeroSecretCompliant = isClean,
            SecretViolations = violations,
            QueuedUsageMeasurementsCount = _durableQueue.Count,
            RuntimeFramework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            OsVersion = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Timestamp = DateTimeOffset.UtcNow
        };
    }
}
