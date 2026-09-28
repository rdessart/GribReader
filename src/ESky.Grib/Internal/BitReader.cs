namespace ESky.Grib.Internal;

internal ref struct BitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _bitPosition;

    public BitReader(ReadOnlySpan<byte> data) => _data = data;

    public ulong ReadUnsigned(int bitCount)
    {
        if (bitCount is < 0 or > 64)
            throw new ArgumentOutOfRangeException(nameof(bitCount));

        if (_bitPosition + bitCount > _data.Length * 8)
            throw new GribException("GRIB2 packed data ended before all requested bits could be read.");

        ulong result = 0;
        for (var n = 0; n < bitCount; n++)
        {
            var byteIndex = _bitPosition >> 3;
            var bitInByte = 7 - (_bitPosition & 7);
            result = (result << 1) | (uint)((_data[byteIndex] >> bitInByte) & 1);
            _bitPosition++;
        }

        return result;
    }
}
