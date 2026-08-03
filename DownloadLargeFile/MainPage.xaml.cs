using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;

namespace DownloadLargeFile
{
    public partial class MainPage : ContentPage
    {
        CancellationTokenSource? _cts;
        string Mymessage { get; set; }
        public MainPage()
        {
            InitializeComponent();
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
            ProgressLabelText.Text = "Downloading...";

            _cts = new CancellationTokenSource();

            try
            {
                var fileName = Path.GetFileName(new Uri(url).LocalPath);
                if (string.IsNullOrEmpty(fileName)) fileName = "download.bin";
                var dest = Path.Combine(FileSystem.AppDataDirectory, fileName);

                await DownloadFileAsync(url, dest, _cts.Token, percent =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        ProgressBar.Progress = percent / 100.0;
                        ProgressLabel.Text = $"{percent:F1}%";
                        ProgressLabelText.Text = Mymessage;
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

        private async Task DownloadFileAsync(string url, string destinationPath, CancellationToken token, Action<double> progress)
        {
            using var http = new HttpClient();
            // Request headers only so we can stream
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength;

            await using var contentStream = await response.Content.ReadAsStreamAsync(token);
            await using var fileStream = File.Create(destinationPath);

            var buffer = new byte[81920];
            long totalRead = 0;
            int read;
            while ((read = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), token)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), token);
                totalRead += read;

                if (contentLength.HasValue && contentLength.Value > 0)
                {
                    double percent = (double)totalRead / contentLength.Value * 100.0;
                    Mymessage = $"  Downloading... {FormatBytes(totalRead)}/{FormatBytes(contentLength.Value)}";
                    progress(percent);
                }
                else
                {
                    // Unknown total length
                    Mymessage = "Downloading... (unknown size)";
                    progress(0);
                }
            }
        }

        public string FormatBytes(long bytes)
        {
            if (bytes < 0)
                throw new ArgumentOutOfRangeException(nameof(bytes), "Byte value cannot be negative.");

            string[] units = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
            double size = bytes;
            int unitIndex = 0;

            // Keep dividing until size is less than 1024 or we reach the largest unit
            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return $"{size:F2} {units[unitIndex]}";
        }
    }
}
