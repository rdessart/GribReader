using ESky.Grib.IconEu;
using ESky.Grib.Weather;
using ICSharpCode.SharpZipLib.BZip2;
using System.Net;
using System.Net.Http;

namespace ESky.Grib.Tests;

public sealed class IconEuClientTests
{
    [Fact]
    public async Task DiscoverLatestRunAsync_SelectsNewestPublishedCycle()
    {
        var handler = new FakeHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            var html = path switch
            {
                "/grib/12/t/" => Listing(
                    "icon-eu_europe_regular-lat-lon_pressure-level_2026092712_000_500_T.grib2.bz2"),
                "/grib/09/t/" => Listing(
                    "icon-eu_europe_regular-lat-lon_pressure-level_2026092809_000_500_T.grib2.bz2"),
                "/grib/06/t/" => Listing(
                    "icon-eu_europe_regular-lat-lon_pressure-level_2026092806_000_500_T.grib2.bz2"),
                "/grib/03/t/" => Listing(
                    "icon-eu_europe_regular-lat-lon_pressure-level_2026092803_000_500_T.grib2.bz2"),
                _ => "<html></html>"
            };

            return Html(html);
        });

        using var http = new HttpClient(handler);
        using var client = new IconEuClient(
            http,
            new IconEuClientOptions
            {
                BaseUri = new Uri("https://example.test/grib/"),
                TimeProvider = new FixedTimeProvider(
                    new DateTimeOffset(
                        2026, 9, 28, 12, 30, 0, TimeSpan.Zero)),
                DiscoveryLookbackCycles = 4
            });

        var run = await client.DiscoverLatestRunAsync();

        Assert.Equal(
            new DateTime(2026, 9, 28, 9, 0, 0, DateTimeKind.Utc),
            run);
    }

    [Fact]
    public async Task LoadAsync_DownloadsDecodesAndReusesCache()
    {
        var run = new DateTime(
            2026, 9, 28, 9, 0, 0, DateTimeKind.Utc);

        const string fileName =
            "icon-eu_europe_regular-lat-lon_pressure-level_2026092809_000_500_U.grib2.bz2";

        var grib = GribTestMessageBuilder.Create2x2(
            [10, 20, 30, 40],
            parameterCategory: 2,
            parameterNumber: 2,
            surfaceType: 100,
            surfaceScaledValue: 50_000,
            forecastHours: 0);

        var compressed = CompressBzip2(grib);
        var downloadCount = 0;

        var handler = new FakeHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path == "/grib/09/u/")
                return Html(Listing(fileName));

            if (path.EndsWith(fileName, StringComparison.Ordinal))
            {
                Interlocked.Increment(ref downloadCount);
                return Bytes(compressed);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var cache = Path.Combine(
            Path.GetTempPath(),
            "ESky.Grib.Tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            using var http = new HttpClient(handler);
            using var client = new IconEuClient(
                http,
                new IconEuClientOptions
                {
                    BaseUri = new Uri("https://example.test/grib/"),
                    CacheDirectory = cache,
                    MaxConcurrentDownloads = 2
                });

            var request = new IconEuRequest
            {
                RunUtc = run,
                Parameters = [IconEuParameter.UComponentOfWind],
                ForecastHours = [0],
                PressureLevelsHpa = [500]
            };

            var first = await client.LoadAsync(request);

            Assert.Equal(run, first.RunUtc);
            Assert.Equal(1, first.FileCount);
            Assert.Equal(1, first.DownloadedFileCount);
            Assert.Equal(0, first.CacheHitCount);
            Assert.Empty(first.MissingFiles);

            var value = first.Dataset.GetValue(
                IconEuParameter.UComponentOfWind.ToGribParameter(),
                latitude: 50.0,
                longitude: 4.0,
                GribLevel.PressureHpa(500),
                forecastOffset: TimeSpan.Zero);

            Assert.Equal(10, value);

            var second = await client.LoadAsync(request);

            Assert.Equal(1, second.FileCount);
            Assert.Equal(0, second.DownloadedFileCount);
            Assert.Equal(1, second.CacheHitCount);
            Assert.Equal(1, downloadCount);
        }
        finally
        {
            if (Directory.Exists(cache))
                Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_MissingRequestedFile_ThrowsByDefault()
    {
        var handler = new FakeHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/grib/09/t/")
            {
                return Html(Listing(
                    "icon-eu_europe_regular-lat-lon_pressure-level_2026092809_000_500_T.grib2.bz2"));
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var http = new HttpClient(handler);
        using var client = new IconEuClient(
            http,
            new IconEuClientOptions
            {
                BaseUri = new Uri("https://example.test/grib/")
            });

        var exception = await Assert.ThrowsAsync<IconEuDataNotAvailableException>(
            () => client.LoadAsync(new IconEuRequest
            {
                RunUtc = new DateTime(
                    2026, 9, 28, 9, 0, 0, DateTimeKind.Utc),
                Parameters = [IconEuParameter.Temperature],
                ForecastHours = [6],
                PressureLevelsHpa = [500]
            }));

        Assert.Contains("First missing file", exception.Message);
    }

    private static string Listing(params string[] files) =>
        "<html><body>" +
        string.Join(
            "",
            files.Select(file => $"<a href=\"{file}\">{file}</a>")) +
        "</body></html>";

    private static HttpResponseMessage Html(string html) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(html)
        };

    private static HttpResponseMessage Bytes(byte[] bytes) =>
        new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        };

    private static byte[] CompressBzip2(byte[] data)
    {
        using var output = new MemoryStream();

        using (var bzip2 = new BZip2OutputStream(output)
        {
            IsStreamOwner = false
        })
        {
            bzip2.Write(data);
        }

        return output.ToArray();
    }

    private sealed class FakeHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
