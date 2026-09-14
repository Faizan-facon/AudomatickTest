namespace Audomatick.Desktop.Models;

/// <summary>
/// Represents release information returned by the FACON Control Plane.
/// </summary>
public sealed class DesktopReleaseInfo
{
    public Guid ReleaseId { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Channel { get; set; } = "stable";
    public string UpdatePackageUrl { get; set; } = string.Empty;
    public string PackageSha256 { get; set; } = string.Empty;
    public bool IsMandatory { get; set; }
    public DateTimeOffset ReleasedAt { get; set; } = DateTimeOffset.UtcNow;
}
