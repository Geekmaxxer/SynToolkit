#nullable enable

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SynToolkit.Services
{
    /// <summary>
    /// Downloads a file while reporting progress. Ported from AME.AppFetch's HttpProgressClient
    /// (https://github.com/Ameliorated-LLC/appfetch, MIT License, Copyright (c) Ameliorated LLC).
    /// </summary>
    public sealed class AppFetchHttpProgressClient : IDisposable
    {
        private string _destinationFilePath = null!;

        private readonly HttpClient _client = new() { Timeout = TimeSpan.FromDays(1) };

        public delegate void ProgressChangedHandler(long? totalFileSize, long totalBytesDownloaded, double? progressPercentage);

        public event ProgressChangedHandler? ProgressChanged;

        public async Task StartDownload(string downloadUrl, string destinationFilePath, long? size = null, CancellationToken cancellationToken = default)
        {
            _destinationFilePath = destinationFilePath;

            for (int i = 0; i < 3; i++)
            {
                await Task.Delay(1000 * i, cancellationToken);
                using HttpResponseMessage response = await _client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    continue;
                }

                if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
                {
                    i = 2;
                    continue;
                }

                await DownloadFileFromHttpResponseMessage(response, size, cancellationToken);
                return;
            }

            throw new Exception("Unexpected end of StartDownload.");
        }

        private async Task DownloadFileFromHttpResponseMessage(HttpResponseMessage response, long? size, CancellationToken cancellationToken)
        {
            response.EnsureSuccessStatusCode();

            if (!response.Content.Headers.ContentLength.HasValue)
            {
                size = response.Content.Headers.ContentLength;
            }

            using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await ProcessContentStream(size, contentStream, cancellationToken);
        }

        private async Task ProcessContentStream(long? totalDownloadSize, Stream contentStream, CancellationToken cancellationToken)
        {
            long totalBytesRead = 0;
            long readCount = 0;
            byte[] buffer = new byte[8192];
            bool isMoreToRead = true;

            using FileStream fileStream = new(_destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
            do
            {
                int bytesRead = await contentStream.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0)
                {
                    isMoreToRead = false;
                    TriggerProgressChanged(totalDownloadSize, totalBytesRead);
                    continue;
                }

                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);

                totalBytesRead += bytesRead;
                readCount += 1;

                if (readCount % 50 == 0)
                {
                    TriggerProgressChanged(totalDownloadSize, totalBytesRead);
                }
            } while (isMoreToRead);
        }

        private void TriggerProgressChanged(long? totalDownloadSize, long totalBytesRead)
        {
            if (ProgressChanged == null)
            {
                return;
            }

            double? progressPercentage = totalDownloadSize.HasValue
                ? Math.Min(Math.Round((double)totalBytesRead / totalDownloadSize.Value * 100, 2), 100)
                : null;

            ProgressChanged(totalDownloadSize, totalBytesRead, progressPercentage);
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}
