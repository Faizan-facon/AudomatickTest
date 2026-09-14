using System.Text.Json.Serialization;

namespace FaconControlPlane.DataPlane.Contracts.Usage;

public sealed class FaconLimitEvaluation
{
    [JsonPropertyName("decision")]
    public string Decision { get; set; } = "Deny";

    [JsonPropertyName("limit")]
    public decimal Limit { get; set; }

    [JsonPropertyName("current")]
    public decimal Current { get; set; }

    [JsonPropertyName("projected")]
    public decimal Projected { get; set; }

    [JsonPropertyName("remaining")]
    public decimal Remaining { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonIgnore]
    public bool IsAllowed => Decision.Equals("Allow", StringComparison.OrdinalIgnoreCase) ||
                             Decision.Equals("AllowWithWarning", StringComparison.OrdinalIgnoreCase);
}

public sealed class FaconLimitExceededException : Exception
{
    public string LimitKey { get; }
    public decimal Limit { get; }
    public decimal Current { get; }
    public decimal Projected { get; }

    public FaconLimitExceededException(
        string limitKey,
        decimal limit,
        decimal current,
        decimal projected,
        string? reason = null)
        : base(reason ?? $"Limit exceeded for '{limitKey}': projected usage {projected} exceeds limit {limit} (current {current}).")
    {
        LimitKey = limitKey;
        Limit = limit;
        Current = current;
        Projected = projected;
    }
}
