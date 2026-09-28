using ESky.Grib.Decoding;
using ESky.Grib.Models;

namespace ESky.Grib.Tests;

public sealed class CcsdsPackingDecoderTests
{
    private readonly CcsdsPackingDecoder _decoder = new();

    [Fact]
    public void Decode_UncompressedBlock_ReturnsSamples()
    {
        var writer = new BitWriter();
        writer.Put(0b111, 3);

        byte[] expected = [10, 20, 30, 40, 250, 1, 2, 3];
        foreach (var value in expected)
            writer.Put(value, 8);

        var values = _decoder.DecodeValues(
            writer.ToArray(),
            Representation(expected.Length, bits: 8, blockSize: 8, rsi: 2));

        Assert.Equal(expected.Select(x => (double)x), values);
    }

    [Fact]
    public void Decode_SplitBlock_DecodesRiceValues()
    {
        uint[] high = [0, 1, 2, 0, 3, 1, 0, 2];
        uint[] low = [1, 2, 3, 0, 0, 1, 2, 3];

        var writer = new BitWriter();
        writer.Put(3, 3); // id 3 => split parameter k = 2

        foreach (var value in high)
            writer.PutFundamentalSequence(value);

        foreach (var value in low)
            writer.Put(value, 2);

        var expected = high.Zip(low, (h, l) => (double)((h << 2) | l)).ToArray();

        var values = _decoder.DecodeValues(
            writer.ToArray(),
            Representation(expected.Length, bits: 8, blockSize: 8, rsi: 2));

        Assert.Equal(expected, values);
    }

    [Fact]
    public void Decode_ZeroBlock_ReturnsZeros()
    {
        var writer = new BitWriter();
        writer.Put(0, 3); // low entropy
        writer.Put(0, 1); // zero block
        writer.PutFundamentalSequence(0); // one zero block

        var values = _decoder.DecodeValues(
            writer.ToArray(),
            Representation(8, bits: 8, blockSize: 8, rsi: 2));

        Assert.Equal(new double[8], values);
    }

    [Fact]
    public void Decode_SecondExtension_DecodesPairs()
    {
        uint[] gamma = [0, 1, 2, 4];

        var writer = new BitWriter();
        writer.Put(0, 3); // low entropy
        writer.Put(1, 1); // second extension

        foreach (var value in gamma)
            writer.PutFundamentalSequence(value);

        double[] expected = [0, 0, 1, 0, 0, 1, 1, 1];

        var values = _decoder.DecodeValues(
            writer.ToArray(),
            Representation(expected.Length, bits: 8, blockSize: 8, rsi: 2));

        Assert.Equal(expected, values);
    }

    [Fact]
    public void Decode_PreprocessedBlock_ReconstructsPredictor()
    {
        uint[] stored = [1000, 2, 4, 1, 0, 6, 3, 8];

        var writer = new BitWriter();
        writer.Put(0b1111, 4);

        foreach (var value in stored)
            writer.Put(value, 16);

        double[] expected = [1000, 1001, 1003, 1002, 1002, 1005, 1003, 1007];

        var values = _decoder.DecodeValues(
            writer.ToArray(),
            Representation(
                expected.Length,
                bits: 16,
                blockSize: 8,
                rsi: 2,
                flags: 0x0C)); // AEC_DATA_MSB | AEC_DATA_PREPROCESS

        Assert.Equal(expected, values);
    }

    [Fact]
    public void Read_RealRegularLatLonCcsdsFixture_DecodesConstantField()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "regular_ll_ccsds.grib2");

        var message = new GribReader().Read(File.ReadAllBytes(path));

        var field = Assert.Single(message.Fields);
        var grid = Assert.IsType<RegularLatLonGrid>(field.Grid);
        var representation = Assert.IsType<CcsdsPackingRepresentation>(
            field.DataRepresentation);

        Assert.Equal((uint)16, grid.Ni);
        Assert.Equal((uint)31, grid.Nj);
        Assert.Equal((ushort)42, representation.TemplateNumber);
        Assert.Equal((byte)0, representation.BitsPerValue);
        Assert.Equal((byte)32, representation.BlockSize);
        Assert.Equal((ushort)128, representation.ReferenceSampleInterval);
        Assert.Equal(496, field.Values.Length);

        Assert.All(
            field.Values,
            value => Assert.InRange(Math.Abs(value - 273.15), 0, 0.001));
    }

    private static CcsdsPackingRepresentation Representation(
        int count,
        byte bits,
        byte blockSize,
        ushort rsi,
        byte flags = 0) =>
        new(
            ValueCount: checked((uint)count),
            ReferenceValue: 0,
            BinaryScaleFactor: 0,
            DecimalScaleFactor: 0,
            BitsPerValue: bits,
            OriginalFieldType: 0,
            CompressionOptionsMask: flags,
            BlockSize: blockSize,
            ReferenceSampleInterval: rsi);

    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = [];
        private byte _current;
        private int _count;

        public void Put(uint value, int bitCount)
        {
            for (var bit = bitCount - 1; bit >= 0; bit--)
            {
                _current = (byte)((_current << 1) | ((value >> bit) & 1u));
                _count++;

                if (_count != 8)
                    continue;

                _bytes.Add(_current);
                _current = 0;
                _count = 0;
            }
        }

        public void PutFundamentalSequence(uint zeros)
        {
            for (uint i = 0; i < zeros; i++)
                Put(0, 1);

            Put(1, 1);
        }

        public byte[] ToArray()
        {
            if (_count == 0)
                return [.. _bytes];

            var result = new List<byte>(_bytes)
            {
                (byte)(_current << (8 - _count))
            };

            return [.. result];
        }
    }
}
