using ESky.Grib.IconEu;
using ESky.Grib.Weather;
using ESky.Grib.Models;

namespace ESky.Grib.Tests;

public sealed class IconEuLiveIntegrationTests
{
    [Fact]
    [Trait("Category", "LiveIconEu")]
    public void Decode_CurrentDwdIconEuUWind_DecodesRealCcsdsField()
    {
        var path = Environment.GetEnvironmentVariable("ICON_EU_GRIB_FILE");

        if (string.IsNullOrWhiteSpace(path))
            return;

        using var stream = File.OpenRead(path);
        var messages = new GribReader().ReadAll(stream);

        Assert.NotEmpty(messages);

        var field = messages
            .SelectMany(message => message.Fields.Select(f => (message, field: f)))
            .FirstOrDefault(
                item =>
                    item.message.Discipline == 0 &&
                    item.field.Product.ParameterCategory == 2 &&
                    item.field.Product.ParameterNumber == 2);

        Assert.NotNull(field.message);
        Assert.NotNull(field.field);

        var grid = Assert.IsType<RegularLatLonGrid>(field.field.Grid);
        var representation = Assert.IsType<CcsdsPackingRepresentation>(
            field.field.DataRepresentation);

        Assert.Equal((ushort)42, representation.TemplateNumber);
        Assert.Equal(grid.PointCount, (uint)field.field.Values.Length);
        Assert.True(grid.Ni > 100);
        Assert.True(grid.Nj > 100);
        Assert.Contains(field.field.Values, double.IsFinite);
    }

    [Fact]
    [Trait("Category", "LiveIconEu")]
    public async Task IconEuClient_DiscoversDownloadsAndDecodesPressureLevel()
    {
        if (Environment.GetEnvironmentVariable("ICON_EU_LIVE") != "1")
            return;

        var cache = Path.Combine(
            Path.GetTempPath(),
            "ESky.Grib.Live",
            Guid.NewGuid().ToString("N"));

        try
        {
            using var client = new IconEuClient(
                new IconEuClientOptions
                {
                    CacheDirectory = cache,
                    MaxConcurrentDownloads = 1
                });

            var run = await client.DiscoverLatestRunAsync();

            var result = await client.LoadAsync(new IconEuRequest
            {
                RunUtc = run,
                Parameters = [IconEuParameter.UComponentOfWind],
                ForecastHours = [0],
                PressureLevelsHpa = [500]
            });

            Assert.Equal(run, result.RunUtc);
            Assert.Equal(1, result.FileCount);
            Assert.Equal(1, result.DownloadedFileCount);
            Assert.Empty(result.MissingFiles);

            var windU = result.Dataset.GetValue(
                IconEuParameter.UComponentOfWind.ToGribParameter(),
                latitude: 50.9,
                longitude: 4.5,
                GribLevel.PressureHpa(500),
                TimeSpan.Zero);

            Assert.NotNull(windU);
            Assert.True(double.IsFinite(windU.Value));
        }
        finally
        {
            if (Directory.Exists(cache))
                Directory.Delete(cache, recursive: true);
        }
    }
}
