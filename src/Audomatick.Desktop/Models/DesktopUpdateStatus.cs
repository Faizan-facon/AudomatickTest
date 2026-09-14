namespace Audomatick.Desktop.Models;

public enum DesktopUpdateState
{
    UpToDate,
    UpdateAvailable,
    Downloading,
    ReadyToInstall,
    Failed
}

/// <summary>
/// Status of update evaluation and download against FACON release policy and Velopack.
/// </summary>
public sealed class DesktopUpdateStatus
{
    public DesktopUpdateState State { get; set; } = DesktopUpdateState.UpToDate;
    public string CurrentVersion { get; set; } = string.Empty;
    public string? PinnedVersion { get; set; }
    public string? FeedVersion { get; set; }
    public double ProgressPercent { get; set; }
    public string? Message { get; set; }
    public bool CanInstall => State == DesktopUpdateState.ReadyToInstall;

    public static DesktopUpdateStatus CreateUpToDate(string currentVersion) =>
        new()
        {
            State = DesktopUpdateState.UpToDate,
            CurrentVersion = currentVersion,
            Message = "Application is up to date."
        };

    public static DesktopUpdateStatus CreateAvailable(string currentVersion, string pinnedVersion, string? feedVersion = null) =>
        new()
        {
            State = DesktopUpdateState.UpdateAvailable,
            CurrentVersion = currentVersion,
            PinnedVersion = pinnedVersion,
            FeedVersion = feedVersion,
            Message = $"Update {pinnedVersion} is available and permitted by FACON release policy."
        };

    public static DesktopUpdateStatus CreateFailed(string currentVersion, string errorMessage) =>
        new()
        {
            State = DesktopUpdateState.Failed,
            CurrentVersion = currentVersion,
            Message = errorMessage
        };
}
