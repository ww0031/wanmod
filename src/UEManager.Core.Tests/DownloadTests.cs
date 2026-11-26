using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using UEManager.Core.Mods;

namespace UEManager.Core.Tests;

public class DownloadTests
{
    [Fact]
    public async Task A_server_that_stops_sending_ends_the_download()
    {
        using var folder = new TestFolder();
        using var http = new HttpClient(new StallingServer());
        var watch = Stopwatch.StartNew();

        var error = await Assert.ThrowsAsync<ModException>(() => FileDownloader.DownloadAsync(http, "https://example.test/mod.zip",
            "mod.zip", null, null, folder.Combine("downloads"), "Mod", stallTimeout: TimeSpan.FromMilliseconds(200)));

        Assert.Contains("no data arrived", error.Message);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"took {watch.Elapsed}");
        Assert.Empty(Directory.GetFiles(folder.Combine("downloads")));
    }

    private static readonly byte[] Whole = [10, 11, 12, 13, 14, 15, 16, 17, 18, 19];

    [Fact]
    public async Task A_download_that_stopped_goes_on_after_its_part()
    {
        using var folder = new TestFolder();
        var server = new RangeServer(Whole) { StallAfter = 4 };
        using var http = new HttpClient(server);
        Task<string> Download() => FileDownloader.DownloadAsync(http, "https://example.test/mod.zip", "mod.zip", Whole.Length, null,
            folder.Combine("downloads"), "Mod", stallTimeout: TimeSpan.FromMilliseconds(200), resume: true);

        await Assert.ThrowsAsync<ModException>(Download);

        Assert.Equal(Whole[..4], File.ReadAllBytes(folder.Combine("downloads", "mod.zip.part")));
        server.StallAfter = -1;
        var file = await Download();

        Assert.Equal([null, 4], server.Asked);
        Assert.Equal(Whole, File.ReadAllBytes(file));
        Assert.Equal(["mod.zip"], Directory.GetFiles(folder.Combine("downloads")).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData(false, 4)]  // the server sends the whole file whatever is asked
    [InlineData(true, 40)]  // the part is longer than the file, so it is no part of it
    public async Task A_part_the_server_does_not_go_on_after_is_replaced_by_the_whole_file(bool ranges, int partSize)
    {
        using var folder = new TestFolder();
        folder.File(@"downloads\mod.zip.part", new string('x', partSize));
        using var http = new HttpClient(new RangeServer(Whole, ranges));

        var file = await FileDownloader.DownloadAsync(http, "https://example.test/mod.zip", "mod.zip", null, null,
            folder.Combine("downloads"), "Mod", resume: true);

        Assert.Equal(Whole, File.ReadAllBytes(file));
    }

    [Fact]
    public async Task A_part_is_not_gone_on_after_unless_asked()
    {
        using var folder = new TestFolder();
        folder.File(@"downloads\mod.zip.part", "xxxx");
        var server = new RangeServer(Whole);
        using var http = new HttpClient(server);

        var file = await FileDownloader.DownloadAsync(http, "https://example.test/mod.zip", "mod.zip", null, null, folder.Combine("downloads"), "Mod");

        Assert.Equal([null], server.Asked);
        Assert.Equal(Whole, File.ReadAllBytes(file));
    }

    /// <summary>Serves a file, from the byte asked for when it knows ranges. It stalls after <see cref="StallAfter"/> bytes of an answer, unless that is negative.</summary>
    private sealed class RangeServer(byte[] content, bool ranges = true) : HttpMessageHandler
    {
        /// <summary>The first byte each request asked for; null for a request for the whole file.</summary>
        public List<long?> Asked { get; } = [];

        public int StallAfter { get; set; } = -1;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var asked = request.Headers.Range?.Ranges.Single().From;
            Asked.Add(asked);
            var from = ranges ? asked ?? 0 : 0;
            if (from >= content.Length)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable));
            var body = content[(int)from..];
            var response = new HttpResponseMessage(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new StreamContent(StallAfter < 0 ? new MemoryStream(body) : new StallingStream(body[..StallAfter])),
            };
            if (from > 0)
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, content.Length - 1, content.Length);
            return Task.FromResult(response);
        }
    }

    /// <summary>Answers with headers and a few bytes, then sends nothing more until the request is cancelled.</summary>
    private sealed class StallingServer : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream([1])) });
    }

    /// <summary>Gives its bytes in one read, then nothing more until the read is cancelled.</summary>
    private sealed class StallingStream(byte[] first) : Stream
    {
        private bool sent;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!sent)
            {
                sent = true;
                first.CopyTo(buffer);
                return first.Length;
            }
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
