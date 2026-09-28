namespace ESky.Grib.Models;

public abstract record GribDataRepresentation(ushort TemplateNumber, uint ValueCount);

public sealed record SimplePackingRepresentation(
    uint ValueCount,
    float ReferenceValue,
    short BinaryScaleFactor,
    short DecimalScaleFactor,
    byte BitsPerValue,
    byte OriginalFieldType)
    : GribDataRepresentation(0, ValueCount);

public sealed record CcsdsPackingRepresentation(
    uint ValueCount,
    float ReferenceValue,
    short BinaryScaleFactor,
    short DecimalScaleFactor,
    byte BitsPerValue,
    byte OriginalFieldType,
    byte CompressionOptionsMask,
    byte BlockSize,
    ushort ReferenceSampleInterval)
    : GribDataRepresentation(42, ValueCount);
