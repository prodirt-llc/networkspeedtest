using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using NetworkSpeedTest.Models;
using NetworkSpeedTest.Services;

namespace NetworkSpeedTest;

public partial class MainWindow : Window
{
    private SpeedTestServer? _server;
    private SpeedTestClient? _client;
    private LatencyTestClient? _latencyClient;
    private SpeedTestResult? _lastSpeedResult;
    private LatencyTestResult? _lastLatencyResult;
    private bool _serverRunning;
    private bool _testRunning;
    private bool _latencyTestRunning;

    // Track latest speeds for bidirectional display
    private double _latestDownloadMBps;
    private double _latestUploadMBps;
    private bool _asymmetryDetected;

    // Color constants for health indicators
    private static readonly SolidColorBrush GreenBrush = new(Color.FromRgb(0x2E, 0xCC, 0x71));
    private static readonly SolidColorBrush YellowBrush = new(Color.FromRgb(0xF3, 0x9C, 0x12));
    private static readonly SolidColorBrush RedBrush = new(Color.FromRgb(0xE7, 0x4C, 0x3C));
    private static readonly SolidColorBrush GreenBorder = new(Color.FromRgb(0x2E, 0xCC, 0x71));
    private static readonly SolidColorBrush YellowBorder = new(Color.FromRgb(0xF3, 0x9C, 0x12));
    private static readonly SolidColorBrush RedBorder = new(Color.FromRgb(0xE7, 0x4C, 0x3C));
    private static readonly SolidColorBrush DefaultBorder = new(Color.FromRgb(0x0F, 0x34, 0x60));

