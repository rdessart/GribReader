using ESky.Grib;
using ESky.Grib.Decoding;
using ESky.Grib.Models;
using ESky.Grib.Weather;

RunRealGribSmokeTest();
RunManagedAecSmokeTest();
RunWeatherApiSmokeTest();

Console.WriteLine("NativeAOT GRIB smoke test passed.");

static void RunRealGribSmokeTest()
{
    var fixture = Path.Combine(
        AppContext.BaseDirectory,
        "TestData",
        "regular_ll_ccsds.grib2");

    if (!File.Exists(fixture))
        throw new InvalidOperationException($"GRIB fixture not found: {fixture}");

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

static byte[] BuildUncompressedAecBlock(ReadOnlySpan<byte> samples)
{
    // With 8-bit samples and unrestricted codes, the AEC identifier is
    // three bits. 0b111 selects an uncompressed block.
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
