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
