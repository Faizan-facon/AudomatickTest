using System.Drawing;
using System.Windows.Forms;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using FaconControlPlane.DataPlane.Contracts.Manifest;
using FaconControlPlane.DataPlane.Contracts.Usage;
using Audomatick.Desktop.Models;
using Audomatick.Desktop.Services;

namespace Audomatick.Desktop;

public sealed class MainForm : Form
{
    private readonly Guid _appId = Guid.Parse("a1b2c3d4-e5f6-4a0b-8c1d-2e3f4a5b6c7d");
    private readonly string _appVersion = "1.0.0";
    private Guid _tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly IReleasePolicyClient _releaseClient;
    private readonly IVelopackCoordinator _velopackCoordinator;
    private readonly IOfflineLimitCache _offlineCache;
    private readonly IDurableUsageQueue _durableQueue;
    private readonly DesktopMeteringFacade _meteringFacade;
    private readonly IDesktopDiagnosticsProvider _diagnosticsProvider;

    // UI Controls
    private Label _lblHeader = null!;
    private Label _lblSubHeader = null!;
    private TextBox _txtTenantId = null!;
    private ComboBox _cmbChannel = null!;
    private TextBox _txtFeedUrl = null!;

    private GroupBox _grpUpdates = null!;
    private Label _lblUpdateStatus = null!;
    private ProgressBar _prgUpdate = null!;
    private Button _btnCheckUpdate = null!;
    private Button _btnDownloadUpdate = null!;
    private Button _btnApplyUpdate = null!;

    private GroupBox _grpMetering = null!;
    private Label _lblLimitStatus = null!;
    private Button _btnCheckCapacity = null!;
    private Button _btnProcessTask = null!;
    private Label _lblQueueStatus = null!;
    private Button _btnFlushQueue = null!;

    private GroupBox _grpDiagnostics = null!;
    private Button _btnRunDiagnostics = null!;
    private Button _btnViewManifest = null!;
    private TextBox _txtDiagnosticsOutput = null!;

    public MainForm()
    {
        // Initialize services
        var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
        _releaseClient = new ReleasePolicyClient(httpClient, _appId, _appVersion);
        _velopackCoordinator = new VelopackCoordinator(_releaseClient);
        _offlineCache = new OfflineLimitCache();
        _durableQueue = new DurableUsageQueue();

        var enforcementOptions = Options.Create(new FaconEnforcementOptions
        {
            ControlPlaneUrl = "http://localhost:5000",
            FailClosedForHardLimits = false
        });

        var limitClient = new FaconLimitClient(httpClient, enforcementOptions, NullLogger<FaconLimitClient>.Instance);
        var usageReporter = new FaconUsageReporter(httpClient, enforcementOptions, NullLogger<FaconUsageReporter>.Instance);
        _meteringFacade = new DesktopMeteringFacade(limitClient, usageReporter, _offlineCache, _durableQueue);
        _diagnosticsProvider = new DesktopDiagnosticsProvider(_releaseClient, _durableQueue);

        InitializeUi();
        UpdateQueueStatusLabel();
    }

    private void InitializeUi()
    {
        Text = "Audomatick Desktop — FACON Velopack Client";
        Size = new Size(880, 760);
        MinimumSize = new Size(800, 680);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = Color.FromArgb(248, 250, 252);

        // Header Panel
        var pnlHeader = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(880, 75),
            Dock = DockStyle.Top,
            BackColor = Color.FromArgb(15, 23, 42),
            Padding = new Padding(20, 15, 20, 15)
        };

