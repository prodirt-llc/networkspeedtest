# Network Speed Test Tool

A professional WPF-based network diagnostic tool for testing bandwidth, latency, jitter, and detecting network asymmetry issues. Built for IT professionals and MSPs to quickly diagnose network performance problems.

![Network Speed Test](https://img.shields.io/badge/platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-6.0+-purple)
![License](https://img.shields.io/badge/license-MIT-green)

## Features

### 🚀 Throughput Testing
- **Multi-threaded speed tests** - Saturate 10G/25G/40G+ links
- **Bidirectional mode** - Test upload and download simultaneously
- **Real-time statistics** - Live MB/s and Mbps display
- **Asymmetry detection** - Automatically warns when upload/download speeds differ >20%
- **Configurable parameters** - Threads, duration, port, buffer size

### 📊 Latency & Jitter Testing
- **RTT measurement** - Min/avg/max round-trip time
- **Jitter calculation** - Inter-packet variation for VoIP troubleshooting
- **Packet loss detection** - Identify network reliability issues
- **Color-coded results** - Green/yellow/red status indicators

### 📈 Professional Reporting
- **HTML reports** - Beautiful, customer-ready diagnostic reports
- **Interactive graphs** - Chart.js visualizations of throughput over time
- **At-a-glance verdict** - "Network Healthy" or "Issues Detected"
- **Dual graph views** - Both Mbps and MB/s representations
- **Asymmetry highlighting** - Red warnings for NIC/driver issues

### 🎯 Use Cases
- **Bandwidth validation** - Verify 10G/25G/40G infrastructure performance
- **NIC troubleshooting** - Detect driver bugs and buffer tuning issues
- **VoIP diagnostics** - Measure jitter and packet loss for phone systems
- **Switch CPU issues** - Identify forwarding plane problems
- **Customer proof** - Professional reports showing network performance

## Installation

### Download Release (Recommended)
1. Download the latest `NetworkSpeedTest.exe` from [Releases](../../releases)
2. Copy to any Windows 10/11 machine
3. Run - no installation required
4. **File size:** ~70MB (self-contained, no dependencies)

### Build from Source
**Requirements:**
- Visual Studio 2022 or later
- .NET 6.0 SDK or later

**Steps:**
```bash
git clone https://github.com/yourusername/network-speed-test.git
cd network-speed-test
dotnet restore
dotnet build -c Release
```

**Publish as single executable:**
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Output: `bin\Release\net6.0\win-x64\publish\NetworkSpeedTest.exe`

## Quick Start

### Running a Speed Test

**On Server Machine:**
1. Launch `NetworkSpeedTest.exe`
2. Click **Server Mode** tab
3. Click **Start Test**
4. Server listens on port 5201

**On Client Machine:**
1. Launch `NetworkSpeedTest.exe`
2. Click **Client Mode** tab
3. Enter server IP address
4. Check **Bidirectional** (recommended)
5. Click **Start Test**
6. Wait for test to complete
7. Click **Generate Report** for HTML output

### Running a Latency Test

1. Click **Latency Test** tab
2. Enter target IP address
3. Set duration (e.g., 300 seconds for 5 minutes)
4. Click **Start Latency Test**
5. View real-time RTT, jitter, and packet loss
6. Click **Generate Report** for detailed analysis

## Configuration

### Optimal Settings by Link Speed

| Link Speed | Threads | Buffer Size |
|-----------|---------|-------------|
| 1 Gbps    | 4       | 128 KB      |
| 10 Gbps   | 8       | 256 KB      |
| 25 Gbps   | 12      | 512 KB      |
| 40 Gbps   | 16      | 512 KB      |
| 100 Gbps  | 32      | 1024 KB     |

### Firewall Configuration

**Windows Firewall:**
```powershell
New-NetFirewallRule -DisplayName "Network Speed Test Server" -Direction Inbound -Protocol TCP -LocalPort 5201 -Action Allow
```

## Interpreting Results

### Asymmetry Detection
- **<10% difference** → Normal, network is healthy ✅
- **10-20% difference** → Minor asymmetry, investigate if causing issues ⚠️
- **>20% difference** → Significant problem, likely NIC/driver issue ❌

**Example:**
- Download: 9.2 Gbps
- Upload: 7.5 Gbps
- Asymmetry: 18% → Check NIC offload settings

### Common Causes of Asymmetry
1. **NIC offload settings** - TCP Checksum, Large Send Offload (LSO), Receive Side Scaling (RSS)
2. **Outdated drivers** - Especially Realtek, older Intel/Broadcom drivers
3. **Buffer mismatches** - Send vs receive buffer configuration
4. **Flow control** - Switch-level pause frame issues

### Latency Interpretation

| Metric | Good | Acceptable | Poor |
|--------|------|------------|------|
| **Latency** | <30ms | 30-100ms | >100ms |
| **Jitter** | <10ms | 10-30ms | >30ms |
| **Packet Loss** | 0% | <0.5% | >1% |

**For VoIP:** Keep latency <30ms, jitter <10ms, packet loss <0.1%

## Troubleshooting

### "Cannot connect to target"
- Verify server is running in Server mode
- Check firewall rules on both machines
- Confirm port 5201 is not blocked
- Test connectivity: `Test-NetConnection -ComputerName <target> -Port 5201`

### Low speeds on high-speed links
- Increase thread count (try 12-16 threads)
- Increase buffer size (512 KB or 1024 KB)
- Check for network bottlenecks (switches, cables, NICs)
- Disable antivirus temporarily for testing
- Verify NIC is running at full speed (check link status)

### Asymmetric performance detected

**Step 1: Check NIC offload settings (both machines):**
```powershell
Get-NetAdapterAdvancedProperty -Name "Ethernet*" | Where-Object {$_.DisplayName -like "*Offload*"}
```

**Step 2: Update NIC drivers:**
```powershell
Get-NetAdapter | Select-Object Name, DriverVersion, DriverDate
```
Download latest from manufacturer website.

**Step 3: Try different buffer sizes:**
```powershell
# Test with 128 KB, 256 KB, 512 KB, 1024 KB
```

**Step 4: Reset TCP/IP stack (last resort):**
```powershell
netsh int ip reset
netsh winsock reset
# Reboot required
```

## Real-World Examples

### Example 1: 10G Link Validation
**Scenario:** Customer claims new 10G connection is slow

**Test Results:**
- Download: 9.2 Gbps ✅
- Upload: 9.1 Gbps ✅
- Asymmetry: 1% ✅
- Latency: 0.5ms ✅

**Conclusion:** Network is performing perfectly. Problem is elsewhere (disk I/O, application, etc.)

### Example 2: VoIP Quality Issues
**Scenario:** Phone calls sound choppy, dropping

**Test Results:**
- Bandwidth: 950 Mbps ✅
- Latency: 85ms ⚠️
- Jitter: 45ms ❌
- Packet Loss: 2.3% ❌

**Conclusion:** Switch CPU overload or congestion. Check switch forwarding plane, upgrade switch, or implement QoS.

### Example 3: Asymmetric NIC Issue
**Scenario:** File uploads to server are slow, downloads are fast

**Test Results:**
- Download: 9.2 Gbps ✅
- Upload: 2.1 Gbps ❌
- Asymmetry: 77% ❌

**Conclusion:** Severe NIC asymmetry. Updated Intel driver from v12 to v25. Retest: Both directions now 9+ Gbps ✅

## Technical Details

### Architecture
- **Language:** C# (.NET 6.0)
- **UI Framework:** WPF (Windows Presentation Foundation)
- **Networking:** Native TCP sockets with async/await
- **Threading:** Real multi-threading (no PowerShell jobs)
- **Reporting:** HTML + Chart.js for interactive graphs

### Protocol
- **Transport:** TCP for reliability
- **Default Port:** 5201 (configurable)
- **Server:** Sends continuous data stream
- **Client:** Receives and measures throughput
- **Bidirectional:** Simultaneous send/receive on same connections
- **Latency:** TCP echo with timestamp measurement

### Performance
- **Tested up to:** 100 Gbps localhost
- **Typical overhead:** <1% CPU per thread
- **Memory usage:** ~50-100MB
- **Report generation:** <1 second

## Deployment

### ConnectWise Backstage
1. Upload `NetworkSpeedTest.exe` to software repository
2. Deploy to technician machines
3. No installation or admin rights required
4. Reports save to configurable location (default: `Desktop\SpeedTestReports`)

### Intune/Group Policy
```powershell
# Copy to Program Files
Copy-Item "NetworkSpeedTest.exe" "C:\Program Files\NetworkTools\"

# Create desktop shortcut
$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut("$env:Public\Desktop\Network Speed Test.lnk")
$Shortcut.TargetPath = "C:\Program Files\NetworkTools\NetworkSpeedTest.exe"
$Shortcut.Save()
```

### Portable USB Deployment
Simply copy `NetworkSpeedTest.exe` to a USB drive. Run from any Windows 10/11 machine with no installation.

## Comparison to Other Tools

| Feature | This Tool | iperf3 | PingPlotter | Commercial Tools |
|---------|-----------|--------|-------------|------------------|
| GUI | ✅ | ❌ | ✅ | ✅ |
| Asymmetry Detection | ✅ | ❌ | ❌ | Some |
| Combined Speed+Latency | ✅ | ❌ | ❌ | Rare |
| HTML Reports | ✅ | ❌ | ✅ | ✅ |
| Windows Native | ✅ | ❌ | ✅ | ✅ |
| Free | ✅ | ✅ | ❌ | ❌ |
| Single .exe | ✅ | ❌ | ❌ | Varies |
| Customer-Ready Output | ✅ | ❌ | ✅ | ✅ |

## Contributing

Contributions are welcome! Please:
1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License

This project is Dual licensed under the AGPL v3 + Commercial Exception - see the [AGPL v3](https://www.gnu.org/licenses/agpl-3.0.en.html) file for details.

## Acknowledgments

- Built with [Claude Code](https://www.anthropic.com/claude) by Anthropic
- Chart.js for beautiful graphs
- Inspired by iperf, but focused on real-world IT troubleshooting
- Icon design optimized for professional MSP deployment

## Support

- **Issues:** [GitHub Issues](../../issues)
- **Documentation:** [Wiki](../../wiki)
- **Discussions:** [GitHub Discussions](../../discussions)

---

**⭐ If this tool saves you time troubleshooting networks, give it a star!**

**Built for MSPs and IT professionals who need fast, reliable, customer-ready network diagnostics.**
