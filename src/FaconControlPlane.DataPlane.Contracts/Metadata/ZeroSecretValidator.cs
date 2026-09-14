using System.Text.Json;
using System.Text.RegularExpressions;

namespace FaconControlPlane.DataPlane.Contracts.Metadata;

/// <summary>
/// Enforces the Zero-Secret Invariant across runtime metadata, logs, and diagnostics.
/// Rejects any payload containing sensitive keywords or patterns.
/// </summary>
public static class ZeroSecretValidator
{
    private static readonly string[] ForbiddenKeySubstrings =
    [
        "password",
        "secret",
        "token",
        "apikey",
        "api_key",
        "private_key",
        "privatekey",
        "credential",
        "authorization",
        "connectionstring",
        "connstr"
    ];

    private static readonly Regex PasswordInConnStrRegex = new(
        @"(?:password|pwd)\s*=\s*[^;]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex UserInfoInUriRegex = new(
        @"[a-zA-Z0-9_\-]+:[^@\s]+@",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BearerTokenRegex = new(
        @"bearer\s+[a-zA-Z0-9_\-\.]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Validates an object graph serialized as JSON for secret leakage.
    /// </summary>
    public static (bool IsClean, IReadOnlyList<string> Violations) ValidateZeroSecrets(object payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        string json;
        if (payload is string s)
        {
            json = s;
        }
        else
        {
            json = JsonSerializer.Serialize(payload);
        }

        var violations = new List<string>();

        using var doc = JsonDocument.Parse(json);
        InspectElement(doc.RootElement, "$", violations);

        return (violations.Count == 0, violations);
    }

    /// <summary>
    /// Asserts that an object graph contains zero secrets, throwing an exception if any violation is found.
    /// </summary>
    public static void AssertNoSecrets(object payload)
    {
        var (isClean, violations) = ValidateZeroSecrets(payload);
        if (!isClean)
        {
            throw new InvalidOperationException(
                $"Zero-Secret violation detected: {string.Join("; ", violations)}");
        }
    }

    private static void InspectElement(JsonElement element, string currentPath, List<string> violations)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var propPath = $"{currentPath}.{prop.Name}";
                    var lowerName = prop.Name.ToLowerInvariant();

                    foreach (var forbidden in ForbiddenKeySubstrings)
                    {
                        if (lowerName.Contains(forbidden))
                        {
                            violations.Add($"Forbidden sensitive property name detected at '{propPath}'.");
                            break;
                        }
                    }

                    InspectElement(prop.Value, propPath, violations);
                }
                break;

            case JsonValueKind.Array:
                int index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    InspectElement(item, $"{currentPath}[{index}]", violations);
                    index++;
                }
                break;

            case JsonValueKind.String:
                var val = element.GetString();
                if (!string.IsNullOrEmpty(val))
                {
                    if (PasswordInConnStrRegex.IsMatch(val))
                    {
                        violations.Add($"Embedded password in connection string detected at '{currentPath}'.");
                    }
                    if (UserInfoInUriRegex.IsMatch(val))
                    {
                        violations.Add($"Embedded credentials in URI detected at '{currentPath}'.");
                    }
                    if (BearerTokenRegex.IsMatch(val))
                    {
                        violations.Add($"Bearer token pattern detected at '{currentPath}'.");
                    }
                }
                break;
        }
    }
}
