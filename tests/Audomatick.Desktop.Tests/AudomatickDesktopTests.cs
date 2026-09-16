using FaconControlPlane.DataPlane.Contracts.Manifest;
using FaconControlPlane.DataPlane.Contracts.Usage;
using Audomatick.Desktop.Models;
using Audomatick.Desktop.Services;
using Xunit;

namespace Audomatick.Desktop.Tests;

public sealed class AudomatickManifestTests
{
    [Fact]
    public void Manifest_ParsesAudomatickDesktopValidly()
    {
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "facon.yaml");
        if (!File.Exists(manifestPath))
        {
            manifestPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "facon.yaml");
        }

        Assert.True(File.Exists(manifestPath), $"facon.yaml should exist at {manifestPath}");

        var yaml = File.ReadAllText(manifestPath);
        var manifest = FaconManifestParser.Parse(yaml);

        Assert.Equal("1.0", manifest.SchemaVersion);
        Assert.Equal("audomatick-desktop", manifest.Name);
        Assert.Equal("desktop", manifest.Type);
        Assert.Equal(Guid.Parse("a1b2c3d4-e5f6-4a0b-8c1d-2e3f4a5b6c7d"), manifest.ApplicationId);
        Assert.Equal("/health/live", manifest.Probes.Liveness.Path);
        Assert.Equal("/health/ready", manifest.Probes.Readiness.Path);
        Assert.Equal("/.well-known/facon", manifest.Metadata.Endpoint);
        Assert.Equal("audomatick-desktop", manifest.Telemetry.ServiceName);

        Assert.Single(manifest.Resources);
        var db = manifest.Resources[0];
        Assert.Equal("primary-database", db.LogicalName);
        Assert.Equal("database", db.ResourceType);
        Assert.Equal("postgres", db.Engine);
        Assert.True(db.Required);

        Assert.Equal(2, manifest.Limits.Count);
        Assert.Equal("audomatick.tasks.processed.daily", manifest.Limits[0].Key);
        Assert.Equal("sessions.active", manifest.Limits[1].Key);
    }
}

public sealed class AudomatickDiagnosticsTests
{
    [Fact]
    public void Diagnostics_EnforcesZeroSecrets()
    {
        var appId = Guid.NewGuid();
        using var client = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
        var releaseClient = new ReleasePolicyClient(client, appId, "1.0.0");
        var tempFile = Path.Combine(Path.GetTempPath(), $"audomatick_diag_{Guid.NewGuid():N}.json");
        var queue = new DurableUsageQueue(tempFile);

        try
        {
            var provider = new DesktopDiagnosticsProvider(releaseClient, queue);
            var report = provider.RunDiagnostics(Guid.NewGuid(), "stable");

            Assert.True(report.IsZeroSecretCompliant);
            Assert.Empty(report.SecretViolations);
            Assert.Equal("1.0.0", report.Version);
            Assert.Equal("stable", report.Channel);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}

public sealed class AudomatickDurableQueueTests
{
    [Fact]
    public void DurableQueue_PersistsAndLoadsFromDisk()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"audomatick_queue_{Guid.NewGuid():N}.json");
        try
        {
            var queue1 = new DurableUsageQueue(tempFile);
            var measurement = new FaconUsageMeasurement
            {
                UsageKey = "audomatick.tasks.processed.daily",
                Quantity = 5.0m,
                OccurredAt = DateTimeOffset.UtcNow,
                SourceEventId = "evt-123"
            };

            queue1.Enqueue(measurement);
            Assert.Equal(1, queue1.Count);

            // Re-instantiate queue from disk
            var queue2 = new DurableUsageQueue(tempFile);
            Assert.Equal(1, queue2.Count);
            var items = queue2.PeekAll();
            Assert.Single(items);
            Assert.Equal("audomatick.tasks.processed.daily", items[0].UsageKey);
            Assert.Equal(5.0m, items[0].Quantity);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}

public sealed class AudomatickReleasePolicyPinningTests
{
    [Fact]
    public async Task ReleasePolicy_UpToDateWhenCurrentMatchesPinned()
    {
        var appId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        // Custom policy client simulation
        using var client = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
        var policyClient = new ReleasePolicyClient(client, appId, "1.0.0");
        var coordinator = new VelopackCoordinator(policyClient, updateManager: null);

        var status = await coordinator.CheckForUpdatesAsync(tenantId, "stable");
        Assert.Equal(DesktopUpdateState.UpToDate, status.State);
    }
}
