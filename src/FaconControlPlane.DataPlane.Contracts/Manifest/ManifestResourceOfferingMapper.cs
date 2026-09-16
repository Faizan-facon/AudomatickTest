namespace FaconControlPlane.DataPlane.Contracts.Manifest;

/// <summary>
/// Result of mapping a manifest resource to control-plane offering and provider requirements.
/// </summary>
public sealed record ManifestOfferingRequirementMapping(
    string RequirementKey,
    string ResourceType,
    string Provider,
    bool Required,
    string SharingPolicy = "Dedicated");

public sealed class ManifestResourceMappingException : InvalidOperationException
{
    public string CanonicalResource { get; }
    public string LogicalName { get; }

    public ManifestResourceMappingException(string canonicalResource, string logicalName, string message)
        : base(message)
    {
        CanonicalResource = canonicalResource;
        LogicalName = logicalName;
    }
}

/// <summary>
/// Explicitly maps application-owned manifest intent to platform-owned offering requirements.
/// The pure manifest parser never selects providers; this mapping is evaluated during
/// offering creation or installation planning.
/// </summary>
public static class ManifestResourceOfferingMapper
{
    /// <summary>
    /// Explicit mapping dictionary for canonical resources to control-plane resourceType and provider.
    /// In v1.0, database:postgres is mapped to database / agent-docker.
    /// </summary>
    private static readonly Dictionary<string, (string ResourceType, string Provider)> CanonicalToOfferingMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["database:postgres"] = ("database", "agent-docker")
        };

    public static ManifestOfferingRequirementMapping MapResource(NormalizedResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (CanonicalToOfferingMap.TryGetValue(resource.CanonicalResource, out var mapping))
        {
            return new ManifestOfferingRequirementMapping(
                RequirementKey: resource.LogicalName,
                ResourceType: mapping.ResourceType,
                Provider: mapping.Provider,
                Required: resource.Required);
        }

        throw new ManifestResourceMappingException(
            resource.CanonicalResource,
            resource.LogicalName,
            $"No infrastructure offering provider registered for resource '{resource.CanonicalResource}' ('{resource.LogicalName}').");
    }

    public static bool TryMapResource(
        NormalizedResource resource,
        out ManifestOfferingRequirementMapping? mapping,
        out string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (CanonicalToOfferingMap.TryGetValue(resource.CanonicalResource, out var mapped))
        {
            mapping = new ManifestOfferingRequirementMapping(
                RequirementKey: resource.LogicalName,
                ResourceType: mapped.ResourceType,
                Provider: mapped.Provider,
                Required: resource.Required);
            errorMessage = null;
            return true;
        }

        mapping = null;
        errorMessage = $"No infrastructure offering provider registered for resource '{resource.CanonicalResource}' ('{resource.LogicalName}').";
        return false;
    }

    public static IReadOnlyList<ManifestOfferingRequirementMapping> MapAll(IEnumerable<NormalizedResource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        return resources.Select(MapResource).ToList();
    }
}
