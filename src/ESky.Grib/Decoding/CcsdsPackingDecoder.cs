using ESky.Grib.Internal;
using ESky.Grib.Models;

namespace ESky.Grib.Decoding;

/// <summary>
/// Decodes GRIB2 Data Representation Template 5.42
/// (CCSDS 121.0-B adaptive entropy coding).
/// </summary>
public sealed class CcsdsPackingDecoder : IDataRepresentationDecoder
{
    public ushort TemplateNumber => 42;

    public GribDataRepresentation ReadRepresentation(ReadOnlySpan<byte> section5)
    {
        if (section5.Length < 25)
            throw new GribException(
                "GRIB2 Section 5 is too short for Data Representation Template 5.42.");

        return new CcsdsPackingRepresentation(
            ValueCount: GribBinary.U32(section5, 5),
            ReferenceValue: GribBinary.F32(section5, 11),
            BinaryScaleFactor: GribBinary.S16(section5, 15),
            DecimalScaleFactor: GribBinary.S16(section5, 17),
            BitsPerValue: section5[19],
            OriginalFieldType: section5[20],
            CompressionOptionsMask: section5[21],
            BlockSize: section5[22],
            ReferenceSampleInterval: GribBinary.U16(section5, 23));
    }

    public double[] DecodeValues(
        ReadOnlySpan<byte> section7Payload,
        GribDataRepresentation representation)
    {
        if (representation is not CcsdsPackingRepresentation ccsds)
        {
            throw new ArgumentException(
                "Representation is not GRIB2 CCSDS packing (Template 5.42).",
                nameof(representation));
        }

        var count = checked((int)ccsds.ValueCount);
        var values = new double[count];

        var decimalDivisor = Math.Pow(10.0, ccsds.DecimalScaleFactor);

        if (ccsds.BitsPerValue == 0)
        {
            Array.Fill(values, ccsds.ReferenceValue / decimalDivisor);
            return values;
        }

        var flags = (AecFlags)ccsds.CompressionOptionsMask;
        var samples = AecDecoder.Decode(
            section7Payload,
            count,
            new AecConfig(
                ccsds.BitsPerValue,
                ccsds.BlockSize,
                ccsds.ReferenceSampleInterval,
                flags));

        var binaryScale = Math.Pow(2.0, ccsds.BinaryScaleFactor);
        var signed = (flags & AecFlags.Signed) != 0;

        for (var i = 0; i < samples.Length; i++)
        {
            var packed = samples[i];
            double x;

            if (signed)
            {
                x = ToSigned(packed, ccsds.BitsPerValue);
            }
            else
            {
                x = ccsds.BitsPerValue == 32
                    ? packed
                    : packed & ((1u << ccsds.BitsPerValue) - 1u);
            }

            values[i] = (ccsds.ReferenceValue + x * binaryScale) / decimalDivisor;
        }

        return values;
    }

    private static long ToSigned(uint value, int bitCount)
    {
        if (bitCount == 32)
            return unchecked((int)value);

        var mask = (1u << bitCount) - 1u;
        var signBit = 1u << (bitCount - 1);
        var normalized = value & mask;

        return (normalized & signBit) == 0
            ? normalized
            : (long)normalized - (1L << bitCount);
    }
}
