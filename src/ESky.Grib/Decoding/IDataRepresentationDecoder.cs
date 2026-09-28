using ESky.Grib.Models;

namespace ESky.Grib.Decoding;

public interface IDataRepresentationDecoder
{
    ushort TemplateNumber { get; }
    GribDataRepresentation ReadRepresentation(ReadOnlySpan<byte> section5);
    double[] DecodeValues(ReadOnlySpan<byte> section7Payload, GribDataRepresentation representation);
}
