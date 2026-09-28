using ESky.Grib.Models;

namespace ESky.Grib.Tests;

public sealed class GribReaderTests
{
    [Fact]
    public void Read_Valid2x2SimplePackedMessage_DecodesMetadataAndValues()
    {
        var bytes = GribTestMessageBuilder.Create2x2([1, 2, 3, 4]);

        var message = new GribReader().Read(bytes);

        Assert.Equal((byte)2, message.Edition);
        Assert.Equal((byte)0, message.Discipline);
        Assert.Equal((ushort)98, message.Identification.OriginatingCenter);
        Assert.Equal(new DateTime(2026, 9, 28, 6, 0, 0, DateTimeKind.Utc), message.Identification.ReferenceTimeUtc);

        var field = Assert.Single(message.Fields);
        var grid = Assert.IsType<RegularLatLonGrid>(field.Grid);
        Assert.Equal((uint)2, grid.Ni);
        Assert.Equal((uint)2, grid.Nj);
        Assert.Equal(new double[] { 1, 2, 3, 4 }, field.Values);
        Assert.Equal(TimeSpan.FromHours(6), field.Product.ForecastOffset);
        Assert.Equal((byte)100, field.Product.FirstFixedSurface.Type);
        Assert.Equal(50_000d, field.Product.FirstFixedSurface.Value);
    }

    [Fact]
    public void Read_SimplePacking_AppliesBinaryAndDecimalScaling()
    {
        // Y = (R + X * 2^E) / 10^D = (10 + X*2) / 10
        var bytes = GribTestMessageBuilder.Create2x2(
            [0, 1, 2, 3], referenceValue: 10, binaryScale: 1, decimalScale: 1);

        var field = Assert.Single(new GribReader().Read(bytes).Fields);

        Assert.Equal(new[] { 1.0, 1.2, 1.4, 1.6 }, field.Values, new DoubleComparer(1e-12));
    }

    [Fact]
    public void Read_NegativeScaleFactor_UsesGribSignMagnitudeEncoding()
    {
        // E = -1: Y = X * 2^-1
        var bytes = GribTestMessageBuilder.Create2x2(
            [2, 4, 6, 8], binaryScale: -1);

        var values = Assert.Single(new GribReader().Read(bytes).Fields).Values;

        Assert.Equal(new[] { 1.0, 2.0, 3.0, 4.0 }, values);
    }

    [Fact]
    public void Read_Bitmap_InsertsNaNForMissingPoints()
    {
        // Bitmap 1011.... => point 1 is missing; Section 7 contains only 3 values.
        var bytes = GribTestMessageBuilder.Create2x2(
            [10, 20, 30], representedValueCount: 3, bitmap: 0b1011_0000);

        var values = Assert.Single(new GribReader().Read(bytes).Fields).Values;

        Assert.Equal(10, values[0]);
        Assert.True(double.IsNaN(values[1]));
        Assert.Equal(20, values[2]);
        Assert.Equal(30, values[3]);
    }

    [Fact]
    public void Field_GetValue_UsesGridCoordinatesNotRawScanOrder()
    {
        // +j and adjacent i: rows are [1,2], [3,4].
        var field = Assert.Single(new GribReader().Read(
            GribTestMessageBuilder.Create2x2([1, 2, 3, 4], scanningMode: 0x40)).Fields);

        Assert.Equal(1, field.GetValue(0, 0));
        Assert.Equal(2, field.GetValue(1, 0));
        Assert.Equal(3, field.GetValue(0, 1));
        Assert.Equal(4, field.GetValue(1, 1));

        var grid = Assert.IsType<RegularLatLonGrid>(field.Grid);
        Assert.Equal((50.0, 4.0), grid.GetCoordinate(0, 0));
        Assert.Equal((51.0, 5.0), grid.GetCoordinate(1, 1));
    }

    [Fact]
    public void ReadAll_ConcatenatedMessages_ReturnsEveryMessage()
    {
        var a = GribTestMessageBuilder.Create2x2([1, 2, 3, 4]);
        var b = GribTestMessageBuilder.Create2x2([5, 6, 7, 8]);
        var data = a.Concat(b).ToArray();

        var messages = new GribReader().ReadAll(data);

        Assert.Equal(2, messages.Count);
        Assert.Equal(1, messages[0].Fields[0].Values[0]);
        Assert.Equal(5, messages[1].Fields[0].Values[0]);
    }

    [Fact]
    public void Read_InvalidMagic_Throws()
    {
        var bytes = GribTestMessageBuilder.Create2x2([1, 2, 3, 4]);
        bytes[0] = (byte)'X';

        var error = Assert.Throws<GribException>(() => new GribReader().Read(bytes));
        Assert.Contains("GRIB marker", error.Message);
    }

    private sealed class DoubleComparer(double tolerance) : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) <= tolerance;
        public int GetHashCode(double obj) => obj.GetHashCode();
    }
}
