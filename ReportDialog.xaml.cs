using System.Windows;

namespace NetworkSpeedTest;

public partial class ReportDialog : Window
{
    public bool IncludeSpeed { get; private set; }
    public bool IncludeLatency { get; private set; }

    public ReportDialog(bool hasSpeedResult, bool hasLatencyResult)
    {
        InitializeComponent();

        IncludeSpeedCheck.IsEnabled = hasSpeedResult;
        IncludeSpeedCheck.IsChecked = hasSpeedResult;
        if (!hasSpeedResult)
            IncludeSpeedCheck.Content = "Include Speed Test Results (no data)";

        IncludeLatencyCheck.IsEnabled = hasLatencyResult;
        IncludeLatencyCheck.IsChecked = hasLatencyResult;
        if (!hasLatencyResult)
            IncludeLatencyCheck.Content = "Include Latency Test Results (no data)";

        // Disable Generate if nothing available
        GenerateBtn.IsEnabled = hasSpeedResult || hasLatencyResult;
    }

    private void GenerateBtn_Click(object sender, RoutedEventArgs e)
    {
        IncludeSpeed = IncludeSpeedCheck.IsChecked == true;
        IncludeLatency = IncludeLatencyCheck.IsChecked == true;

        if (!IncludeSpeed && !IncludeLatency)
        {
            MessageBox.Show("Select at least one section to include.", "Nothing Selected",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
