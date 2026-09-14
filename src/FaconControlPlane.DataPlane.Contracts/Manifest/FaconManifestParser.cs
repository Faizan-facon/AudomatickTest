using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace FaconControlPlane.DataPlane.Contracts.Manifest;

public static class FaconManifestParser
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static FaconManifest Parse(string yamlContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yamlContent);

        var manifest = Deserializer.Deserialize<FaconManifest>(yamlContent)
            ?? throw new FormatException("Failed to deserialize facon manifest from yaml.");

        var validation = FaconManifestValidator.Validate(manifest);
        if (!validation.IsValid)
        {
            throw new FormatException($"Invalid facon.yaml manifest: {string.Join("; ", validation.Errors)}");
        }

        return manifest;
    }

    public static string Serialize(FaconManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return Serializer.Serialize(manifest);
    }
}
