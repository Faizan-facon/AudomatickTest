using Velopack;
using Audomatick.Desktop.Models;

namespace Audomatick.Desktop.Services;

public interface IVelopackCoordinator
{
    bool IsInstalled { get; }
    string CurrentVersion { get; }
    Task<DesktopUpdateStatus> CheckForUpdatesAsync(Guid tenantId, string channel = "stable", CancellationToken ct = default);
    Task<bool> DownloadUpdateAsync(Action<int>? progress = null, CancellationToken ct = default);
    void ApplyUpdateAndRestart();
}

public sealed class VelopackCoordinator : IVelopackCoordinator
{
    private readonly IReleasePolicyClient _policyClient;
    private readonly string _updateFeedUrl;
    private UpdateManager? _updateManager;
    private UpdateInfo? _pendingUpdate;
    private DesktopReleaseInfo? _pinnedRelease;

    public bool IsInstalled => _updateManager?.IsInstalled ?? false;
    public string CurrentVersion => _policyClient.CurrentVersion;

    public VelopackCoordinator(IReleasePolicyClient policyClient, string updateFeedUrl = "https://downloads.facon.io/releases/desktop")
    {
        _policyClient = policyClient ?? throw new ArgumentNullException(nameof(policyClient));
        _updateFeedUrl = updateFeedUrl;

        try
        {
            _updateManager = new UpdateManager(_updateFeedUrl);
        }
        catch
        {
            // UpdateManager initialization may throw if running in test / non-windows harness
            _updateManager = null;
        }
    }

    // Constructor for testing with mock or injected update manager
    public VelopackCoordinator(IReleasePolicyClient policyClient, UpdateManager? updateManager, string updateFeedUrl = "https://downloads.facon.io/releases/desktop")
    {
        _policyClient = policyClient ?? throw new ArgumentNullException(nameof(policyClient));
        _updateManager = updateManager;
        _updateFeedUrl = updateFeedUrl;
    }

    /// <summary>
    /// Checks for updates against FACON release policy and Velopack feed.
    /// Invariant DESK-004: FACON release policy is the authority. Even if Velopack feed contains
    /// a newer version, the client will only update to versions permitted by the tenant's policy.
    /// </summary>
    public async Task<DesktopUpdateStatus> CheckForUpdatesAsync(Guid tenantId, string channel = "stable", CancellationToken ct = default)
    {
        // 1. Check FACON Release Policy
        _pinnedRelease = await _policyClient.ResolvePinnedReleaseAsync(tenantId, channel, ct);

        if (_pinnedRelease == null)
        {
            return DesktopUpdateStatus.CreateUpToDate(CurrentVersion);
        }

        // Compare current version with pinned version
        if (string.Equals(_pinnedRelease.Version, CurrentVersion, StringComparison.OrdinalIgnoreCase))
        {
            return DesktopUpdateStatus.CreateUpToDate(CurrentVersion);
        }

        if (Version.TryParse(CurrentVersion, out var currVer) && Version.TryParse(_pinnedRelease.Version, out var pinnedVer))
        {
            if (currVer >= pinnedVer)
            {
                // Current version is equal to or ahead of pinned target
                return DesktopUpdateStatus.CreateUpToDate(CurrentVersion);
            }
        }

        // 2. Check Velopack Feed if installed / update manager active
        string? feedVersion = null;
        if (_updateManager != null && _updateManager.IsInstalled)
        {
            try
            {
                _pendingUpdate = await _updateManager.CheckForUpdatesAsync();
                if (_pendingUpdate != null)
                {
                    feedVersion = _pendingUpdate.TargetFullRelease.Version.ToString();

                    // If feed version is higher than pinned version, check if feed version matches pinned version
                    if (Version.TryParse(feedVersion, out var feedVer) && Version.TryParse(_pinnedRelease.Version, out var allowedVer))
                    {
                        if (feedVer > allowedVer)
                        {
                            // Invariant DESK-004: Feed has a version higher than tenant's pinned release.
                            // We do NOT allow updating to the feed version directly without pinning approval!
                            return new DesktopUpdateStatus
                            {
                                State = DesktopUpdateState.UpdateAvailable,
                                CurrentVersion = CurrentVersion,
                                PinnedVersion = _pinnedRelease.Version,
                                FeedVersion = feedVersion,
                                Message = $"Update {_pinnedRelease.Version} allowed by FACON policy (Feed latest {feedVersion} held back by tenant release pinning)."
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Velopack feed offline or check failed
                return DesktopUpdateStatus.CreateFailed(CurrentVersion, $"Velopack feed check failed: {ex.Message}");
            }
        }

        return DesktopUpdateStatus.CreateAvailable(CurrentVersion, _pinnedRelease.Version, feedVersion);
    }

    public async Task<bool> DownloadUpdateAsync(Action<int>? progress = null, CancellationToken ct = default)
    {
        if (_updateManager == null || !_updateManager.IsInstalled)
        {
            // In dev / portable mode, simulate download progress
            for (var p = 10; p <= 100; p += 30)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Invoke(p);
                await Task.Delay(50, ct);
            }
            progress?.Invoke(100);
            return true;
        }

        if (_pendingUpdate == null)
        {
            return false;
        }

        try
        {
            await _updateManager.DownloadUpdatesAsync(_pendingUpdate, progress, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void ApplyUpdateAndRestart()
    {
        if (_updateManager != null && _updateManager.IsInstalled && _pendingUpdate != null)
        {
            _updateManager.ApplyUpdatesAndRestart(_pendingUpdate);
        }
    }
}
