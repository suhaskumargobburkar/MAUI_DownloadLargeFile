using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Networking;

namespace DownloadLargeFile
{
    public partial class MainPage : ContentPage
    {
        CancellationTokenSource? _cts;

        public MainPage()
        {
            InitializeComponent();

            // Subscribe to connectivity changes
            Connectivity.ConnectivityChanged += OnConnectivityChanged;
            UpdateConnectivityStatus();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            Connectivity.ConnectivityChanged -= OnConnectivityChanged;
        }

        private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(UpdateConnectivityStatus);
        }

        void UpdateConnectivityStatus()
        {
            var access = Connectivity.Current.NetworkAccess;
            var profiles = Connectivity.Current.ConnectionProfiles;

            ConnectionStatusLabel.Text = access.ToString() + (profiles != null ? $" ({string.Join(", ", profiles)})" : string.Empty);
            SpeedLabel.Text = "Speed: N/A";
        }

        private async void OnDownloadClicked(object? sender, EventArgs e)
        {
            var url = UrlEntry?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                await DisplayAlert("Error", "Please enter a download URL.", "OK");
                return;
            }

            DownloadBtn.IsEnabled = false;
            CancelBtn.IsEnabled = true;
            ProgressBar.Progress = 0;
            ProgressLabel.Text = "0%";
            SpeedLabel.Text = "Measuring...";

            _cts = new CancellationTokenSource();

            try
            {
                var fileName = Path.GetFileName(new Uri(url).LocalPath);
                if (string.IsNullOrEmpty(fileName)) fileName = "download.bin";
                var dest = Path.Combine(FileSystem.AppDataDirectory, fileName);

                await DownloadFileAsync(url, dest, _cts.Token,
                    percent =>
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            ProgressBar.Progress = percent / 100.0;
                            ProgressLabel.Text = $"{percent:F1}%";
                        });
                    },
                    mbps =>
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            if (mbps <= 0)
                            {
                                SpeedLabel.Text = "Speed: N/A";
                            }
                            else
                            {
                                double kbps = mbps * 1024.0; // using 1024 base consistent with calculation
                                var quality = GetQualityLabel(mbps);
                                SpeedLabel.Text = $"{kbps:F2} kbps / {mbps:F2} Mbps ({quality})";
                            }
                        });
                    });

                await DisplayAlert("Completed", $"File saved to:\n{dest}", "OK");
            }
            catch (OperationCanceledException)
            {
                await DisplayAlert("Canceled", "Download was canceled.", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
            finally
            {
                DownloadBtn.IsEnabled = true;
                CancelBtn.IsEnabled = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void OnCancelClicked(object? sender, EventArgs e)
        {
            _cts?.Cancel();
        }

        private async Task DownloadFileAsync(string url, string destinationPath, CancellationToken token, Action<double> progressPercent, Action<double> progressSpeed)
        {
            using var http = new HttpClient();
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength;

            await using var contentStream = await response.Content.ReadAsStreamAsync(token);
            await using var fileStream = File.Create(destinationPath);

            var buffer = new byte[81920];
            long totalRead = 0;
            int read;

            var sw = Stopwatch.StartNew();
            long lastBytes = 0;
            var lastTime = sw.Elapsed;

            while ((read = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), token)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), token);
                totalRead += read;

                if (contentLength.HasValue && contentLength.Value > 0)
                {
                    double percent = (double)totalRead / contentLength.Value * 100.0;
                    progressPercent(percent);
                }
                else
                {
                    progressPercent(0);
                }

                // Compute instantaneous speed every loop
                var now = sw.Elapsed;
                var elapsedSinceLast = (now - lastTime).TotalSeconds;
                if (elapsedSinceLast >= 0.5)
                {
                    var bytesSinceLast = totalRead - lastBytes;
                    double bytesPerSecond = bytesSinceLast / elapsedSinceLast;
                    double mbps = bytesPerSecond * 8.0 / (1024 * 1024);
                    progressSpeed(mbps);

                    lastBytes = totalRead;
                    lastTime = now;
                }
            }

            // final speed calculation
            var totalSeconds = sw.Elapsed.TotalSeconds;
            if (totalSeconds > 0)
            {
                double bytesPerSecond = totalRead / totalSeconds;
                double mbps = bytesPerSecond * 8.0 / (1024 * 1024);
                progressSpeed(mbps);
            }
        }

        static string GetQualityLabel(double mbps)
        {
            if (mbps <= 0) return "Unknown";
            if (mbps < 0.5) return "Very poor";
            if (mbps < 1) return "Poor";
            if (mbps < 5) return "Fair";
            if (mbps < 20) return "Good";
            return "Excellent";
        }

        public string FormatBytes(long bytes)
        {
            if (bytes < 0)
                throw new ArgumentOutOfRangeException(nameof(bytes), "Byte value cannot be negative.");

            string[] units = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
            double size = bytes;
            int unitIndex = 0;

            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return $"{size:F2} {units[unitIndex]}";
        }
    }
}