    public MainWindow()
    {
        InitializeComponent();
        var defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "SpeedTestReports");
        OutputFolderBox.Text = defaultDir;
        AddStatus("Ready. Select Server, Client, or Latency Test mode to begin.");
    }

    private void AddStatus(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => AddStatus(message));
            return;
        }

        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        StatusLog.Items.Add($"[{timestamp}] {message}");
        StatusLog.ScrollIntoView(StatusLog.Items[StatusLog.Items.Count - 1]);
    }

    private void ModeTab_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;

        // Tab index 2 = Latency Test
        bool isLatencyTab = ModeTab.SelectedIndex == 2;

        if (isLatencyTab)
        {
            ThroughputResultsPanel.Visibility = Visibility.Collapsed;
            LatencyResultsPanel.Visibility = Visibility.Visible;
        }
        else
        {
            ThroughputResultsPanel.Visibility = Visibility.Visible;
            LatencyResultsPanel.Visibility = Visibility.Collapsed;
        }
    }

    #region Server Mode

    private async void StartServerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_serverRunning) return;

        if (!int.TryParse(ServerPortBox.Text, out int port) || port < 1 || port > 65535)
        {
            MessageBox.Show("Enter a valid port (1-65535).", "Invalid Port", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _serverRunning = true;
        StartServerBtn.IsEnabled = false;
        StopServerBtn.IsEnabled = true;

        _server = new SpeedTestServer { Port = port };
        _server.StatusMessage += AddStatus;

        try
        {
            await Task.Run(() => _server.StartAsync());
        }
        catch (Exception ex)
        {
            AddStatus($"Server error: {ex.Message}");
        }
    }

    private void StopServerBtn_Click(object sender, RoutedEventArgs e)
    {
        _server?.Stop();
        _serverRunning = false;
        StartServerBtn.IsEnabled = true;
        StopServerBtn.IsEnabled = false;
    }

    #endregion

    #region Client Mode (Throughput)

    private async void StartTestBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_testRunning) return;

        string target = TargetIpBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            MessageBox.Show("Enter a target IP address.", "Missing Target", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(ClientPortBox.Text, out int port) || port < 1 || port > 65535)
        {
            MessageBox.Show("Enter a valid port (1-65535).", "Invalid Port", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(ThreadCountBox.Text, out int threads) || threads < 1 || threads > 128)
        {
            MessageBox.Show("Enter a valid thread count (1-128).", "Invalid Threads", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(DurationBox.Text, out int duration) || duration < 1 || duration > 300)
        {
            MessageBox.Show("Enter a valid duration (1-300 seconds).", "Invalid Duration", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _testRunning = true;
        StartTestBtn.IsEnabled = false;
        StopTestBtn.IsEnabled = true;

        ResetThroughputDisplay();

        _client = new SpeedTestClient
        {
            TargetHost = target,
            Port = port,
            ThreadCount = threads,
            DurationSeconds = duration,
            Bidirectional = BidirectionalCheck.IsChecked == true
        };
        _client.StatusMessage += AddStatus;
        _client.ProgressUpdate += OnProgressUpdate;

        try
        {
            _lastSpeedResult = await Task.Run(() => _client.RunAsync());
            UpdateFinalThroughputDisplay(_lastSpeedResult);
        }
        catch (Exception ex)
        {
            AddStatus($"Error: {ex.Message}");
        }
        finally
        {
            _testRunning = false;
            StartTestBtn.IsEnabled = true;
            StopTestBtn.IsEnabled = false;
        }
    }

    private void StopTestBtn_Click(object sender, RoutedEventArgs e)
    {
        _client?.Cancel();
        AddStatus("Stopping test...");
    }

    private void OnProgressUpdate(double progress, double downloadMBps, double uploadMBps)
    {
        if (downloadMBps > 0) _latestDownloadMBps = downloadMBps;
        if (uploadMBps > 0) _latestUploadMBps = uploadMBps;

        Dispatcher.BeginInvoke(() =>
        {
            double pct = Math.Min(progress * 100, 100);
            TestProgress.Value = pct;
            ProgressText.Text = $"{pct:F0}%";

            if (_latestDownloadMBps > 0)
            {
                DownloadSpeedMB.Text = $"{_latestDownloadMBps:F2} MB/s";
                DownloadSpeedMbps.Text = $"{_latestDownloadMBps * 8:F2} Mbps";
            }

            if (_latestUploadMBps > 0)
            {
                UploadSpeedMB.Text = $"{_latestUploadMBps:F2} MB/s";
                UploadSpeedMbps.Text = $"{_latestUploadMBps * 8:F2} Mbps";
            }

            // Live asymmetry check — once detected, keep showing it
            if (_latestDownloadMBps > 0 && _latestUploadMBps > 0)
            {
                double max = Math.Max(_latestDownloadMBps, _latestUploadMBps);
                double min = Math.Min(_latestDownloadMBps, _latestUploadMBps);
                double asymPct = ((max - min) / max) * 100.0;
                if (asymPct > 20)
                {
                    _asymmetryDetected = true;
                    AsymmetryWarning.Visibility = Visibility.Visible;
                    AsymmetryPercent.Text = $"{asymPct:F1}%";
                }
                else if (!_asymmetryDetected)
                {
                    AsymmetryWarning.Visibility = Visibility.Collapsed;
                }
            }
        });
    }

    private void ResetThroughputDisplay()
    {
        _latestDownloadMBps = 0;
        _latestUploadMBps = 0;
        _asymmetryDetected = false;
        DownloadSpeedMB.Text = "-- MB/s";
        DownloadSpeedMbps.Text = "-- Mbps";
        UploadSpeedMB.Text = "-- MB/s";
        UploadSpeedMbps.Text = "-- Mbps";
        TestProgress.Value = 0;
        ProgressText.Text = "Testing...";
        AsymmetryWarning.Visibility = Visibility.Collapsed;
    }

    private void UpdateFinalThroughputDisplay(SpeedTestResult result)
    {
        TestProgress.Value = 100;
        ProgressText.Text = "Complete";

        DownloadSpeedMB.Text = $"{result.AverageDownloadMBps:F2} MB/s";
        DownloadSpeedMbps.Text = $"{result.AverageDownloadMBps * 8:F2} Mbps";

        if (result.Bidirectional)
        {
            UploadSpeedMB.Text = $"{result.AverageUploadMBps:F2} MB/s";
            UploadSpeedMbps.Text = $"{result.AverageUploadMBps * 8:F2} Mbps";

            if (result.IsAsymmetric)
            {
                AsymmetryWarning.Visibility = Visibility.Visible;
                AsymmetryPercent.Text = $"{result.AsymmetryPercent:F1}%";
            }
        }
    }

    #endregion

    #region Latency Test Mode

    private async void StartLatencyBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_latencyTestRunning) return;

        string target = LatencyTargetBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            MessageBox.Show("Enter a target IP address.", "Missing Target", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(LatencyPortBox.Text, out int port) || port < 1 || port > 65535)
        {
            MessageBox.Show("Enter a valid port (1-65535).", "Invalid Port", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(LatencyDurationBox.Text, out int duration) || duration < 1 || duration > 300)
        {
            MessageBox.Show("Enter a valid duration (1-300 seconds).", "Invalid Duration", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _latencyTestRunning = true;
        StartLatencyBtn.IsEnabled = false;
        StopLatencyBtn.IsEnabled = true;

        ResetLatencyDisplay();

        _latencyClient = new LatencyTestClient
        {
            TargetHost = target,
            Port = port,
            DurationSeconds = duration
        };
        _latencyClient.StatusMessage += AddStatus;
        _latencyClient.LatencyUpdate += OnLatencyUpdate;

        try
        {
            _lastLatencyResult = await Task.Run(() => _latencyClient.RunAsync());
            UpdateFinalLatencyDisplay(_lastLatencyResult);
        }
        catch (Exception ex)
        {
            AddStatus($"Latency error: {ex.Message}");
        }
        finally
        {
            _latencyTestRunning = false;
            StartLatencyBtn.IsEnabled = true;
            StopLatencyBtn.IsEnabled = false;
        }
    }

    private void StopLatencyBtn_Click(object sender, RoutedEventArgs e)
    {
        _latencyClient?.Cancel();
        AddStatus("Stopping latency test...");
    }

    private void OnLatencyUpdate(double progress, double rttMs, double jitterMs, double lossPercent)
    {
        Dispatcher.BeginInvoke(() =>
        {
            double pct = Math.Min(progress * 100, 100);
            TestProgress.Value = pct;
            ProgressText.Text = $"{pct:F0}%";

            LatencyAvgText.Text = $"{rttMs:F2} ms";
            JitterText.Text = $"{jitterMs:F2} ms";
            LossText.Text = $"{lossPercent:F1}%";

            // Color-code cards
            ApplyHealthColor(LatencyCard, LatencyCardTitle, rttMs < 30 ? "good" : rttMs < 100 ? "warn" : "bad");
            ApplyHealthColor(JitterCard, JitterCardTitle, jitterMs < 10 ? "good" : jitterMs < 30 ? "warn" : "bad");
            ApplyHealthColor(LossCard, LossCardTitle, lossPercent < 0.1 ? "good" : lossPercent < 1 ? "warn" : "bad");
        });
    }

    private void ResetLatencyDisplay()
    {
        TestProgress.Value = 0;
        ProgressText.Text = "Testing latency...";
        LatencyAvgText.Text = "-- ms";
        LatencyMinMaxText.Text = "min -- / max --";
        JitterText.Text = "-- ms";
        LossText.Text = "-- %";
        LossDetailText.Text = "--/-- received";

        // Reset to default colors
        ApplyHealthColor(LatencyCard, LatencyCardTitle, "good");
        ApplyHealthColor(JitterCard, JitterCardTitle, "good");
        ApplyHealthColor(LossCard, LossCardTitle, "good");
    }

    private void UpdateFinalLatencyDisplay(LatencyTestResult result)
    {
        TestProgress.Value = 100;
        ProgressText.Text = "Complete";

        LatencyAvgText.Text = $"{result.AvgRttMs:F2} ms";
        LatencyMinMaxText.Text = $"min {result.MinRttMs:F2} / max {result.MaxRttMs:F2}";
        JitterText.Text = $"{result.JitterMs:F2} ms";
        LossText.Text = $"{result.PacketLossPercent:F1}%";
        LossDetailText.Text = $"{result.Received}/{result.Sent} received";

        ApplyHealthColor(LatencyCard, LatencyCardTitle,
            result.AvgRttMs < 30 ? "good" : result.AvgRttMs < 100 ? "warn" : "bad");
        ApplyHealthColor(JitterCard, JitterCardTitle,
            result.JitterMs < 10 ? "good" : result.JitterMs < 30 ? "warn" : "bad");
        ApplyHealthColor(LossCard, LossCardTitle,
            result.PacketLossPercent < 0.1 ? "good" : result.PacketLossPercent < 1 ? "warn" : "bad");
    }

    private static void ApplyHealthColor(Border card, TextBlock title, string level)
    {
        switch (level)
        {
            case "good":
                card.BorderBrush = GreenBorder;
                title.Foreground = GreenBrush;
                break;
            case "warn":
                card.BorderBrush = YellowBorder;
                title.Foreground = YellowBrush;
                break;
            default:
                card.BorderBrush = RedBorder;
                title.Foreground = RedBrush;
                break;
        }
    }

    #endregion

    #region Report

    private void BrowseFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Report Output Folder"
        };

        if (dialog.ShowDialog() == true)
        {
            OutputFolderBox.Text = dialog.FolderName;
        }
    }

    private void GenerateReportBtn_Click(object sender, RoutedEventArgs e)
    {
        bool hasSpeed = _lastSpeedResult != null;
        bool hasLatency = _lastLatencyResult != null;

        if (!hasSpeed && !hasLatency)
        {
            MessageBox.Show("Run at least one test before generating a report.",
                "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new ReportDialog(hasSpeed, hasLatency) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        string? folder = string.IsNullOrWhiteSpace(OutputFolderBox.Text) ? null : OutputFolderBox.Text;

        try
        {
            var speedForReport = dialog.IncludeSpeed ? _lastSpeedResult : null;
            var latencyForReport = dialog.IncludeLatency ? _lastLatencyResult : null;

            string path = ReportGenerator.SaveCombinedReport(speedForReport, latencyForReport, folder);
            AddStatus($"Report saved: {path}");

            var result = MessageBox.Show($"Report saved to:\n{path}\n\nOpen in browser?",
                "Report Saved", MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save report: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    protected override void OnClosed(EventArgs e)
    {
        _server?.Stop();
        _client?.Cancel();
        _latencyClient?.Cancel();
        base.OnClosed(e);
    }
}
