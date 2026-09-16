using System.Text.RegularExpressions;

namespace FaconControlPlane.DataPlane.Contracts.Manifest;

public static class FaconManifestValidator
{
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.Ordinal)
    {
        "service",
        "worker",
        "webapp",
        "desktop"
    };

    private static readonly HashSet<string> AllowedResourceTypes = new(StringComparer.Ordinal)
    {
        "database",
        "cache",
        "messageBroker",
        "objectStore"
    };

    private static readonly Dictionary<string, HashSet<string>> AllowedEnginesByResourceType = new(StringComparer.Ordinal)
    {
        ["database"] = new(StringComparer.Ordinal) { "postgres", "mysql", "sqlserver" },
        ["cache"] = new(StringComparer.Ordinal) { "redis" },
        ["messageBroker"] = new(StringComparer.Ordinal) { "rabbitmq" },
        ["objectStore"] = new(StringComparer.Ordinal) { "s3" }
    };

    private static readonly HashSet<string> AllowedMeasurementKinds = new(StringComparer.Ordinal)
    {
        "Counter",
        "Gauge"
    };

    private static readonly HashSet<string> AllowedAggregationPeriods = new(StringComparer.Ordinal)
    {
        "None",
        "Minute",
        "Hour",
        "Day"
    };

    private static readonly HashSet<string> AllowedEnforcementModes = new(StringComparer.Ordinal)
    {
        "Soft",
        "Hard"
    };

    private static readonly Regex PackagingRegex = new(@"^[a-z][a-z0-9-]{0,63}$", RegexOptions.Compiled);
    private static readonly Regex ChannelRegex = new(@"^[a-zA-Z0-9][a-zA-Z0-9._-]{0,31}$", RegexOptions.Compiled);
    private static readonly Regex ResourceLogicalNameRegex = new(@"^[a-z][a-z0-9_-]{0,63}$", RegexOptions.Compiled);

    public static ManifestValidationResult Validate(FaconManifest manifest, ManifestValidationResult? existingResult = null)
    {
        var result = existingResult ?? new ManifestValidationResult();
        result.Manifest = manifest;

        // 1. schemaVersion
        if (string.IsNullOrWhiteSpace(manifest.SchemaVersion))
        {
            result.AddError(ManifestValidationCategory.Contract, "MissingRequiredProperty", "schemaVersion", "required property is missing");
        }
        else if (manifest.SchemaVersion != "1.0")
        {
            result.AddError(ManifestValidationCategory.Contract, "InvalidSchemaVersion", "schemaVersion", $"unsupported schemaVersion '{manifest.SchemaVersion}'; expected '1.0'");
        }

        // 2. name
        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            result.AddError(ManifestValidationCategory.Contract, "MissingRequiredProperty", "name", "required property is missing");
        }
        else if (manifest.Name.Length > 128)
        {
            result.AddError(ManifestValidationCategory.Contract, "InvalidLength", "name", "name must be between 1 and 128 characters");
        }

        // 3. applicationId
        if (manifest.ApplicationId == Guid.Empty)
        {
            result.AddError(ManifestValidationCategory.Contract, "InvalidGuid", "applicationId", "applicationId must be a valid non-empty GUID");
        }

        // 4. type
        if (string.IsNullOrWhiteSpace(manifest.Type))
        {
            result.AddError(ManifestValidationCategory.Contract, "MissingRequiredProperty", "type", "required property is missing");
        }
        else if (!AllowedTypes.Contains(manifest.Type))
        {
            result.AddError(ManifestValidationCategory.Contract, "InvalidValue", "type", $"type '{manifest.Type}' is invalid; allowed values are 'service', 'worker', 'webapp', 'desktop'");
        }

        // 5. probes
        if (manifest.Probes == null)
        {
            result.AddError(ManifestValidationCategory.Contract, "MissingRequiredProperty", "probes", "required property is missing");
        }
        else
        {
            ValidateProbe(manifest.Probes.Liveness, "probes.liveness", result);
            ValidateProbe(manifest.Probes.Readiness, "probes.readiness", result);
        }

        // 6. metadata
        if (manifest.Metadata == null)
        {
            result.AddError(ManifestValidationCategory.Contract, "MissingRequiredProperty", "metadata", "required property is missing");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(manifest.Metadata.Endpoint))
            {
                result.AddError(ManifestValidationCategory.Contract, "MissingRequiredProperty", "metadata.endpoint", "required property is missing");
            }
            else
            {
                if (!manifest.Metadata.Endpoint.StartsWith('/'))
                {
                    result.AddError(ManifestValidationCategory.Contract, "InvalidPath", "metadata.endpoint", "endpoint must start with '/'");
                }
                if (manifest.Metadata.Endpoint.Contains("://"))
                {
                    result.AddError(ManifestValidationCategory.Contract, "InvalidEndpoint", "metadata.endpoint", "endpoint must not contain scheme or host");
                }
                if (manifest.Metadata.Endpoint.Length > 2048)
                {
                    result.AddError(ManifestValidationCategory.Contract, "InvalidLength", "metadata.endpoint", "endpoint must not exceed 2048 characters");
                }
            }

            if (!string.IsNullOrWhiteSpace(manifest.Metadata.Packaging))
            {
                if (!PackagingRegex.IsMatch(manifest.Metadata.Packaging))
                {
                    result.AddError(ManifestValidationCategory.Contract, "InvalidPattern", "metadata.packaging", $"packaging '{manifest.Metadata.Packaging}' is invalid; must match pattern '^[a-z][a-z0-9-]{{0,63}}$'");
                }

                if (manifest.Metadata.Packaging == "velopack")
                {
                    if (string.IsNullOrWhiteSpace(manifest.Metadata.VelopackVersion))
                    {
                        result.AddError(ManifestValidationCategory.Contract, "MissingRequiredProperty", "metadata.velopackVersion", "velopackVersion is required when packaging is 'velopack'");
                    }
                }
            }

            if (manifest.Metadata.SupportedChannels is { Count: > 0 })
            {
                var seenChannels = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < manifest.Metadata.SupportedChannels.Count; i++)
                {
                    var ch = manifest.Metadata.SupportedChannels[i];
                    if (string.IsNullOrWhiteSpace(ch) || !ChannelRegex.IsMatch(ch))
                    {
                        result.AddError(ManifestValidationCategory.Contract, "InvalidPattern", $"metadata.supportedChannels[{i}]", $"channel '{ch}' is invalid; must match pattern '^[a-zA-Z0-9][a-zA-Z0-9._-]{{0,31}}$'");
                    }
                    if (!seenChannels.Add(ch))
                    {
                        result.AddError(ManifestValidationCategory.Contract, "DuplicateValue", $"metadata.supportedChannels[{i}]", $"duplicate channel '{ch}'");
                    }
                }
            }
        }

        // 7. resources
        if (manifest.Resources != null)
        {
            var seenLogicalNames = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < manifest.Resources.Count; i++)
            {
                var r = manifest.Resources[i];
                var resourcePath = $"resources[{i}]";
                var validForNormalization = true;

                if (string.IsNullOrWhiteSpace(r.LogicalName))
                {
                    result.AddError(ManifestValidationCategory.Contract, "MissingRequiredProperty", $"{resourcePath}.logicalName", "required property is missing");
                    validForNormalization = false;
                }
                else
                {
                    if (!ResourceLogicalNameRegex.IsMatch(r.LogicalName))
                    {
                        result.AddError(ManifestValidationCategory.Contract, "InvalidPattern", $"{resourcePath}.logicalName", $"logicalName '{r.LogicalName}' is invalid; must match pattern '^[a-z][a-z0-9_-]{{0,63}}$'");
                        validForNormalization = false;
                    }

                    if (!seenLogicalNames.Add(r.LogicalName))
                    {
                        result.AddError(ManifestValidationCategory.Contract, "DuplicateResourceName", $"{resourcePath}.logicalName", $"duplicate logicalName '{r.LogicalName}' is not permitted");
                        validForNormalization = false;
                    }
                }

                if (string.IsNullOrWhiteSpace(r.ResourceType))
                {
                    result.AddError(ManifestValidationCategory.Contract, "MissingRequiredProperty", $"{resourcePath}.resourceType", "required property is missing");
                    validForNormalization = false;
                }
                else if (!AllowedResourceTypes.Contains(r.ResourceType))
                {
                    result.AddError(ManifestValidationCategory.Contract, "InvalidValue", $"{resourcePath}.resourceType", $"resourceType '{r.ResourceType}' is invalid; allowed values are 'database', 'cache', 'messageBroker', 'objectStore'");
                    validForNormalization = false;
                }

                if (string.IsNullOrWhiteSpace(r.Engine))
                {
                    result.AddError(ManifestValidationCategory.Contract, "MissingRequiredProperty", $"{resourcePath}.engine", "required property is missing");
                    validForNormalization = false;
                }
                else if (AllowedResourceTypes.Contains(r.ResourceType))
                {
                    if (AllowedEnginesByResourceType.TryGetValue(r.ResourceType, out var allowedEngines))
                    {
                        if (!allowedEngines.Contains(r.Engine))
                        {
                            result.AddError(ManifestValidationCategory.Contract, "IncompatibleEngine", $"{resourcePath}.engine", $"engine '{r.Engine}' is not valid for resourceType '{r.ResourceType}'");
                            validForNormalization = false;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(r.Purpose) && r.Purpose.Length > 512)
                {
                    result.AddError(ManifestValidationCategory.Contract, "InvalidLength", $"{resourcePath}.purpose", "purpose must not exceed 512 characters");
                }

                if (validForNormalization)
                {
                    result.NormalizedResources.Add(new NormalizedResource(
                        LogicalName: r.LogicalName,
                        ResourceType: r.ResourceType,
                        Engine: r.Engine,
                        CanonicalResource: $"{r.ResourceType}:{r.Engine}",
                        Required: r.Required,
                        Purpose: r.Purpose));
                }
            }
        }

        // 8. telemetry
        if (manifest.Telemetry != null)
        {
            if (manifest.Telemetry.ServiceName != null)
            {
                if (string.IsNullOrWhiteSpace(manifest.Telemetry.ServiceName))
                {
                    result.AddError(ManifestValidationCategory.Contract, "InvalidValue", "telemetry.serviceName", "serviceName if supplied must be non-empty");
                }
                else if (manifest.Telemetry.ServiceName.Length > 128)
                {
                    result.AddError(ManifestValidationCategory.Contract, "InvalidLength", "telemetry.serviceName", "serviceName must not exceed 128 characters");
                }
            }
        }

        // 9. limits
        if (manifest.Limits != null)
        {
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < manifest.Limits.Count; i++)
            {
                var lim = manifest.Limits[i];
                var limitPath = $"limits[{i}]";

                if (string.IsNullOrWhiteSpace(lim.Key))
                {
                    result.AddError(ManifestValidationCategory.Contract, "MissingRequiredProperty", $"{limitPath}.key", "required property is missing");
                }
                else
                {
                    if (lim.Key.Length > 128)
                    {
                        result.AddError(ManifestValidationCategory.Contract, "InvalidLength", $"{limitPath}.key", "key must not exceed 128 characters");
                    }
                    if (!seenKeys.Add(lim.Key))
                    {
                        result.AddError(ManifestValidationCategory.Contract, "DuplicateLimitKey", $"{limitPath}.key", $"duplicate limit key '{lim.Key}' is not permitted");
                    }
                }

                if (string.IsNullOrWhiteSpace(lim.MeasurementKind) || !AllowedMeasurementKinds.Contains(lim.MeasurementKind))
                {
                    result.AddError(ManifestValidationCategory.Contract, "InvalidValue", $"{limitPath}.measurementKind", $"measurementKind '{lim.MeasurementKind}' is invalid; allowed values are 'Counter', 'Gauge'");
                }

                if (string.IsNullOrWhiteSpace(lim.AggregationPeriod) || !AllowedAggregationPeriods.Contains(lim.AggregationPeriod))
                {
                    result.AddError(ManifestValidationCategory.Contract, "InvalidValue", $"{limitPath}.aggregationPeriod", $"aggregationPeriod '{lim.AggregationPeriod}' is invalid; allowed values are 'None', 'Minute', 'Hour', 'Day'");
                }

                if (string.IsNullOrWhiteSpace(lim.EnforcementMode) || !AllowedEnforcementModes.Contains(lim.EnforcementMode))
                {
                    result.AddError(ManifestValidationCategory.Contract, "InvalidValue", $"{limitPath}.enforcementMode", $"enforcementMode '{lim.EnforcementMode}' is invalid; allowed values are 'Soft', 'Hard'");
                }

                if (!string.IsNullOrWhiteSpace(lim.Description) && lim.Description.Length > 512)
                {
                    result.AddError(ManifestValidationCategory.Contract, "InvalidLength", $"{limitPath}.description", "description must not exceed 512 characters");
                }
            }
        }

        return result;
    }

    private static void ValidateProbe(ProbeConfig? probe, string path, ManifestValidationResult result)
    {
        if (probe == null)
        {
            result.AddError(ManifestValidationCategory.Contract, "MissingRequiredProperty", path, "required property is missing");
            return;
        }

        if (string.IsNullOrWhiteSpace(probe.Path))
        {
            result.AddError(ManifestValidationCategory.Contract, "MissingRequiredProperty", $"{path}.path", "required property is missing");
        }
        else
        {
            if (!probe.Path.StartsWith('/'))
            {
                result.AddError(ManifestValidationCategory.Contract, "InvalidPath", $"{path}.path", "path must start with '/'");
            }
            if (probe.Path.Length > 2048)
            {
                result.AddError(ManifestValidationCategory.Contract, "InvalidLength", $"{path}.path", "path must not exceed 2048 characters");
            }
        }

        if (probe.Port < 0 || probe.Port > 65535)
        {
            result.AddError(ManifestValidationCategory.Contract, "InvalidPort", $"{path}.port", "must be between 0 and 65535");
        }

        if (probe.InitialDelaySeconds < 0)
        {
            result.AddError(ManifestValidationCategory.Contract, "InvalidRange", $"{path}.initialDelaySeconds", "initialDelaySeconds must be >= 0");
        }

        if (probe.PeriodSeconds < 0)
        {
            result.AddError(ManifestValidationCategory.Contract, "InvalidRange", $"{path}.periodSeconds", "periodSeconds must be >= 0");
        }

        if (probe.TimeoutSeconds < 0)
        {
            result.AddError(ManifestValidationCategory.Contract, "InvalidRange", $"{path}.timeoutSeconds", "timeoutSeconds must be >= 0");
        }
    }
}

