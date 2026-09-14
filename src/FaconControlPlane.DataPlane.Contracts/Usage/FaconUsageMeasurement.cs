using System.Text.Json.Serialization;

namespace FaconControlPlane.DataPlane.Contracts.Usage;

/// <summary>
/// Data-plane usage measurement payload reported to Control Plane (USG-003, USG-010).
/// </summary>
public sealed class FaconUsageMeasurement
{
    [JsonPropertyName("usageKey")]
    public string UsageKey { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; } = 1.0m;

    [JsonPropertyName("occurredAt")]
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("sourceEventId")]
    public string SourceEventId { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("dimensions")]
    public Dictionary<string, string>? Dimensions { get; set; }
}
