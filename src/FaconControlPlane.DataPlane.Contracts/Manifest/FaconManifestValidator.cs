namespace FaconControlPlane.DataPlane.Contracts.Manifest;

public sealed class ManifestValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<string> Errors { get; } = [];

    public void AddError(string error) => Errors.Add(error);
}

public static class FaconManifestValidator
{
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "service",
        "worker",
        "webapp",
        "desktop"
    };

    public static ManifestValidationResult Validate(FaconManifest manifest)
    {
        var result = new ManifestValidationResult();

        if (string.IsNullOrWhiteSpace(manifest.SchemaVersion))
        {
            result.AddError("SchemaVersion is required.");
        }
        else if (manifest.SchemaVersion != "1.0")
        {
            result.AddError($"Unsupported SchemaVersion '{manifest.SchemaVersion}'. Expected '1.0'.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            result.AddError("Manifest Name is required.");
        }

        if (manifest.ApplicationId == Guid.Empty)
        {
            result.AddError("ApplicationId must be a valid non-empty GUID.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Type) || !AllowedTypes.Contains(manifest.Type))
        {
            result.AddError($"Invalid Type '{manifest.Type}'. Supported types: service, worker, webapp, desktop.");
        }

        if (manifest.Probes == null)
        {
            result.AddError("Probes section is required.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(manifest.Probes.Liveness?.Path) || !manifest.Probes.Liveness.Path.StartsWith('/'))
            {
                result.AddError("Liveness probe path must start with '/'.");
            }

            if (string.IsNullOrWhiteSpace(manifest.Probes.Readiness?.Path) || !manifest.Probes.Readiness.Path.StartsWith('/'))
            {
                result.AddError("Readiness probe path must start with '/'.");
            }
        }

        if (manifest.Metadata == null || string.IsNullOrWhiteSpace(manifest.Metadata.Endpoint) || !manifest.Metadata.Endpoint.StartsWith('/'))
        {
            result.AddError("Metadata endpoint must start with '/'.");
        }

        if (manifest.Resources != null)
        {
            for (var i = 0; i < manifest.Resources.Count; i++)
            {
                var r = manifest.Resources[i];
                if (string.IsNullOrWhiteSpace(r.LogicalName))
                {
                    result.AddError($"Resource[{i}] LogicalName is required.");
                }

                if (string.IsNullOrWhiteSpace(r.ResourceType))
                {
                    result.AddError($"Resource[{i}] ResourceType is required.");
                }
            }
        }

        return result;
    }
}