        _lblHeader = new Label
        {
            Text = "AUDOMATICK DESKTOP",
            Font = new Font("Segoe UI", 13F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.White,
            Location = new Point(20, 12),
            AutoSize = true
        };
        pnlHeader.Controls.Add(_lblHeader);

        _lblSubHeader = new Label
        {
            Text = $"v{_appVersion} • Velopack 1.2.0 • AppId: {_appId} • Type: desktop",
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(148, 163, 184),
            Location = new Point(22, 40),
            AutoSize = true
        };
        pnlHeader.Controls.Add(_lblSubHeader);
        Controls.Add(pnlHeader);

        // Configuration Strip
        var pnlConfig = new Panel
        {
            Location = new Point(0, 75),
            Size = new Size(880, 48),
            Dock = DockStyle.Top,
            BackColor = Color.FromArgb(241, 245, 249),
            Padding = new Padding(20, 10, 20, 10)
        };

        var lblTenant = new Label { Text = "Tenant ID:", Location = new Point(20, 14), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
        pnlConfig.Controls.Add(lblTenant);

        _txtTenantId = new TextBox
        {
            Text = _tenantId.ToString(),
            Location = new Point(88, 11),
            Width = 260,
            Font = new Font("Consolas", 8.5F)
        };
        _txtTenantId.TextChanged += (s, e) =>
        {
            if (Guid.TryParse(_txtTenantId.Text, out var parsed)) _tenantId = parsed;
        };
        pnlConfig.Controls.Add(_txtTenantId);

        var lblChannel = new Label { Text = "Channel:", Location = new Point(370, 14), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
        pnlConfig.Controls.Add(lblChannel);

        _cmbChannel = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(432, 11),
            Width = 110
        };
        _cmbChannel.Items.AddRange(new object[] { "stable", "beta", "canary" });
        _cmbChannel.SelectedIndex = 0;
        pnlConfig.Controls.Add(_cmbChannel);

        var lblFeed = new Label { Text = "Update Feed:", Location = new Point(560, 14), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
        pnlConfig.Controls.Add(lblFeed);

        _txtFeedUrl = new TextBox
        {
            Text = "https://downloads.facon.io/releases/audomatick",
            Location = new Point(645, 11),
            Width = 200,
            Font = new Font("Consolas", 8.5F)
        };
        pnlConfig.Controls.Add(_txtFeedUrl);
        Controls.Add(pnlConfig);

        // Main content flow layout
        var pnlContent = new Panel
        {
            Location = new Point(0, 123),
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(20, 15, 20, 20)
        };

        // --- 1. Velopack Updates Group ---
        _grpUpdates = new GroupBox
        {
            Text = "Velopack 1.2.0 Auto-Update Lifecycle & Pinning (DESK-004)",
            Location = new Point(20, 15),
            Size = new Size(825, 135),
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
        };

        _lblUpdateStatus = new Label
        {
            Text = "Status: Idle. Click 'Check for Updates' to verify against FACON release policy and Velopack feed.",
            Location = new Point(15, 28),
            Size = new Size(795, 22),
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            ForeColor = Color.FromArgb(71, 85, 105)
        };
        _grpUpdates.Controls.Add(_lblUpdateStatus);

        _prgUpdate = new ProgressBar
        {
            Location = new Point(15, 54),
            Size = new Size(795, 16),
            Minimum = 0,
            Maximum = 100,
            Value = 0
        };
        _grpUpdates.Controls.Add(_prgUpdate);

        _btnCheckUpdate = new Button
        {
            Text = "Check for Updates",
            Location = new Point(15, 82),
            Size = new Size(150, 34),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            BackColor = Color.FromArgb(2, 132, 199),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _btnCheckUpdate.Click += BtnCheckUpdate_Click;
        _grpUpdates.Controls.Add(_btnCheckUpdate);

        _btnDownloadUpdate = new Button
        {
            Text = "Download Update",
            Location = new Point(175, 82),
            Size = new Size(150, 34),
            Font = new Font("Segoe UI", 9F),
            Enabled = false,
            FlatStyle = FlatStyle.Flat
        };
        _btnDownloadUpdate.Click += BtnDownloadUpdate_Click;
        _grpUpdates.Controls.Add(_btnDownloadUpdate);

        _btnApplyUpdate = new Button
        {
            Text = "Apply & Restart",
            Location = new Point(335, 82),
            Size = new Size(150, 34),
            Font = new Font("Segoe UI", 9F),
            Enabled = false,
            BackColor = Color.FromArgb(22, 163, 74),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _btnApplyUpdate.Click += BtnApplyUpdate_Click;
        _grpUpdates.Controls.Add(_btnApplyUpdate);
        pnlContent.Controls.Add(_grpUpdates);

        // --- 2. FACON Metering & Offline Queue Group ---
        _grpMetering = new GroupBox
        {
            Text = "FACON Data-Plane Metering & Limit Enforcement (LIM-001..012)",
            Location = new Point(20, 165),
            Size = new Size(825, 125),
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
        };

        _lblLimitStatus = new Label
        {
            Text = "Limit: audomatick.tasks.processed.daily — Last Evaluation: Not evaluated",
            Location = new Point(15, 26),
            Size = new Size(795, 20),
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            ForeColor = Color.FromArgb(71, 85, 105)
        };
        _grpMetering.Controls.Add(_lblLimitStatus);

        _lblQueueStatus = new Label
        {
            Text = "Offline Durable Queue: 0 pending measurements",
            Location = new Point(15, 48),
            Size = new Size(795, 20),
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            ForeColor = Color.FromArgb(71, 85, 105)
        };
        _grpMetering.Controls.Add(_lblQueueStatus);

        _btnCheckCapacity = new Button
        {
            Text = "Check Quota Capacity",
            Location = new Point(15, 76),
            Size = new Size(160, 34),
            Font = new Font("Segoe UI", 9F),
            FlatStyle = FlatStyle.Flat
        };
        _btnCheckCapacity.Click += BtnCheckCapacity_Click;
        _grpMetering.Controls.Add(_btnCheckCapacity);

        _btnProcessTask = new Button
        {
            Text = "Process Automated Task (+1)",
            Location = new Point(185, 76),
            Size = new Size(200, 34),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            BackColor = Color.FromArgb(79, 70, 229),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _btnProcessTask.Click += BtnProcessTask_Click;
        _grpMetering.Controls.Add(_btnProcessTask);

        _btnFlushQueue = new Button
        {
            Text = "Flush Offline Queue",
            Location = new Point(395, 76),
            Size = new Size(150, 34),
            Font = new Font("Segoe UI", 9F),
            FlatStyle = FlatStyle.Flat
        };
        _btnFlushQueue.Click += BtnFlushQueue_Click;
        _grpMetering.Controls.Add(_btnFlushQueue);
        pnlContent.Controls.Add(_grpMetering);

        // --- 3. Diagnostics & Manifest Inspector ---
        _grpDiagnostics = new GroupBox
        {
            Text = "FACON Zero-Secret Diagnostics & Manifest (facon.yaml)",
            Location = new Point(20, 305),
            Size = new Size(825, 260),
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
        };

        _btnRunDiagnostics = new Button
        {
            Text = "Run Zero-Secret Diagnostics",
            Location = new Point(15, 26),
            Size = new Size(190, 32),
            Font = new Font("Segoe UI", 9F),
            FlatStyle = FlatStyle.Flat
        };
        _btnRunDiagnostics.Click += BtnRunDiagnostics_Click;
        _grpDiagnostics.Controls.Add(_btnRunDiagnostics);

        _btnViewManifest = new Button
        {
            Text = "Inspect facon.yaml Manifest",
            Location = new Point(215, 26),
            Size = new Size(190, 32),
            Font = new Font("Segoe UI", 9F),
            FlatStyle = FlatStyle.Flat
        };
        _btnViewManifest.Click += BtnViewManifest_Click;
        _grpDiagnostics.Controls.Add(_btnViewManifest);

        _txtDiagnosticsOutput = new TextBox
        {
            Location = new Point(15, 66),
            Size = new Size(795, 180),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 8.75F),
            BackColor = Color.FromArgb(241, 245, 249),
            ForeColor = Color.FromArgb(15, 23, 42),
            Text = "Ready. Click 'Inspect facon.yaml Manifest' or 'Run Zero-Secret Diagnostics' to audit."
        };
        _grpDiagnostics.Controls.Add(_txtDiagnosticsOutput);
        pnlContent.Controls.Add(_grpDiagnostics);

        Controls.Add(pnlContent);
    }

    private string GetSelectedChannel() => _cmbChannel.SelectedItem?.ToString() ?? "stable";

    private async void BtnCheckUpdate_Click(object? sender, EventArgs e)
    {
        _btnCheckUpdate.Enabled = false;
        _lblUpdateStatus.Text = "Checking FACON release policy and Velopack feed...";
        _lblUpdateStatus.ForeColor = Color.FromArgb(2, 132, 199);

        try
        {
            var channel = GetSelectedChannel();
            var status = await _velopackCoordinator.CheckForUpdatesAsync(_tenantId, channel);

            _lblUpdateStatus.Text = $"[{status.State}] {status.Message}";
            if (status.State == DesktopUpdateState.UpdateAvailable)
            {
                _lblUpdateStatus.ForeColor = Color.FromArgb(22, 163, 74);
                _btnDownloadUpdate.Enabled = true;
            }
            else
            {
                _lblUpdateStatus.ForeColor = Color.FromArgb(71, 85, 105);
            }
        }
        catch (Exception ex)
        {
            _lblUpdateStatus.Text = $"Update check failed: {ex.Message}";
            _lblUpdateStatus.ForeColor = Color.FromArgb(220, 38, 38);
        }
        finally
        {
            _btnCheckUpdate.Enabled = true;
        }
    }

    private async void BtnDownloadUpdate_Click(object? sender, EventArgs e)
    {
        _btnDownloadUpdate.Enabled = false;
        _lblUpdateStatus.Text = "Downloading update package via Velopack...";
        _lblUpdateStatus.ForeColor = Color.FromArgb(2, 132, 199);

        try
        {
            var success = await _velopackCoordinator.DownloadUpdateAsync(p =>
            {
                Invoke(() => _prgUpdate.Value = Math.Clamp(p, 0, 100));
            });

            if (success)
            {
                _lblUpdateStatus.Text = "Update downloaded successfully. Click 'Apply & Restart' to complete.";
                _lblUpdateStatus.ForeColor = Color.FromArgb(22, 163, 74);
                _btnApplyUpdate.Enabled = true;
            }
            else
            {
                _lblUpdateStatus.Text = "Download completed with warnings or no package retrieved.";
                _lblUpdateStatus.ForeColor = Color.FromArgb(217, 119, 6);
            }
        }
        catch (Exception ex)
        {
            _lblUpdateStatus.Text = $"Download error: {ex.Message}";
            _lblUpdateStatus.ForeColor = Color.FromArgb(220, 38, 38);
        }
        finally
        {
            _btnDownloadUpdate.Enabled = true;
        }
    }

    private void BtnApplyUpdate_Click(object? sender, EventArgs e)
    {
        var confirm = MessageBox.Show(
            "Velopack will apply the staged update and restart the application immediately. Proceed?",
            "Apply Update & Restart",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirm == DialogResult.Yes)
        {
            _velopackCoordinator.ApplyUpdateAndRestart();
        }
    }

    private async void BtnCheckCapacity_Click(object? sender, EventArgs e)
    {
        _btnCheckCapacity.Enabled = false;
        try
        {
            var eval = await _meteringFacade.CheckCapacityAsync("audomatick.tasks.processed.daily");
            _lblLimitStatus.Text = $"Limit Evaluation: [{eval.Decision}] Limit: {eval.Limit} • Current: {eval.Current} • Remaining: {eval.Remaining}";
            _lblLimitStatus.ForeColor = eval.Decision == "Allow" ? Color.FromArgb(22, 163, 74) : Color.FromArgb(217, 119, 6);
        }
        catch (Exception ex)
        {
            _lblLimitStatus.Text = $"Capacity check error: {ex.Message}";
            _lblLimitStatus.ForeColor = Color.FromArgb(220, 38, 38);
        }
        finally
        {
            _btnCheckCapacity.Enabled = true;
        }
    }

    private async void BtnProcessTask_Click(object? sender, EventArgs e)
    {
        _btnProcessTask.Enabled = false;
        try
        {
            var taskId = Guid.NewGuid().ToString("N")[..8];
            var result = await _meteringFacade.GenerateReportAsync(
                tenantId: _tenantId,
                reportType: $"AutomatedTask_{taskId}",
                ct: default);

            if (result.Success)
            {
                _txtDiagnosticsOutput.AppendText($"[{DateTime.Now:HH:mm:ss}] Task {taskId} PROCESSED: {result.Message}\r\n");
            }
            else
            {
                _txtDiagnosticsOutput.AppendText($"[{DateTime.Now:HH:mm:ss}] Task {taskId} BLOCKED: {result.Message}\r\n");
            }

            UpdateQueueStatusLabel();
        }
        catch (Exception ex)
        {
            _txtDiagnosticsOutput.AppendText($"[{DateTime.Now:HH:mm:ss}] Error: {ex.Message}\r\n");
        }
        finally
        {
            _btnProcessTask.Enabled = true;
        }
    }

    private async void BtnFlushQueue_Click(object? sender, EventArgs e)
    {
        _btnFlushQueue.Enabled = false;
        try
        {
            var flushed = await _meteringFacade.FlushOfflineQueueAsync();
            _txtDiagnosticsOutput.AppendText($"[{DateTime.Now:HH:mm:ss}] Flushed {flushed} queued measurements to Control Plane.\r\n");
            UpdateQueueStatusLabel();
        }
        catch (Exception ex)
        {
            _txtDiagnosticsOutput.AppendText($"[{DateTime.Now:HH:mm:ss}] Flush error: {ex.Message}\r\n");
        }
        finally
        {
            _btnFlushQueue.Enabled = true;
        }
    }

    private void BtnRunDiagnostics_Click(object? sender, EventArgs e)
    {
        try
        {
            var diag = _diagnosticsProvider.RunDiagnostics(_tenantId, GetSelectedChannel());
            _txtDiagnosticsOutput.Clear();
            _txtDiagnosticsOutput.AppendText("===============================================================\r\n");
            _txtDiagnosticsOutput.AppendText(" AUDOMATICK DESKTOP — FACON ZERO-SECRET DIAGNOSTICS REPORT\r\n");
            _txtDiagnosticsOutput.AppendText("===============================================================\r\n");
            _txtDiagnosticsOutput.AppendText($"Application ID:       {diag.ApplicationId}\r\n");
            _txtDiagnosticsOutput.AppendText($"Current Version:      {diag.Version}\r\n");
            _txtDiagnosticsOutput.AppendText($"Channel:              {diag.Channel}\r\n");
            _txtDiagnosticsOutput.AppendText($"Tenant ID:            {diag.TenantId}\r\n");
            _txtDiagnosticsOutput.AppendText($"Zero-Secret Safe:     {diag.IsZeroSecretCompliant}\r\n");
            _txtDiagnosticsOutput.AppendText($"Secret Violations:    {(diag.SecretViolations.Count == 0 ? "None (Compliant)" : string.Join(", ", diag.SecretViolations))}\r\n");
            _txtDiagnosticsOutput.AppendText($"Queued Measurements:  {diag.QueuedUsageMeasurementsCount}\r\n");
            _txtDiagnosticsOutput.AppendText($"Runtime Framework:    {diag.RuntimeFramework}\r\n");
            _txtDiagnosticsOutput.AppendText($"OS Description:       {diag.OsVersion}\r\n");
            _txtDiagnosticsOutput.AppendText($"Generated At:         {diag.Timestamp:u}\r\n");
            _txtDiagnosticsOutput.AppendText("===============================================================\r\n");
        }
        catch (Exception ex)
        {
            _txtDiagnosticsOutput.Text = $"Diagnostics error: {ex.Message}";
        }
    }

    private void BtnViewManifest_Click(object? sender, EventArgs e)
    {
        try
        {
            var manifestPath = Path.Combine(AppContext.BaseDirectory, "facon.yaml");
            if (!File.Exists(manifestPath))
            {
                manifestPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "facon.yaml");
            }

            if (!File.Exists(manifestPath))
            {
                _txtDiagnosticsOutput.Text = "facon.yaml manifest was not found in application directory.";
                return;
            }

            var yaml = File.ReadAllText(manifestPath);
            var manifest = FaconManifestParser.Parse(yaml);

            _txtDiagnosticsOutput.Clear();
            _txtDiagnosticsOutput.AppendText("===============================================================\r\n");
            _txtDiagnosticsOutput.AppendText(" PARSED CANONICAL FACON DATA-PLANE MANIFEST (facon.yaml)\r\n");
            _txtDiagnosticsOutput.AppendText("===============================================================\r\n");
            _txtDiagnosticsOutput.AppendText($"Schema Version:       {manifest.SchemaVersion}\r\n");
            _txtDiagnosticsOutput.AppendText($"Application Name:     {manifest.Name}\r\n");
            _txtDiagnosticsOutput.AppendText($"Application ID:       {manifest.ApplicationId}\r\n");
            _txtDiagnosticsOutput.AppendText($"Application Type:     {manifest.Type}\r\n");
            _txtDiagnosticsOutput.AppendText($"Metadata Endpoint:    {manifest.Metadata.Endpoint}\r\n");
            _txtDiagnosticsOutput.AppendText($"Telemetry Service:    {manifest.Telemetry.ServiceName}\r\n");
            _txtDiagnosticsOutput.AppendText($"Liveness Probe:       {manifest.Probes.Liveness.Path}\r\n");
            _txtDiagnosticsOutput.AppendText($"Readiness Probe:      {manifest.Probes.Readiness.Path}\r\n");
            _txtDiagnosticsOutput.AppendText($"Resources Count:      {manifest.Resources.Count}\r\n");
            _txtDiagnosticsOutput.AppendText("===============================================================\r\n\r\n");
            _txtDiagnosticsOutput.AppendText("RAW YAML:\r\n" + yaml);
        }
        catch (Exception ex)
        {
            _txtDiagnosticsOutput.Text = $"Manifest parser error: {ex.Message}";
        }
    }

    private void UpdateQueueStatusLabel()
    {
        _lblQueueStatus.Text = $"Offline Durable Queue: {_durableQueue.Count} pending measurements";
    }
}
