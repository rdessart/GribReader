using ESky.Grib.Models;

namespace ESky.Grib.Decoding;

public interface IGridDefinitionDecoder
{
    ushort TemplateNumber { get; }
    GribGrid Decode(ReadOnlySpan<byte> section3);
}
