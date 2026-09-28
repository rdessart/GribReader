using ESky.Grib.Models;

namespace ESky.Grib.Tests;

public sealed class IconEuLiveIntegrationTests
{
    [Fact]
    [Trait("Category", "LiveIconEu")]
    public void Decode_CurrentDwdIconEuUWind_DecodesRealCcsdsField()
    {
        var path = Environment.GetEnvironmentVariable("ICON_EU_GRIB_FILE");

        // The ordinary unit-test job stays hermetic. The dedicated live
        // integration job provides this variable after downloading from DWD.
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
}
