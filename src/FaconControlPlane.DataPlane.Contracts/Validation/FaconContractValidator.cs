using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FaconControlPlane.DataPlane.Contracts.Manifest;
using FaconControlPlane.DataPlane.Contracts.Metadata;

namespace FaconControlPlane.DataPlane.Contracts.Validation;

public sealed class ContractValidationRequest
{
    public Guid? ExpectedApplicationId { get; set; }
    public Guid? ExpectedReleaseId { get; set; }
    public string? ExpectedApplicationVersion { get; set; }
    public FaconManifest? Manifest { get; set; }
    public string LivenessPath { get; set; } = "/health/live";
    public string ReadinessPath { get; set; } = "/health/ready";
    public string MetadataPath { get; set; } = "/.well-known/facon";
    public bool ExpectReady { get; set; } = true;
}

public sealed class ContractCheckResult
{
    public string CheckName { get; init; } = string.Empty;
    public bool Passed { get; init; }
    public string Details { get; init; } = string.Empty;
}

public sealed class ContractValidationReport
{
    public bool IsSuccess => Checks.All(c => c.Passed);
    public List<ContractCheckResult> Checks { get; } = [];
    public FaconRuntimeMetadata? ObservedMetadata { get; set; }
    public IReadOnlyList<string> Failures => Checks.Where(c => !c.Passed).Select(c => $"{c.CheckName}: {c.Details}").ToList();

    public void AddCheck(string name, bool passed, string details)
    {
        Checks.Add(new ContractCheckResult { CheckName = name, Passed = passed, Details = details });
    }
}

public sealed class FaconContractValidator
{
    private readonly HttpClient _httpClient;

