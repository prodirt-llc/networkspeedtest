using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NetworkSpeedTest.Models;

namespace NetworkSpeedTest.Services;

public class SpeedTestServer
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly List<Task> _activeSessions = new();

    public event Action<string>? StatusMessage;
    public int Port { get; set; } = 5201;

    public async Task StartAsync()
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, Port);
        _listener.Start();
        StatusMessage?.Invoke($"Server listening on port {Port}");

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                var endpoint = client.Client.RemoteEndPoint as IPEndPoint;
                StatusMessage?.Invoke($"Client connected from {endpoint?.Address}:{endpoint?.Port}");
                var session = HandleClientAsync(client, _cts.Token);
                lock (_activeSessions) _activeSessions.Add(session);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"Server error: {ex.Message}");
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        StatusMessage?.Invoke("Server stopped");
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using (client)
            {
                client.NoDelay = true;
                client.ReceiveBufferSize = 262144;
                client.SendBufferSize = 262144;
                var stream = client.GetStream();

                var header = new byte[Protocol.HeaderSize];
                int read = 0;
                while (read < Protocol.HeaderSize)
                {
                    int n = await stream.ReadAsync(header.AsMemory(read, Protocol.HeaderSize - read), ct);
                    if (n == 0) return;
                    read += n;
                }

                var (cmd, threads, duration) = Protocol.ParseHeader(header);
                StatusMessage?.Invoke($"Request: cmd={cmd}, threads={threads}, duration={duration}s");

                // Send ACK
                await stream.WriteAsync(new[] { Protocol.CmdAck }, ct);

                switch (cmd)
                {
                    case Protocol.CmdStartDownload:
                        // Client wants to download = server sends data
                        await SendDataAsync(stream, duration, ct);
                        break;
                    case Protocol.CmdStartUpload:
                        // Client wants to upload = server receives data
                        await ReceiveDataAsync(stream, duration, ct);
                        break;
                    case Protocol.CmdStartBidirectional:
                        // Both directions simultaneously
                        var sendTask = SendDataAsync(stream, duration, ct);
                        var recvTask = ReceiveDataAsync(stream, duration, ct);
                        await Task.WhenAll(sendTask, recvTask);
                        break;
                    case Protocol.CmdLatencyPing:
                        await HandleLatencyEchoAsync(stream, duration, ct);
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage?.Invoke($"Session error: {ex.Message}");
        }
        finally
        {
            lock (_activeSessions) _activeSessions.Remove(Task.CompletedTask);
        }
    }

    private async Task SendDataAsync(NetworkStream stream, int durationSeconds, CancellationToken ct)
    {
        var buffer = new byte[65536];
        Random.Shared.NextBytes(buffer);
        var deadline = DateTime.UtcNow.AddSeconds(durationSeconds);
        long totalSent = 0;

        try
        {
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                await stream.WriteAsync(buffer, ct);
                totalSent += buffer.Length;
            }
        }
        catch (Exception) { /* client may disconnect */ }

        double mb = totalSent / (1024.0 * 1024.0);
        StatusMessage?.Invoke($"Sent {mb:F1} MB to client");
    }

    private async Task ReceiveDataAsync(NetworkStream stream, int durationSeconds, CancellationToken ct)
    {
        var buffer = new byte[65536];
        var deadline = DateTime.UtcNow.AddSeconds(durationSeconds);
        long totalReceived = 0;

        try
        {
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) break;
                readCts.CancelAfter(remaining.Add(TimeSpan.FromSeconds(2)));

                int n = await stream.ReadAsync(buffer, readCts.Token);
                if (n == 0) break;
                totalReceived += n;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }

        double mb = totalReceived / (1024.0 * 1024.0);
        StatusMessage?.Invoke($"Received {mb:F1} MB from client");
    }

    private async Task HandleLatencyEchoAsync(NetworkStream stream, int durationSeconds, CancellationToken ct)
    {
        var buf = new byte[8];
        var deadline = DateTime.UtcNow.AddSeconds(durationSeconds);
        int echoed = 0;

        StatusMessage?.Invoke($"Latency echo mode for {durationSeconds}s");

        try
        {
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                int read = 0;
                while (read < 8)
                {
                    using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero) goto done;
                    readCts.CancelAfter(remaining.Add(TimeSpan.FromSeconds(2)));

                    int n = await stream.ReadAsync(buf.AsMemory(read, 8 - read), readCts.Token);
                    if (n == 0) goto done;
                    read += n;
                }

                await stream.WriteAsync(buf, ct);
                await stream.FlushAsync(ct);
                echoed++;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }

        done:
        StatusMessage?.Invoke($"Latency echo complete: {echoed} pings echoed");
    }
}
