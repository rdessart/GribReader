using ESky.Grib.Internal;
using ESky.Grib.Models;

namespace ESky.Grib.Decoding;

public sealed class SimplePackingDecoder : IDataRepresentationDecoder
{
    public ushort TemplateNumber => 0;

    public GribDataRepresentation ReadRepresentation(ReadOnlySpan<byte> section5)
    {
        if (section5.Length < 21)
            throw new GribException("GRIB2 Section 5 is too short for Data Representation Template 5.0.");

        return new SimplePackingRepresentation(
            ValueCount: GribBinary.U32(section5, 5),
            ReferenceValue: GribBinary.F32(section5, 11),
            BinaryScaleFactor: GribBinary.S16(section5, 15),
            DecimalScaleFactor: GribBinary.S16(section5, 17),
            BitsPerValue: section5[19],
            OriginalFieldType: section5[20]);
    }

    public double[] DecodeValues(ReadOnlySpan<byte> section7Payload, GribDataRepresentation representation)
    {
        if (representation is not SimplePackingRepresentation simple)
            throw new ArgumentException("Representation is not GRIB2 simple packing (Template 5.0).", nameof(representation));

        var count = checked((int)simple.ValueCount);
        var values = new double[count];
        var binaryScale = Math.Pow(2.0, simple.BinaryScaleFactor);
        var decimalScale = Math.Pow(10.0, simple.DecimalScaleFactor);

        if (simple.BitsPerValue == 0)
        {
            var constant = simple.ReferenceValue / decimalScale;
            Array.Fill(values, constant);
            return values;
        }

        if (simple.BitsPerValue > 64)
            throw new GribException($"Simple packing with {simple.BitsPerValue} bits/value is unsupported.");

        var requiredBits = (ulong)simple.BitsPerValue * simple.ValueCount;
        if (requiredBits > (ulong)section7Payload.Length * 8UL)
            throw new GribException("GRIB2 Section 7 does not contain enough packed bits for the advertised value count.");

        var reader = new BitReader(section7Payload);
        for (var i = 0; i < count; i++)
        {
            var x = reader.ReadUnsigned(simple.BitsPerValue);
            values[i] = (simple.ReferenceValue + x * binaryScale) / decimalScale;
        }

        return values;
    }
}
