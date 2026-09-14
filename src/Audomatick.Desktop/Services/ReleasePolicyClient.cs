using System.Net.Http.Json;
using FaconControlPlane.DataPlane.Contracts.Metadata;
using Audomatick.Desktop.Models;

namespace Audomatick.Desktop.Services;

public interface IReleasePolicyClient
{
    Guid ApplicationId { get; }
    string CurrentVersion { get; }
    Task<DesktopReleaseInfo?> ResolvePinnedReleaseAsync(Guid tenantId, string channel = "stable", CancellationToken ct = default);
    FaconRuntimeMetadata GetRuntimeMetadata(Guid? releaseId = null, Guid? tenantId = null, string channel = "stable");
}

public sealed class ReleasePolicyClient : IReleasePolicyClient
{
    private readonly HttpClient _httpClient;
    private readonly Guid _applicationId;
    private readonly string _currentVersion;

    public Guid ApplicationId => _applicationId;
    public string CurrentVersion => _currentVersion;

    public ReleasePolicyClient(HttpClient httpClient, Guid applicationId, string currentVersion)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _applicationId = applicationId;
        _currentVersion = currentVersion ?? throw new ArgumentNullException(nameof(currentVersion));
    }

    public async Task<DesktopReleaseInfo?> ResolvePinnedReleaseAsync(Guid tenantId, string channel = "stable", CancellationToken ct = default)
    {
        var url = $"/api/v1/applications/{_applicationId}/releases/resolve-desktop?tenantId={tenantId}&channel={channel}&currentVersion={_currentVersion}";

        try
        {
            var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<DesktopReleaseInfo>(cancellationToken: ct);
        }
        catch
        {
            // Network or control plane failure: fail safely, continue running current version
            return null;
        }
    }

    public FaconRuntimeMetadata GetRuntimeMetadata(Guid? releaseId = null, Guid? tenantId = null, string channel = "stable")
    {
        return new FaconRuntimeMetadata
        {
            SchemaVersion = "1.0",
            ApplicationId = _applicationId,
            ApplicationVersion = _currentVersion,
            ReleaseId = releaseId,
            Environment = "production",
            CommitSha = "desktop-local",
            BuildTimestamp = DateTimeOffset.UtcNow,
            Endpoints = new Dictionary<string, string>
            {
                ["liveness"] = "in-proc",
                ["readiness"] = "in-proc"
            }
        };
    }
}
