using System.Net;
using System.Net.Http.Headers;
using UEManager.Core.Mods;

namespace UEManager.Core;

/// <summary>Downloads a file the user asked for, and checks it before anything uses it.</summary>
public static class FileDownloader
{
    /// <summary>A download that receives no data for this long stops, so a stalled server never keeps the app busy.</summary>
    public static readonly TimeSpan DefaultStallTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Downloads the file from the first of <paramref name="urls"/> that gives it whole and unchanged: copies of the
    /// same file on several hosts. Each is checked as by the one-address download.
    /// </summary>
    /// <exception cref="ModException">No address gave the file: the last one's problem.</exception>
    public static async Task<string> DownloadAsync(HttpClient http, IReadOnlyList<string> urls, string fileName, long? expectedSize,
        string? expectedSha256, string folder, string what, IProgress<double>? progress = null, CancellationToken cancellation = default,
        bool resume = false)
    {
        ModException? last = null;
        foreach (var url in urls)
        {
            try
            {
                return await DownloadAsync(http, url, fileName, expectedSize, expectedSha256, folder, what, progress, cancellation, resume: resume);
            }
            catch (ModException ex) when (!cancellation.IsCancellationRequested)
            {
                Log.Warn($"Download from '{url}' failed: {ex.Message}");
                last = ex;
                progress?.Report(0);
            }
        }
        throw last ?? new ModException($"{what} could not be downloaded: no address is set.");
    }

    /// <summary>Where a download of <paramref name="fileName"/> into <paramref name="folder"/> goes.</summary>
    public static string PathIn(string folder, string fileName) =>
        Path.Combine(folder, FileSystem.SafeName(Path.GetFileName(fileName), "download"));

    /// <summary>Added to a download's name until it is whole and checked, so a download cut off by the app's end never looks finished.</summary>
    public const string PartExtension = ".part";

    /// <summary>
    /// Downloads <paramref name="url"/> into <paramref name="folder"/> as <paramref name="fileName"/> and
    /// returns its path. A file whose size or SHA-256 differs from the expected one is deleted and refused,
    /// and so is the part of a download that failed or stalled.
    /// </summary>
    /// <param name="expectedSize">The size in bytes, or null (or 0) when it is not known.</param>
    /// <param name="what">What is downloaded, for messages: "UE4SS", or a mod's name.</param>
    /// <param name="stallTimeout">How long the download may receive no data; <see cref="DefaultStallTimeout"/> when null.</param>
    /// <param name="resume">
    /// The part of a download that was stopped, failed or stalled stays, and the next try with the same name goes on
    /// after it where the server allows that. The caller deletes a part it gives up.
    /// </param>
    public static async Task<string> DownloadAsync(HttpClient http, string url, string fileName, long? expectedSize,
        string? expectedSha256, string folder, string what, IProgress<double>? progress = null, CancellationToken cancellation = default,
        TimeSpan? stallTimeout = null, bool resume = false)
    {
        Directory.CreateDirectory(folder);
        var target = PathIn(folder, fileName);
        var part = target + PartExtension;
        var limit = stallTimeout ?? DefaultStallTimeout;
        // HttpClient's own timeout ends with the headers; this one also covers every read of the body.
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        try
        {
            stall.CancelAfter(limit);
            var have = resume && File.Exists(part) ? new FileInfo(part).Length : 0;
            var response = await GetAsync(http, url, have, stall.Token);
            if (have > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                // The part is no start of this file: the file comes whole again.
                response.Dispose();
                have = 0;
                response = await GetAsync(http, url, 0, stall.Token);
            }
            using var answer = response;
            if (!response.IsSuccessStatusCode)
                throw new ModException($"{what} could not be downloaded: the server answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            // A server that does not go on after the part sends the whole file, which then replaces the part.
            if (response.StatusCode != HttpStatusCode.PartialContent || response.Content.Headers.ContentRange?.From != have)
                have = 0;
            var total = (have > 0 ? response.Content.Headers.ContentRange?.Length ?? have + response.Content.Headers.ContentLength
                : response.Content.Headers.ContentLength) ?? expectedSize;
            await using (var input = await response.Content.ReadAsStreamAsync(stall.Token))
            await using (var output = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                var done = have;
                int read;
                stall.CancelAfter(limit);
                while ((read = await input.ReadAsync(buffer, stall.Token)) > 0)
                {
                    stall.CancelAfter(limit);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
                    done += read;
                    if (total > 0)
                        progress?.Report(Math.Min(1, (double)done / total.Value));
                }
            }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            if (!resume)
                FileSystem.TryDeleteFile(part);
            throw new ModException($"{what} could not be downloaded: no data arrived for {limit.TotalSeconds:0} seconds. Check the connection, then try again.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            if (!resume)
                FileSystem.TryDeleteFile(part);
            throw new ModException($"{what} could not be downloaded: {ex.Message}");
        }

        var size = new FileInfo(part).Length;
        if (expectedSize is > 0 && size != expectedSize)
        {
            FileSystem.TryDeleteFile(part);
            throw new ModException($"{what} was downloaded, but the file has {size:N0} bytes instead of {expectedSize:N0}, so it was not installed. Try again later.");
        }
        if (!string.IsNullOrWhiteSpace(expectedSha256)
            && !string.Equals(FileSystem.Sha256(part), expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            FileSystem.TryDeleteFile(part);
            throw new ModException($"{what} was downloaded, but the file is not the expected one (its checksum differs), so it was not installed.");
        }
        File.Move(part, target, overwrite: true);
        Log.Info($"Downloaded '{url}' to '{target}'.");
        return target;
    }

    /// <summary>Asks for the file from <paramref name="from"/> on; from its start with 0.</summary>
    private static Task<HttpResponseMessage> GetAsync(HttpClient http, string url, long from, CancellationToken cancellation)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (from > 0)
            request.Headers.Range = new RangeHeaderValue(from, null);
        return http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
    }
}
