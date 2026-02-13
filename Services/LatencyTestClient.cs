using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NetworkSpeedTest.Models;

namespace NetworkSpeedTest.Services;

public class LatencyTestClient
{
    public string TargetHost { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 5201;
    public int DurationSeconds { get; set; } = 10;

    private CancellationTokenSource? _cts;

    public event Action<string>? StatusMessage;
    /// <summary>progress (0-1), currentRttMs, runningJitterMs, lossPercent</summary>
    public event Action<double, double, double, double>? LatencyUpdate;

    public void Cancel() => _cts?.Cancel();

    public async Task<LatencyTestResult> RunAsync()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var result = new LatencyTestResult
        {
            TargetHost = TargetHost,
            Port = Port,
            DurationSeconds = DurationSeconds,
            TestTime = DateTime.Now
        };

        TcpClient? client = null;
        try
        {
            StatusMessage?.Invoke($"Connecting to {TargetHost}:{Port} for latency test...");

            client = new TcpClient { NoDelay = true };
            await client.ConnectAsync(TargetHost, Port, ct);
            var stream = client.GetStream();

            // Send header: latency ping mode
            var header = Protocol.BuildHeader(Protocol.CmdLatencyPing, 1, DurationSeconds);
            await stream.WriteAsync(header, ct);

            // Wait for ACK
            var ack = new byte[1];
            int ackRead = await stream.ReadAsync(ack, ct);
            if (ackRead == 0 || ack[0] != Protocol.CmdAck)
                throw new Exception("Server did not acknowledge latency test");

            StatusMessage?.Invoke("Latency test running...");

            var sw = Stopwatch.StartNew();
            var deadline = TimeSpan.FromSeconds(DurationSeconds);
            double runningJitter = 0;
            double lastRtt = -1;
            var sendBuf = new byte[8];
            var recvBuf = new byte[8];

            while (sw.Elapsed < deadline && !ct.IsCancellationRequested)
            {
                // Write current Stopwatch ticks as the ping payload
                long sendTicks = sw.ElapsedTicks;
                BitConverter.GetBytes(sendTicks).CopyTo(sendBuf, 0);
                result.Sent++;

                try
                {
                    await stream.WriteAsync(sendBuf, ct);
                    await stream.FlushAsync(ct);

                    // Read echo with a timeout
                    using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    readCts.CancelAfter(2000); // 2s timeout per ping

                    int read = 0;
                    while (read < 8)
                    {
                        int n = await stream.ReadAsync(recvBuf.AsMemory(read, 8 - read), readCts.Token);
                        if (n == 0) goto done;
                        read += n;
                    }

                    long echoTicks = BitConverter.ToInt64(recvBuf, 0);
                    long nowTicks = sw.ElapsedTicks;
                    double rttMs = (nowTicks - echoTicks) / (double)Stopwatch.Frequency * 1000.0;

                    result.Received++;
                    double elapsed = sw.Elapsed.TotalSeconds;

                    result.Samples.Add(new LatencySample
                    {
                        TimestampSeconds = elapsed,
                        RoundTripMs = rttMs
                    });

                    // RFC 3550 jitter: smoothed absolute difference
                    if (lastRtt >= 0)
                    {
                        double diff = Math.Abs(rttMs - lastRtt);
                        runningJitter += (diff - runningJitter) / 16.0;
                    }
                    lastRtt = rttMs;

                    double progress = elapsed / DurationSeconds;
                    double lossPercent = result.Sent > 0 ? ((result.Sent - result.Received) / (double)result.Sent) * 100.0 : 0;
                    LatencyUpdate?.Invoke(progress, rttMs, runningJitter, lossPercent);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Individual ping timeout = packet loss
                    StatusMessage?.Invoke($"Ping {result.Sent} timed out");
                }

                // Wait ~100ms between pings
                try { await Task.Delay(100, ct); } catch (OperationCanceledException) { break; }
            }

            done:
            StatusMessage?.Invoke("Latency test completed.");
            StatusMessage?.Invoke($"  Sent: {result.Sent}  Received: {result.Received}  Loss: {result.PacketLossPercent:F1}%");
            StatusMessage?.Invoke($"  RTT min/avg/max: {result.MinRttMs:F2}/{result.AvgRttMs:F2}/{result.MaxRttMs:F2} ms");
            StatusMessage?.Invoke($"  Jitter: {result.JitterMs:F2} ms");
        }
        catch (OperationCanceledException)
        {
            StatusMessage?.Invoke("Latency test cancelled.");
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"Latency test failed: {ex.Message}");
        }
        finally
        {
            client?.Dispose();
        }

        return result;
    }
}
