using System.Diagnostics;

namespace FaconControlPlane.DataPlane.Contracts.Telemetry;

public static class FaconTelemetryEnricher
{
    public const string TenantIdAttribute = "facon.tenant.id";
    public const string ApplicationIdAttribute = "facon.app.id";
    public const string ReleaseIdAttribute = "facon.release.id";
    public const string DeploymentIdAttribute = "facon.deployment.id";
    public const string EnvironmentAttribute = "facon.environment";

    public static void EnrichCurrentActivity(
        Guid applicationId,
        Guid? releaseId = null,
        Guid? deploymentId = null,
        Guid? tenantId = null,
        string? environment = null)
    {
        var activity = Activity.Current;
        if (activity == null) return;

        activity.SetTag(ApplicationIdAttribute, applicationId.ToString());

        if (releaseId.HasValue)
        {
            activity.SetTag(ReleaseIdAttribute, releaseId.Value.ToString());
        }

        if (deploymentId.HasValue)
        {
            activity.SetTag(DeploymentIdAttribute, deploymentId.Value.ToString());
        }

        if (tenantId.HasValue)
        {
            activity.SetTag(TenantIdAttribute, tenantId.Value.ToString());
        }

        if (!string.IsNullOrEmpty(environment))
        {
            activity.SetTag(EnvironmentAttribute, environment);
        }
    }
}
