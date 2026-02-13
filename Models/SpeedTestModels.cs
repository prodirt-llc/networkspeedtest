using System;
using System.Collections.Generic;

namespace NetworkSpeedTest.Models;

public class ThroughputSample
{
    public double TimestampSeconds { get; set; }
    public double MegabytesPerSecond { get; set; }
    public double MegabitsPerSecond => MegabytesPerSecond * 8.0;
}

public class SpeedTestResult
{
    public List<ThroughputSample> DownloadSamples { get; } = new();
    public List<ThroughputSample> UploadSamples { get; } = new();
    public double AverageDownloadMBps { get; set; }
    public double AverageUploadMBps { get; set; }
    public double PeakDownloadMBps { get; set; }
    public double PeakUploadMBps { get; set; }
    public int ThreadCount { get; set; }
    public int DurationSeconds { get; set; }
    public bool Bidirectional { get; set; }
    public string TargetHost { get; set; } = "";
    public int Port { get; set; }
    public DateTime TestTime { get; set; } = DateTime.Now;

    public double AsymmetryPercent
    {
        get
        {
            if (!Bidirectional || AverageDownloadMBps == 0 || AverageUploadMBps == 0)
                return 0;
            double max = Math.Max(AverageDownloadMBps, AverageUploadMBps);
            double min = Math.Min(AverageDownloadMBps, AverageUploadMBps);
            return ((max - min) / max) * 100.0;
        }
    }

    public bool IsAsymmetric => AsymmetryPercent > 20.0;
}

public class LatencySample
{
    public double TimestampSeconds { get; set; }
    public double RoundTripMs { get; set; }
}

public class LatencyTestResult
{
    public List<LatencySample> Samples { get; } = new();
    public int Sent { get; set; }
    public int Received { get; set; }
    public string TargetHost { get; set; } = "";
    public int Port { get; set; }
    public int DurationSeconds { get; set; }
    public DateTime TestTime { get; set; } = DateTime.Now;

    public double MinRttMs => Samples.Count > 0 ? Samples.Min(s => s.RoundTripMs) : 0;
    public double AvgRttMs => Samples.Count > 0 ? Samples.Average(s => s.RoundTripMs) : 0;
    public double MaxRttMs => Samples.Count > 0 ? Samples.Max(s => s.RoundTripMs) : 0;

    public double PacketLossPercent => Sent > 0 ? ((Sent - Received) / (double)Sent) * 100.0 : 0;

    /// <summary>RFC 3550-style jitter: mean of absolute differences between consecutive RTT samples.</summary>
    public double JitterMs
    {
        get
        {
            if (Samples.Count < 2) return 0;
            double sum = 0;
            for (int i = 1; i < Samples.Count; i++)
                sum += Math.Abs(Samples[i].RoundTripMs - Samples[i - 1].RoundTripMs);
            return sum / (Samples.Count - 1);
        }
    }
}

public static class Protocol
{
    public const byte CmdStartDownload = 0x01;
    public const byte CmdStartUpload = 0x02;
    public const byte CmdStartBidirectional = 0x03;
    public const byte CmdStop = 0x04;
    public const byte CmdAck = 0x05;
    public const byte CmdLatencyPing = 0x06;

    public const int HeaderSize = 9; // 1 byte cmd + 4 bytes threads + 4 bytes duration

    public static byte[] BuildHeader(byte command, int threads, int durationSeconds)
    {
        var buf = new byte[HeaderSize];
        buf[0] = command;
        BitConverter.GetBytes(threads).CopyTo(buf, 1);
        BitConverter.GetBytes(durationSeconds).CopyTo(buf, 5);
        return buf;
    }

    public static (byte command, int threads, int duration) ParseHeader(byte[] buf)
    {
        byte cmd = buf[0];
        int threads = BitConverter.ToInt32(buf, 1);
        int duration = BitConverter.ToInt32(buf, 5);
        return (cmd, threads, duration);
    }
}
