using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NetworkSpeedTest.Models;

namespace NetworkSpeedTest.Services;

public static class ReportGenerator
{
    private static readonly string DefaultDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "SpeedTestReports");

    private const string SharedCss = @"
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { font-family: 'Segoe UI', Tahoma, sans-serif; background: #1a1a2e; color: #eee; padding: 30px; }
        .container { max-width: 1000px; margin: 0 auto; }
        h1 { text-align: center; margin-bottom: 5px; color: #00d4ff; }
        h2 { color: #00d4ff; margin: 30px 0 15px 0; padding-bottom: 8px; border-bottom: 1px solid #0f3460; }
        .subtitle { text-align: center; color: #888; margin-bottom: 30px; }
        .stats { display: flex; gap: 20px; justify-content: center; margin-bottom: 30px; flex-wrap: wrap; }
        .stat-card {
            background: #16213e; border-radius: 12px; padding: 20px 30px;
            text-align: center; min-width: 180px; border: 1px solid #0f3460;
        }
        .stat-card h3 { color: #00d4ff; margin-bottom: 10px; }
        .speed { font-size: 2em; font-weight: bold; }
        .speed-alt { color: #888; margin-top: 4px; }
        .detail { color: #666; margin-top: 8px; font-size: 0.9em; }
        .chart-container { background: #16213e; border-radius: 12px; padding: 20px; margin-bottom: 20px; border: 1px solid #0f3460; }
        .warning {
            background: #4a3520; border: 1px solid #e67e22; border-radius: 8px;
            padding: 15px 20px; margin-bottom: 20px; color: #f39c12;
        }
        .alert-red {
            background: #3d1515; border: 2px solid #e74c3c; border-radius: 8px;
            padding: 15px 20px; margin-bottom: 12px; color: #e74c3c; font-weight: bold;
        }
        .good { border-color: #2ecc71 !important; }
        .good h3 { color: #2ecc71 !important; }
        .warn { border-color: #f39c12 !important; }
        .warn h3 { color: #f39c12 !important; }
        .bad { border-color: #e74c3c !important; }
        .bad h3 { color: #e74c3c !important; }
        .info { background: #16213e; border-radius: 8px; padding: 15px; margin-top: 20px; border: 1px solid #0f3460; }
        .info-row { display: flex; justify-content: space-between; padding: 5px 0; border-bottom: 1px solid #0f3460; }
        .info-row:last-child { border-bottom: none; }
        .verdict-box {
            text-align: center; padding: 20px; border-radius: 12px; margin-bottom: 25px;
            font-size: 1.4em; font-weight: bold;
        }
        .verdict-healthy { background: #0d3320; border: 2px solid #2ecc71; color: #2ecc71; }
        .verdict-issues { background: #3d1515; border: 2px solid #e74c3c; color: #e74c3c; }
        .section-divider { border: none; border-top: 1px solid #0f3460; margin: 30px 0; }";

    public static string GenerateCombinedHtml(
        SpeedTestResult? speedResult,
        LatencyTestResult? latencyResult)
    {
        var now = DateTime.Now;
        string targetHost = speedResult?.TargetHost ?? latencyResult?.TargetHost ?? "unknown";
        int port = speedResult?.Port ?? latencyResult?.Port ?? 0;

        // Determine overall verdict
        var issues = new List<string>();
        if (speedResult != null && speedResult.IsAsymmetric)
            issues.Add($"Speed asymmetry detected ({speedResult.AsymmetryPercent:F1}% difference) — possible NIC issue, duplex mismatch, or path problem");
        if (latencyResult != null && latencyResult.PacketLossPercent > 1)
            issues.Add($"Packet loss {latencyResult.PacketLossPercent:F1}% — exceeds 1% threshold");
        if (latencyResult != null && latencyResult.JitterMs > 30)
            issues.Add($"Jitter {latencyResult.JitterMs:F2} ms — exceeds 30 ms threshold");
        if (latencyResult != null && latencyResult.AvgRttMs > 100)
            issues.Add($"Average latency {latencyResult.AvgRttMs:F2} ms — exceeds 100 ms threshold");

        bool healthy = issues.Count == 0;

        var sb = new StringBuilder();

        // --- HTML head ---
        sb.AppendLine($@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Network Diagnostics Report</title>
    <script src=""https://cdn.jsdelivr.net/npm/chart.js@4""></script>
    <style>{SharedCss}</style>
</head>
<body>
<div class=""container"">
    <h1>Network Diagnostics Report</h1>
    <p class=""subtitle"">{now:yyyy-MM-dd HH:mm:ss} &mdash; {targetHost}:{port}</p>");

        // --- Verdict box ---
        if (healthy)
        {
            sb.AppendLine(@"    <div class=""verdict-box verdict-healthy"">&#10003; Network Healthy</div>");
        }
        else
        {
            sb.AppendLine(@"    <div class=""verdict-box verdict-issues"">&#9888;&#65039; Issues Detected</div>");
            foreach (var issue in issues)
                sb.AppendLine($@"    <div class=""alert-red"">{issue}</div>");
        }

        // --- Test Summary info box ---
        sb.AppendLine(@"    <div class=""info"" style=""margin-bottom:25px"">");
        sb.AppendLine($@"        <div class=""info-row""><span>Report Generated</span><span>{now:yyyy-MM-dd HH:mm:ss}</span></div>");
        sb.AppendLine($@"        <div class=""info-row""><span>Target</span><span>{targetHost}:{port}</span></div>");
        if (speedResult != null)
            sb.AppendLine($@"        <div class=""info-row""><span>Speed Test</span><span>{speedResult.TestTime:HH:mm:ss} &mdash; {speedResult.ThreadCount} threads, {speedResult.DurationSeconds}s, {(speedResult.Bidirectional ? "Bidirectional" : "Download")}</span></div>");
        if (latencyResult != null)
            sb.AppendLine($@"        <div class=""info-row""><span>Latency Test</span><span>{latencyResult.TestTime:HH:mm:ss} &mdash; {latencyResult.DurationSeconds}s, {latencyResult.Sent} pings</span></div>");
        sb.AppendLine($@"        <div class=""info-row""><span>Verdict</span><span>{(healthy ? "Healthy" : $"{issues.Count} issue(s) detected")}</span></div>");
        sb.AppendLine("    </div>");

        // --- Speed Test Section ---
        if (speedResult != null)
        {
            AppendSpeedSection(sb, speedResult);
        }

        // --- Latency Section ---
        if (latencyResult != null)
        {
            if (speedResult != null)
                sb.AppendLine(@"    <hr class=""section-divider""/>");
            AppendLatencySection(sb, latencyResult);
        }

        // --- Footer ---
        sb.AppendLine(@"</div>
</body>
</html>");

        return sb.ToString();
    }

    private static void AppendSpeedSection(StringBuilder sb, SpeedTestResult r)
    {
        sb.AppendLine(@"    <h2>Speed Test Results</h2>");

        if (r.IsAsymmetric)
        {
            sb.AppendLine($@"    <div class=""warning"">
                <strong>Asymmetry Warning:</strong> {r.AsymmetryPercent:F1}% difference between upload and download speeds.
                This may indicate a NIC configuration issue, duplex mismatch, or network path problem.
            </div>");
        }

        // Stat cards
        sb.AppendLine(@"    <div class=""stats"">");
        sb.AppendLine($@"        <div class=""stat-card"">
            <h3>Download</h3>
            <div class=""speed"">{r.AverageDownloadMBps:F2} MB/s</div>
            <div class=""speed-alt"">{r.AverageDownloadMBps * 8:F2} Mbps</div>
            <div class=""detail"">Peak: {r.PeakDownloadMBps:F2} MB/s ({r.PeakDownloadMBps * 8:F2} Mbps)</div>
        </div>");
        if (r.Bidirectional)
        {
            sb.AppendLine($@"        <div class=""stat-card"">
            <h3>Upload</h3>
            <div class=""speed"">{r.AverageUploadMBps:F2} MB/s</div>
            <div class=""speed-alt"">{r.AverageUploadMBps * 8:F2} Mbps</div>
            <div class=""detail"">Peak: {r.PeakUploadMBps:F2} MB/s ({r.PeakUploadMBps * 8:F2} Mbps)</div>
        </div>");
        }
        sb.AppendLine("    </div>");

        // Charts data
        var dlLabels = string.Join(",", r.DownloadSamples.Select(s => $"\"{s.TimestampSeconds:F1}\""));
        var dlMbps = string.Join(",", r.DownloadSamples.Select(s => $"{s.MegabitsPerSecond:F2}"));
        var dlMBs = string.Join(",", r.DownloadSamples.Select(s => $"{s.MegabytesPerSecond:F2}"));
        var ulMbps = string.Join(",", r.UploadSamples.Select(s => $"{s.MegabitsPerSecond:F2}"));
        var ulMBs = string.Join(",", r.UploadSamples.Select(s => $"{s.MegabytesPerSecond:F2}"));
        var chartLabels = r.DownloadSamples.Count >= r.UploadSamples.Count ? dlLabels
            : string.Join(",", r.UploadSamples.Select(s => $"\"{s.TimestampSeconds:F1}\""));

        var ulDatasetMbps = r.Bidirectional ? $@",{{
            label: 'Upload (Mbps)', data: [{ulMbps}],
            borderColor: '#e74c3c', backgroundColor: 'rgba(231,76,60,0.1)', fill: true, tension: 0.3
        }}" : "";
        var ulDatasetMBs = r.Bidirectional ? $@",{{
            label: 'Upload (MB/s)', data: [{ulMBs}],
            borderColor: '#e74c3c', backgroundColor: 'rgba(231,76,60,0.1)', fill: true, tension: 0.3
        }}" : "";

        sb.AppendLine($@"    <div class=""chart-container""><canvas id=""chartMbps""></canvas></div>
    <div class=""chart-container""><canvas id=""chartMBs""></canvas></div>
    <script>
    new Chart(document.getElementById('chartMbps'), {{
        type: 'line',
        data: {{ labels: [{chartLabels}], datasets: [
            {{ label: 'Download (Mbps)', data: [{dlMbps}], borderColor: '#00d4ff', backgroundColor: 'rgba(0,212,255,0.1)', fill: true, tension: 0.3 }}
            {ulDatasetMbps}
        ] }},
        options: {{ responsive: true, plugins: {{ title: {{ display: true, text: 'Throughput (Mbps)', color: '#eee' }} }},
            scales: {{ x: {{ ticks: {{ color: '#888' }}, grid: {{ color: '#333' }} }}, y: {{ ticks: {{ color: '#888' }}, grid: {{ color: '#333' }}, beginAtZero: true }} }} }}
    }});
    new Chart(document.getElementById('chartMBs'), {{
        type: 'line',
        data: {{ labels: [{chartLabels}], datasets: [
            {{ label: 'Download (MB/s)', data: [{dlMBs}], borderColor: '#2ecc71', backgroundColor: 'rgba(46,204,113,0.1)', fill: true, tension: 0.3 }}
            {ulDatasetMBs}
        ] }},
        options: {{ responsive: true, plugins: {{ title: {{ display: true, text: 'Throughput (MB/s)', color: '#eee' }} }},
            scales: {{ x: {{ ticks: {{ color: '#888' }}, grid: {{ color: '#333' }} }}, y: {{ ticks: {{ color: '#888' }}, grid: {{ color: '#333' }}, beginAtZero: true }} }} }}
    }});
    </script>");

        // Info table
        sb.AppendLine($@"    <div class=""info"">
        <div class=""info-row""><span>Threads</span><span>{r.ThreadCount}</span></div>
        <div class=""info-row""><span>Duration</span><span>{r.DurationSeconds} seconds</span></div>
        <div class=""info-row""><span>Mode</span><span>{(r.Bidirectional ? "Bidirectional" : "Download only")}</span></div>
        {(r.Bidirectional ? $@"<div class=""info-row""><span>Asymmetry</span><span>{r.AsymmetryPercent:F1}%</span></div>" : "")}
    </div>");
    }

    private static void AppendLatencySection(StringBuilder sb, LatencyTestResult r)
    {
        sb.AppendLine(@"    <h2>Latency Test Results</h2>");

        string RttCls() => r.AvgRttMs < 30 ? "good" : r.AvgRttMs < 100 ? "warn" : "bad";
        string JitCls() => r.JitterMs < 10 ? "good" : r.JitterMs < 30 ? "warn" : "bad";
        string LossCls() => r.PacketLossPercent < 0.1 ? "good" : r.PacketLossPercent < 1 ? "warn" : "bad";
        string Label(string c) => c switch { "good" => "Good", "warn" => "Warning", _ => "Critical" };

        var rc = RttCls(); var jc = JitCls(); var lc = LossCls();

        sb.AppendLine($@"    <div class=""stats"">
        <div class=""stat-card {rc}""><h3>Latency (RTT)</h3>
            <div class=""speed"">{r.AvgRttMs:F2} ms</div>
            <div class=""speed-alt"">min {r.MinRttMs:F2} / max {r.MaxRttMs:F2} ms</div>
            <div class=""detail"">{Label(rc)}</div></div>
        <div class=""stat-card {jc}""><h3>Jitter</h3>
            <div class=""speed"">{r.JitterMs:F2} ms</div>
            <div class=""speed-alt"">Inter-packet variation</div>
            <div class=""detail"">{Label(jc)}</div></div>
        <div class=""stat-card {lc}""><h3>Packet Loss</h3>
            <div class=""speed"">{r.PacketLossPercent:F1}%</div>
            <div class=""speed-alt"">{r.Received}/{r.Sent} received</div>
            <div class=""detail"">{Label(lc)}</div></div>
    </div>");

        // Chart data
        var labels = string.Join(",", r.Samples.Select(s => $"\"{s.TimestampSeconds:F1}\""));
        var rttData = string.Join(",", r.Samples.Select(s => $"{s.RoundTripMs:F3}"));
        var jitterPoints = new double[r.Samples.Count];
        if (r.Samples.Count > 1)
        {
            for (int i = 1; i < r.Samples.Count; i++)
                jitterPoints[i] = Math.Abs(r.Samples[i].RoundTripMs - r.Samples[i - 1].RoundTripMs);
        }
        var jitterData = string.Join(",", jitterPoints.Select(j => $"{j:F3}"));

        sb.AppendLine($@"    <div class=""chart-container""><canvas id=""chartRtt""></canvas></div>
    <div class=""chart-container""><canvas id=""chartJitter""></canvas></div>
    <script>
    new Chart(document.getElementById('chartRtt'), {{
        type: 'line',
        data: {{ labels: [{labels}], datasets: [{{
            label: 'RTT (ms)', data: [{rttData}],
            borderColor: '#00d4ff', backgroundColor: 'rgba(0,212,255,0.1)', fill: true, tension: 0.3, pointRadius: 2
        }}] }},
        options: {{ responsive: true, plugins: {{ title: {{ display: true, text: 'Round-Trip Time (ms)', color: '#eee' }} }},
            scales: {{ x: {{ ticks: {{ color: '#888' }}, grid: {{ color: '#333' }} }}, y: {{ ticks: {{ color: '#888' }}, grid: {{ color: '#333' }}, beginAtZero: true }} }} }}
    }});
    new Chart(document.getElementById('chartJitter'), {{
        type: 'line',
        data: {{ labels: [{labels}], datasets: [{{
            label: 'Jitter (ms)', data: [{jitterData}],
            borderColor: '#f39c12', backgroundColor: 'rgba(243,156,18,0.1)', fill: true, tension: 0.3, pointRadius: 2
        }}] }},
        options: {{ responsive: true, plugins: {{ title: {{ display: true, text: 'Inter-Packet Jitter (ms)', color: '#eee' }} }},
            scales: {{ x: {{ ticks: {{ color: '#888' }}, grid: {{ color: '#333' }} }}, y: {{ ticks: {{ color: '#888' }}, grid: {{ color: '#333' }}, beginAtZero: true }} }} }}
    }});
    </script>");

        sb.AppendLine($@"    <div class=""info"">
        <div class=""info-row""><span>Duration</span><span>{r.DurationSeconds} seconds</span></div>
        <div class=""info-row""><span>Pings Sent</span><span>{r.Sent}</span></div>
        <div class=""info-row""><span>Pings Received</span><span>{r.Received}</span></div>
        <div class=""info-row""><span>Min RTT</span><span>{r.MinRttMs:F2} ms</span></div>
        <div class=""info-row""><span>Avg RTT</span><span>{r.AvgRttMs:F2} ms</span></div>
        <div class=""info-row""><span>Max RTT</span><span>{r.MaxRttMs:F2} ms</span></div>
        <div class=""info-row""><span>Jitter</span><span>{r.JitterMs:F2} ms</span></div>
    </div>");
    }

    public static string SaveCombinedReport(
        SpeedTestResult? speedResult,
        LatencyTestResult? latencyResult,
        string? outputFolder = null)
    {
        var html = GenerateCombinedHtml(speedResult, latencyResult);
        var dir = outputFolder ?? DefaultDir;
        Directory.CreateDirectory(dir);
        var filename = $"NetworkDiagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.html";
        var path = Path.Combine(dir, filename);
        File.WriteAllText(path, html, Encoding.UTF8);
        return path;
    }
}
