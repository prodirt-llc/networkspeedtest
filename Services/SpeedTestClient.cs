using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NetworkSpeedTest.Models;

namespace NetworkSpeedTest.Services;

public class SpeedTestClient
{
    public string TargetHost { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 5201;
    public int ThreadCount { get; set; } = 8;
    public int DurationSeconds { get; set; } = 10;
    public bool Bidirectional { get; set; }

    private CancellationTokenSource? _cts;

    public event Action<string>? StatusMessage;
    public event Action<double, double, double>? ProgressUpdate; // elapsed, downloadMBps, uploadMBps

    public void Cancel()
    {
        _cts?.Cancel();
    }

    public async Task<SpeedTestResult> RunAsync()
    {
        _cts = new CancellationTokenSource();
        var result = new SpeedTestResult
        {
            ThreadCount = ThreadCount,
            DurationSeconds = DurationSeconds,
            Bidirectional = Bidirectional,
            TargetHost = TargetHost,
            Port = Port,
            TestTime = DateTime.Now
        };

        try
        {
            if (Bidirectional)
            {
                StatusMessage?.Invoke("Starting bidirectional speed test...");
                var downloadTask = RunDirectionAsync(Protocol.CmdStartDownload, isDownload: true, result, _cts.Token);
                var uploadTask = RunDirectionAsync(Protocol.CmdStartUpload, isDownload: false, result, _cts.Token);
                await Task.WhenAll(downloadTask, uploadTask);
            }
            else
            {
                StatusMessage?.Invoke("Starting download speed test...");
                await RunDirectionAsync(Protocol.CmdStartDownload, isDownload: true, result, _cts.Token);
            }

            // Compute averages
            if (result.DownloadSamples.Count > 0)
            {
                result.AverageDownloadMBps = result.DownloadSamples.Average(s => s.MegabytesPerSecond);
                result.PeakDownloadMBps = result.DownloadSamples.Max(s => s.MegabytesPerSecond);
            }
            if (result.UploadSamples.Count > 0)
            {
                result.AverageUploadMBps = result.UploadSamples.Average(s => s.MegabytesPerSecond);
                result.PeakUploadMBps = result.UploadSamples.Max(s => s.MegabytesPerSecond);
            }

            StatusMessage?.Invoke("Test completed.");
            if (result.Bidirectional)
            {
                StatusMessage?.Invoke($"Download: {result.AverageDownloadMBps:F2} MB/s ({result.AverageDownloadMBps * 8:F2} Mbps)");
                StatusMessage?.Invoke($"Upload:   {result.AverageUploadMBps:F2} MB/s ({result.AverageUploadMBps * 8:F2} Mbps)");
                if (result.IsAsymmetric)
                    StatusMessage?.Invoke($"WARNING: Asymmetric speeds detected ({result.AsymmetryPercent:F1}% difference)");
            }
            else
            {
                StatusMessage?.Invoke($"Download: {result.AverageDownloadMBps:F2} MB/s ({result.AverageDownloadMBps * 8:F2} Mbps)");
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage?.Invoke("Test cancelled.");
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"Test failed: {ex.Message}");
        }

        return result;
    }

    private async Task RunDirectionAsync(byte command, bool isDownload, SpeedTestResult result, CancellationToken ct)
    {
        var bytesPerThread = new ConcurrentDictionary<int, long>();
        var tasks = new List<Task>();

        for (int i = 0; i < ThreadCount; i++)
        {
            int threadId = i;
            bytesPerThread[threadId] = 0;
            tasks.Add(Task.Run(() => RunThreadAsync(command, threadId, bytesPerThread, ct), ct));
        }

        // Sampling loop - collect throughput every 500ms
        var sw = Stopwatch.StartNew();
        long lastBytes = 0;
        double lastTime = 0;

        while (!ct.IsCancellationRequested && sw.Elapsed.TotalSeconds < DurationSeconds + 1)
        {
            await Task.Delay(500, ct).ConfigureAwait(false);

            double elapsed = sw.Elapsed.TotalSeconds;
            long totalBytes = bytesPerThread.Values.Sum();
            double intervalSeconds = elapsed - lastTime;
            long intervalBytes = totalBytes - lastBytes;

            if (intervalSeconds > 0 && intervalBytes > 0)
            {
                double mbps = (intervalBytes / (1024.0 * 1024.0)) / intervalSeconds;
                var sample = new ThroughputSample
                {
                    TimestampSeconds = elapsed,
                    MegabytesPerSecond = mbps
                };

                if (isDownload)
                {
                    lock (result.DownloadSamples) result.DownloadSamples.Add(sample);
                    ProgressUpdate?.Invoke(elapsed / DurationSeconds, mbps, 0);
                }
                else
                {
                    lock (result.UploadSamples) result.UploadSamples.Add(sample);
                    ProgressUpdate?.Invoke(elapsed / DurationSeconds, 0, mbps);
                }
            }

            lastBytes = totalBytes;
            lastTime = elapsed;
        }

        try { await Task.WhenAll(tasks); } catch { }
    }

    private async Task RunThreadAsync(byte command, int threadId,
        ConcurrentDictionary<int, long> bytesCounter, CancellationToken ct)
    {
        TcpClient? client = null;
        try
        {
            client = new TcpClient();
            client.NoDelay = true;
            client.ReceiveBufferSize = 262144;
            client.SendBufferSize = 262144;

            await client.ConnectAsync(TargetHost, Port, ct);
            var stream = client.GetStream();

            // Send header
            var header = Protocol.BuildHeader(command, ThreadCount, DurationSeconds);
            await stream.WriteAsync(header, ct);

            // Wait for ACK
            var ack = new byte[1];
            int read = await stream.ReadAsync(ack, ct);
            if (read == 0 || ack[0] != Protocol.CmdAck)
                throw new Exception("Server did not acknowledge");

            var buffer = new byte[65536];
            var deadline = DateTime.UtcNow.AddSeconds(DurationSeconds);

            if (command == Protocol.CmdStartDownload)
            {
                // Receive data from server
                while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
                {
                    int n = await stream.ReadAsync(buffer, ct);
                    if (n == 0) break;
                    bytesCounter.AddOrUpdate(threadId, n, (_, old) => old + n);
                }
            }
            else if (command == Protocol.CmdStartUpload)
            {
                // Send data to server
                Random.Shared.NextBytes(buffer);
                while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
                {
                    await stream.WriteAsync(buffer, ct);
                    bytesCounter.AddOrUpdate(threadId, buffer.Length, (_, old) => old + buffer.Length);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* thread errors are expected on shutdown */ }
        finally
        {
            client?.Dispose();
        }
    }
}
