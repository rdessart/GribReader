using ESky.Grib.Decoding;

namespace ESky.Grib;

public sealed class GribDecoderRegistry
{
    private readonly Dictionary<ushort, IGridDefinitionDecoder> _gridDecoders = new();
    private readonly Dictionary<ushort, IDataRepresentationDecoder> _dataDecoders = new();

    public GribDecoderRegistry()
    {
        Register(new RegularLatLonGridDecoder());
        Register(new SimplePackingDecoder());
    }

    public GribDecoderRegistry Register(IGridDefinitionDecoder decoder)
    {
        _gridDecoders[decoder.TemplateNumber] = decoder;
        return this;
    }

    public GribDecoderRegistry Register(IDataRepresentationDecoder decoder)
    {
        _dataDecoders[decoder.TemplateNumber] = decoder;
        return this;
    }

    internal IGridDefinitionDecoder GetGridDecoder(ushort templateNumber) =>
        _gridDecoders.TryGetValue(templateNumber, out var decoder)
            ? decoder
            : throw new UnsupportedGribTemplateException("Grid Definition (3)", templateNumber);

    internal IDataRepresentationDecoder GetDataDecoder(ushort templateNumber) =>
        _dataDecoders.TryGetValue(templateNumber, out var decoder)
            ? decoder
            : throw new UnsupportedGribTemplateException("Data Representation (5)", templateNumber);
}
