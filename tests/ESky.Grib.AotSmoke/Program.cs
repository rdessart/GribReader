using ESky.Grib;
using ESky.Grib.Decoding;
using ESky.Grib.IconEu;
using ESky.Grib.Models;
using ESky.Grib.Weather;
using ICSharpCode.SharpZipLib.BZip2;
using System.Net;
using System.Net.Http;

RunRealGribSmokeTest();
RunManagedAecSmokeTest();
RunWeatherApiSmokeTest();
await RunIconEuClientSmokeTestAsync();

Console.WriteLine("NativeAOT GRIB smoke test passed.");

static void RunRealGribSmokeTest()
{
    var fixture = FixturePath();

    using var stream = File.OpenRead(fixture);
    var message = new GribReader().Read(stream);
    var field = message.Fields.Single();

    if (field.DataRepresentation is not CcsdsPackingRepresentation representation ||
        representation.TemplateNumber != 42)
    {
        throw new InvalidOperationException(
            "Expected GRIB2 Data Representation Template 5.42.");
    }

    if (field.Grid is not RegularLatLonGrid grid ||
        grid.Ni != 16 ||
        grid.Nj != 31)
    {
        throw new InvalidOperationException("Unexpected regular lat/lon grid.");
    }

    if (field.Values.Length != 496)
        throw new InvalidOperationException("Unexpected decoded value count.");

    foreach (var value in field.Values)
    {
        if (!double.IsFinite(value) || Math.Abs(value - 273.15) > 0.001)
        {
            throw new InvalidOperationException(
                $"Unexpected decoded GRIB value: {value}");
        }
    }

    var centre = grid.GetCoordinate(grid.Width / 2, grid.Height / 2);
    var interpolated = field.GetBilinearInterpolatedValue(
        centre.Latitude,
        centre.Longitude);

    if (interpolated is null || Math.Abs(interpolated.Value - 273.15) > 0.001)
        throw new InvalidOperationException("Grid interpolation smoke test failed.");
}

static void RunManagedAecSmokeTest()
{
    byte[] expected = [10, 20, 30, 40, 250, 1, 2, 3];

    var representation = new CcsdsPackingRepresentation(
        ValueCount: (uint)expected.Length,
        ReferenceValue: 0,
        BinaryScaleFactor: 0,
        DecimalScaleFactor: 0,
        BitsPerValue: 8,
        OriginalFieldType: 0,
        CompressionOptionsMask: 0,
        BlockSize: 8,
        ReferenceSampleInterval: 2);

    var payload = BuildUncompressedAecBlock(expected);

    var decoded = new CcsdsPackingDecoder().DecodeValues(
        payload,
        representation);

    if (decoded.Length != expected.Length)
        throw new InvalidOperationException("AEC decoded length mismatch.");

    for (var i = 0; i < expected.Length; i++)
    {
        if (decoded[i] != expected[i])
        {
            throw new InvalidOperationException(
                $"AEC sample {i} mismatch: expected {expected[i]}, got {decoded[i]}.");
        }
    }
}

static void RunWeatherApiSmokeTest()
{
    var pressureAtFl340 =
        GribStandardAtmosphere.PressureHpaFromAltitudeFeet(34_000);

    if (!double.IsFinite(pressureAtFl340) ||
        pressureAtFl340 is < 200 or > 300)
    {
        throw new InvalidOperationException(
            $"Unexpected ISA pressure at FL340: {pressureAtFl340} hPa.");
    }

    var wind = new GribWind(3, 4);

    if (Math.Abs(wind.SpeedMetersPerSecond - 5.0) > 1e-12)
        throw new InvalidOperationException("Wind API smoke test failed.");
}

static async Task RunIconEuClientSmokeTestAsync()
{
    const string fileName =
        "icon-eu_europe_regular-lat-lon_pressure-level_2026092809_000_500_U.grib2.bz2";

    var compressed = CompressBzip2(
        File.ReadAllBytes(FixturePath()));

    var handler = new FakeHandler(request =>
    {
        if (request.RequestUri!.AbsolutePath == "/grib/09/u/")
        {
            var listing =
                $"<html><body><a href=\"{fileName}\">{fileName}</a></body></html>";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(listing)
            };
        }

        if (request.RequestUri.AbsolutePath.EndsWith(
                fileName,
                StringComparison.Ordinal))
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(compressed)
            };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    });

    var cache = Path.Combine(
        Path.GetTempPath(),
        "ESky.Grib.AotSmoke",
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
                MaxConcurrentDownloads = 1
            });

        var result = await client.LoadAsync(new IconEuRequest
        {
            RunUtc = new DateTime(
                2026, 9, 28, 9, 0, 0, DateTimeKind.Utc),
            Parameters = [IconEuParameter.UComponentOfWind],
            ForecastHours = [0],
            PressureLevelsHpa = [500]
        });

        if (result.FileCount != 1 ||
            result.DownloadedFileCount != 1 ||
            result.CacheHitCount != 0)
        {
            throw new InvalidOperationException(
                "ICON-EU loader smoke test failed.");
        }
    }
    finally
    {
        if (Directory.Exists(cache))
            Directory.Delete(cache, recursive: true);
    }
}

static string FixturePath()
{
    var fixture = Path.Combine(
        AppContext.BaseDirectory,
        "TestData",
        "regular_ll_ccsds.grib2");

    if (!File.Exists(fixture))
        throw new InvalidOperationException($"GRIB fixture not found: {fixture}");

    return fixture;
}

static byte[] CompressBzip2(byte[] data)
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

static byte[] BuildUncompressedAecBlock(ReadOnlySpan<byte> samples)
{
    var totalBits = 3 + samples.Length * 8;
    var result = new byte[(totalBits + 7) / 8];
    var bitPosition = 0;

    WriteBits(result, ref bitPosition, 0b111, 3);

    foreach (var sample in samples)
        WriteBits(result, ref bitPosition, sample, 8);

    return result;
}

static void WriteBits(
    Span<byte> destination,
    ref int bitPosition,
    uint value,
    int bitCount)
{
    for (var bit = bitCount - 1; bit >= 0; bit--)
    {
        var byteIndex = bitPosition >> 3;
        var bitInByte = 7 - (bitPosition & 7);

        if (((value >> bit) & 1u) != 0)
            destination[byteIndex] |= (byte)(1 << bitInByte);

        bitPosition++;
    }
}

file sealed class FakeHandler(
    Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        Task.FromResult(responseFactory(request));
}