    public FaconContractValidator(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<ContractValidationReport> ValidateContractAsync(
        ContractValidationRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        request ??= new ContractValidationRequest();
        var report = new ContractValidationReport();

        var livenessPath = request.Manifest?.Probes?.Liveness?.Path ?? request.LivenessPath;
        var readinessPath = request.Manifest?.Probes?.Readiness?.Path ?? request.ReadinessPath;
        var metadataPath = request.Manifest?.Metadata?.Endpoint ?? request.MetadataPath;

        // 1. Validate Liveness Probe
        try
        {
            var liveResponse = await _httpClient.GetAsync(livenessPath, cancellationToken);
            if (liveResponse.StatusCode == HttpStatusCode.OK)
            {
                report.AddCheck("Liveness Probe", true, $"Responded with HTTP 200 at {livenessPath}");
            }
            else
            {
                report.AddCheck("Liveness Probe", false, $"Expected HTTP 200 at {livenessPath}, but received {(int)liveResponse.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            report.AddCheck("Liveness Probe", false, $"Failed to reach {livenessPath}: {ex.Message}");
        }

        // 2. Validate Readiness Probe
        try
        {
            var readyResponse = await _httpClient.GetAsync(readinessPath, cancellationToken);
            var expectedStatus = request.ExpectReady ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable;

            if (readyResponse.StatusCode == expectedStatus)
            {
                report.AddCheck("Readiness Probe", true, $"Responded with expected {(int)expectedStatus} at {readinessPath}");
            }
            else
            {
                report.AddCheck("Readiness Probe", false, $"Expected {(int)expectedStatus} at {readinessPath}, but received {(int)readyResponse.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            report.AddCheck("Readiness Probe", false, $"Failed to reach {readinessPath}: {ex.Message}");
        }

        // 3. Validate Runtime Metadata Endpoint & Schema
        try
        {
            var metaResponse = await _httpClient.GetAsync(metadataPath, cancellationToken);
            if (metaResponse.StatusCode != HttpStatusCode.OK)
            {
                report.AddCheck("Metadata Endpoint", false, $"Expected HTTP 200 at {metadataPath}, but received {(int)metaResponse.StatusCode}");
                return report;
            }

            var rawJson = await metaResponse.Content.ReadAsStringAsync(cancellationToken);
            var metadata = JsonSerializer.Deserialize<FaconRuntimeMetadata>(rawJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (metadata == null)
            {
                report.AddCheck("Metadata Deserialization", false, "Response body could not be parsed as FaconRuntimeMetadata.");
                return report;
            }

            report.ObservedMetadata = metadata;
            report.AddCheck("Metadata Deserialization", true, $"Successfully parsed metadata from {metadataPath}");

            // Verify Schema Version
            if (metadata.SchemaVersion == "1.0")
            {
                report.AddCheck("Schema Version", true, "Schema version is '1.0'");
            }
            else
            {
                report.AddCheck("Schema Version", false, $"Expected schema version '1.0', but was '{metadata.SchemaVersion}'");
            }

            // Verify ApplicationId
            if (metadata.ApplicationId == Guid.Empty)
            {
                report.AddCheck("Application ID", false, "ApplicationId is Guid.Empty.");
            }
            else if (request.ExpectedApplicationId.HasValue && metadata.ApplicationId != request.ExpectedApplicationId.Value)
            {
                report.AddCheck("Application ID", false, $"Expected ApplicationId {request.ExpectedApplicationId}, but was {metadata.ApplicationId}");
            }
            else
            {
                report.AddCheck("Application ID", true, $"Valid ApplicationId: {metadata.ApplicationId}");
            }

            // Verify ReleaseId if expected
            if (request.ExpectedReleaseId.HasValue)
            {
                if (metadata.ReleaseId == request.ExpectedReleaseId.Value)
                {
                    report.AddCheck("Release ID", true, $"Release ID matches expected: {metadata.ReleaseId}");
                }
                else
                {
                    report.AddCheck("Release ID", false, $"Expected ReleaseId {request.ExpectedReleaseId}, but was {metadata.ReleaseId}");
                }
            }

            // Verify ApplicationVersion if expected
            if (!string.IsNullOrEmpty(request.ExpectedApplicationVersion))
            {
                if (metadata.ApplicationVersion == request.ExpectedApplicationVersion)
                {
                    report.AddCheck("Application Version", true, $"Version matches expected: {metadata.ApplicationVersion}");
                }
                else
                {
                    report.AddCheck("Application Version", false, $"Expected Version {request.ExpectedApplicationVersion}, but was {metadata.ApplicationVersion}");
                }
            }

            // 4. Zero-Secret Invariant Check
            var (isClean, violations) = ZeroSecretValidator.ValidateZeroSecrets(rawJson);
            if (isClean)
            {
                report.AddCheck("Zero-Secret Invariant", true, "Metadata contains no sensitive tokens, passwords, or connection strings.");
            }
            else
            {
                report.AddCheck("Zero-Secret Invariant", false, $"Violations detected: {string.Join("; ", violations)}");
            }

            // 5. Manifest Consistency Check
            if (request.Manifest != null)
            {
                var manifestErrors = new List<string>();
                if (request.Manifest.ApplicationId != metadata.ApplicationId)
                {
                    manifestErrors.Add($"Manifest ApplicationId ({request.Manifest.ApplicationId}) does not match metadata ({metadata.ApplicationId})");
                }

                if (metadata.Endpoints.TryGetValue("liveness", out var metaLive) && metaLive != request.Manifest.Probes.Liveness.Path)
                {
                    manifestErrors.Add($"Manifest Liveness path ({request.Manifest.Probes.Liveness.Path}) does not match metadata ({metaLive})");
                }

                if (metadata.Endpoints.TryGetValue("readiness", out var metaReady) && metaReady != request.Manifest.Probes.Readiness.Path)
                {
                    manifestErrors.Add($"Manifest Readiness path ({request.Manifest.Probes.Readiness.Path}) does not match metadata ({metaReady})");
                }

                if (manifestErrors.Count == 0)
                {
                    report.AddCheck("Manifest Consistency", true, "Application runtime metadata is consistent with facon.yaml manifest.");
                }
                else
                {
                    report.AddCheck("Manifest Consistency", false, string.Join("; ", manifestErrors));
                }
            }
        }
        catch (Exception ex)
        {
            report.AddCheck("Metadata Endpoint", false, $"Error validating metadata: {ex.Message}");
        }

        return report;
    }
}
