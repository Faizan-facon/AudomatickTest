using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace FaconControlPlane.DataPlane.Contracts.Manifest;

public static class FaconManifestParser
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithDuplicateKeyChecking()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private static readonly HashSet<string> RootAllowedProperties = new(StringComparer.Ordinal)
    {
        "schemaVersion", "name", "applicationId", "type", "probes", "metadata", "resources", "telemetry", "limits"
    };

    private static readonly HashSet<string> ProbesAllowedProperties = new(StringComparer.Ordinal)
    {
        "liveness", "readiness"
    };

    private static readonly HashSet<string> ProbeConfigAllowedProperties = new(StringComparer.Ordinal)
    {
        "path", "port", "initialDelaySeconds", "periodSeconds", "timeoutSeconds"
    };

    private static readonly HashSet<string> MetadataAllowedProperties = new(StringComparer.Ordinal)
    {
        "endpoint", "packaging", "velopackVersion", "supportedChannels"
    };

    private static readonly HashSet<string> ResourceAllowedProperties = new(StringComparer.Ordinal)
    {
        "logicalName", "resourceType", "engine", "purpose", "required"
    };

    private static readonly HashSet<string> ResourceForbiddenInfrastructureProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "provider", "host", "port", "username", "password", "connectionString", "vault", "deploymentTarget", "database", "credentials"
    };

    private static readonly HashSet<string> TelemetryAllowedProperties = new(StringComparer.Ordinal)
    {
        "serviceName", "tracingEnabled", "metricsEnabled"
    };

    private static readonly HashSet<string> LimitAllowedProperties = new(StringComparer.Ordinal)
    {
        "key", "measurementKind", "aggregationPeriod", "enforcementMode", "description"
    };

    public static ManifestValidationResult Validate(string yamlContent)
    {
        var result = new ManifestValidationResult();

        if (string.IsNullOrWhiteSpace(yamlContent))
        {
            result.AddError(ManifestValidationCategory.Syntax, "EmptyContent", string.Empty, "Manifest content is empty.");
            return result;
        }

        YamlStream yamlStream = new();
        try
        {
            using var reader = new StringReader(yamlContent);
            yamlStream.Load(reader);
        }
        catch (YamlException ex)
        {
            var isDuplicateKey = ex.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
            var code = isDuplicateKey ? "DuplicateKey" : "InvalidYaml";
            var path = string.Empty;
            if (isDuplicateKey)
            {
                var idx = ex.Message.IndexOf("duplicate key", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    path = ex.Message[(idx + "duplicate key".Length)..].Trim().Trim('\'', '"', '.');
                }
            }
            result.AddError(ManifestValidationCategory.Syntax, code, path, isDuplicateKey ? ex.Message : $"YAML syntax error: {ex.Message}");
            return result;
        }

        if (yamlStream.Documents.Count == 0)
        {
            result.AddError(ManifestValidationCategory.Syntax, "EmptyDocument", string.Empty, "Manifest contains no documents.");
            return result;
        }

        if (yamlStream.Documents.Count > 1)
        {
            result.AddError(ManifestValidationCategory.Syntax, "MultipleDocumentsNotSupported", string.Empty, "Manifest must contain exactly one YAML document.");
            return result;
        }

        if (yamlStream.Documents[0].RootNode is not YamlMappingNode rootMapping)
        {
            result.AddError(ManifestValidationCategory.Syntax, "InvalidRootNode", string.Empty, "Manifest root must be a YAML mapping.");
            return result;
        }

        // Validate AST strictly: feature restrictions, closed-world schema, scalar types
        InspectNodeFeatures(rootMapping, string.Empty, result);
        ValidateAstSchema(rootMapping, result);

        if (!result.IsValid)
        {
            return result;
        }

        FaconManifest manifest;
        try
        {
            manifest = Deserializer.Deserialize<FaconManifest>(yamlContent)
                ?? throw new FormatException("Failed to deserialize facon manifest from yaml.");
        }
        catch (YamlException ex)
        {
            result.AddError(ManifestValidationCategory.Syntax, "YamlDeserializationError", string.Empty, ex.Message);
            return result;
        }

        return FaconManifestValidator.Validate(manifest, result);
    }

    public static FaconManifest Parse(string yamlContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yamlContent);

        var result = Validate(yamlContent);
        if (!result.IsValid || result.Manifest is null)
        {
            throw new ManifestValidationException(result);
        }

        return result.Manifest;
    }

    public static string Serialize(FaconManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return Serializer.Serialize(manifest);
    }

    private static void InspectNodeFeatures(YamlNode node, string currentPath, ManifestValidationResult result)
    {
        if (node.NodeType == YamlNodeType.Alias)
        {
            result.AddError(ManifestValidationCategory.Syntax, "UnsupportedFeature", currentPath, "YAML anchors and aliases are not supported.");
            return;
        }

        if (!node.Anchor.IsEmpty)
        {
            result.AddError(ManifestValidationCategory.Syntax, "UnsupportedFeature", currentPath, "YAML anchors and aliases are not supported.");
        }

        if (!node.Tag.IsEmpty && !node.Tag.Value.StartsWith("tag:yaml.org,2002:", StringComparison.Ordinal))
        {
            result.AddError(ManifestValidationCategory.Syntax, "UnsupportedFeature", currentPath, $"Custom YAML tag '{node.Tag.Value}' is not supported.");
        }

        switch (node)
        {
            case YamlMappingNode mapping:
            {
                var seenKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var (keyNode, valNode) in mapping.Children)
                {
                    if (keyNode is not YamlScalarNode scalarKey)
                    {
                        result.AddError(ManifestValidationCategory.Syntax, "NonStringKey", currentPath, "Non-string mapping keys are not supported.");
                        continue;
                    }

                    if (scalarKey.Value == "<<")
                    {
                        result.AddError(ManifestValidationCategory.Syntax, "UnsupportedFeature", currentPath, "YAML merge keys ('<<') are not supported.");
                    }

                    var keyStr = scalarKey.Value ?? string.Empty;
                    var childPath = string.IsNullOrEmpty(currentPath) ? keyStr : $"{currentPath}.{keyStr}";

                    if (!seenKeys.Add(keyStr))
                    {
                        result.AddError(ManifestValidationCategory.Syntax, "DuplicateKey", childPath, $"Duplicate key '{keyStr}' is not permitted.");
                    }

                    InspectNodeFeatures(keyNode, childPath, result);
                    InspectNodeFeatures(valNode, childPath, result);
                }
                break;
            }
            case YamlSequenceNode sequence:
            {
                for (var i = 0; i < sequence.Children.Count; i++)
                {
                    var itemPath = $"{currentPath}[{i}]";
                    InspectNodeFeatures(sequence.Children[i], itemPath, result);
                }
                break;
            }
        }
    }

    private static void ValidateAstSchema(YamlMappingNode root, ManifestValidationResult result)
    {
        // 1. Root level closed-world check
        foreach (var (keyNode, valNode) in root.Children)
        {
            if (keyNode is not YamlScalarNode scalarKey) continue;
            var key = scalarKey.Value ?? string.Empty;

            if (!RootAllowedProperties.Contains(key))
            {
                result.AddError(ManifestValidationCategory.Contract, "UnknownProperty", key, $"unknown property '{key}'");
                continue;
            }

            switch (key)
            {
                case "probes" when valNode is YamlMappingNode probesMapping:
                    ValidateProbesAst(probesMapping, result);
                    break;
                case "metadata" when valNode is YamlMappingNode metadataMapping:
                    ValidateMetadataAst(metadataMapping, result);
                    break;
                case "resources" when valNode is YamlSequenceNode resourcesSeq:
                    ValidateResourcesAst(resourcesSeq, result);
                    break;
                case "telemetry" when valNode is YamlMappingNode telemetryMapping:
                    ValidateTelemetryAst(telemetryMapping, result);
                    break;
                case "limits" when valNode is YamlSequenceNode limitsSeq:
                    ValidateLimitsAst(limitsSeq, result);
                    break;
            }
        }
    }

    private static void ValidateProbesAst(YamlMappingNode probesMapping, ManifestValidationResult result)
    {
        foreach (var (keyNode, valNode) in probesMapping.Children)
        {
            if (keyNode is not YamlScalarNode scalarKey) continue;
            var key = scalarKey.Value ?? string.Empty;
            var probePath = $"probes.{key}";

            if (!ProbesAllowedProperties.Contains(key))
            {
                result.AddError(ManifestValidationCategory.Contract, "UnknownProperty", probePath, $"unknown property '{key}'");
                continue;
            }

            if (valNode is YamlMappingNode probeConfigMapping)
            {
                ValidateProbeConfigAst(probeConfigMapping, probePath, result);
            }
        }
    }

    private static void ValidateProbeConfigAst(YamlMappingNode probeConfigMapping, string probePath, ManifestValidationResult result)
    {
        foreach (var (keyNode, valNode) in probeConfigMapping.Children)
        {
            if (keyNode is not YamlScalarNode scalarKey) continue;
            var key = scalarKey.Value ?? string.Empty;
            var propPath = $"{probePath}.{key}";

            if (!ProbeConfigAllowedProperties.Contains(key))
            {
                result.AddError(ManifestValidationCategory.Contract, "UnknownProperty", propPath, $"unknown property '{key}'");
                continue;
            }

            if (key is "port" or "initialDelaySeconds" or "periodSeconds" or "timeoutSeconds")
            {
                ValidateIntegerScalar(valNode, propPath, result);
            }
        }
    }

    private static void ValidateMetadataAst(YamlMappingNode metadataMapping, ManifestValidationResult result)
    {
        foreach (var (keyNode, _) in metadataMapping.Children)
        {
            if (keyNode is not YamlScalarNode scalarKey) continue;
            var key = scalarKey.Value ?? string.Empty;
            var propPath = $"metadata.{key}";

            if (!MetadataAllowedProperties.Contains(key))
            {
                result.AddError(ManifestValidationCategory.Contract, "UnknownProperty", propPath, $"unknown property '{key}'");
            }
        }
    }

    private static void ValidateResourcesAst(YamlSequenceNode resourcesSeq, ManifestValidationResult result)
    {
        for (var i = 0; i < resourcesSeq.Children.Count; i++)
        {
            var resNode = resourcesSeq.Children[i];
            var resPath = $"resources[{i}]";

            if (resNode is not YamlMappingNode resMapping)
            {
                result.AddError(ManifestValidationCategory.Contract, "InvalidResourceType", resPath, "Resource declaration must be a YAML mapping.");
                continue;
            }

            foreach (var (keyNode, valNode) in resMapping.Children)
            {
                if (keyNode is not YamlScalarNode scalarKey) continue;
                var key = scalarKey.Value ?? string.Empty;
                var propPath = $"{resPath}.{key}";

                if (ResourceForbiddenInfrastructureProperties.Contains(key))
                {
                    result.AddError(ManifestValidationCategory.Contract, "UnknownProperty", propPath, "unknown property; provider selection belongs to the control plane");
                    continue;
                }

                if (!ResourceAllowedProperties.Contains(key))
                {
                    result.AddError(ManifestValidationCategory.Contract, "UnknownProperty", propPath, $"unknown property '{key}'");
                    continue;
                }

                if (key == "required")
                {
                    ValidateBooleanScalar(valNode, propPath, result);
                }
            }
        }
    }

    private static void ValidateTelemetryAst(YamlMappingNode telemetryMapping, ManifestValidationResult result)
    {
        foreach (var (keyNode, valNode) in telemetryMapping.Children)
        {
            if (keyNode is not YamlScalarNode scalarKey) continue;
            var key = scalarKey.Value ?? string.Empty;
            var propPath = $"telemetry.{key}";

            if (!TelemetryAllowedProperties.Contains(key))
            {
                result.AddError(ManifestValidationCategory.Contract, "UnknownProperty", propPath, $"unknown property '{key}'");
                continue;
            }

            if (key is "tracingEnabled" or "metricsEnabled")
            {
                ValidateBooleanScalar(valNode, propPath, result);
            }
        }
    }

    private static void ValidateLimitsAst(YamlSequenceNode limitsSeq, ManifestValidationResult result)
    {
        for (var i = 0; i < limitsSeq.Children.Count; i++)
        {
            var limNode = limitsSeq.Children[i];
            var limPath = $"limits[{i}]";

            if (limNode is not YamlMappingNode limMapping)
            {
                result.AddError(ManifestValidationCategory.Contract, "InvalidLimitType", limPath, "Limit declaration must be a YAML mapping.");
                continue;
            }

            foreach (var (keyNode, _) in limMapping.Children)
            {
                if (keyNode is not YamlScalarNode scalarKey) continue;
                var key = scalarKey.Value ?? string.Empty;
                var propPath = $"{limPath}.{key}";

                if (!LimitAllowedProperties.Contains(key))
                {
                    result.AddError(ManifestValidationCategory.Contract, "UnknownProperty", propPath, $"unknown property '{key}'");
                }
            }
        }
    }

    private static void ValidateBooleanScalar(YamlNode valNode, string propPath, ManifestValidationResult result)
    {
        if (valNode is not YamlScalarNode scalar)
        {
            result.AddError(ManifestValidationCategory.Contract, "InvalidBooleanType", propPath, "Must be a YAML boolean (true or false), not an object or sequence.");
            return;
        }

        if (scalar.Style != ScalarStyle.Plain || (scalar.Value != "true" && scalar.Value != "false"))
        {
            result.AddError(ManifestValidationCategory.Contract, "InvalidBooleanType", propPath, "Must be a YAML boolean (true or false), not a quoted string.");
        }
    }

    private static void ValidateIntegerScalar(YamlNode valNode, string propPath, ManifestValidationResult result)
    {
        if (valNode is not YamlScalarNode scalar)
        {
            result.AddError(ManifestValidationCategory.Contract, "InvalidIntegerType", propPath, "Must be a valid integer scalar.");
            return;
        }

        if (scalar.Style != ScalarStyle.Plain || !int.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            result.AddError(ManifestValidationCategory.Contract, "InvalidIntegerType", propPath, "Must be a valid unquoted base-10 integer.");
        }
    }
}

