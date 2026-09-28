using System.Buffers.Binary;

namespace ESky.Grib.Internal;

internal static class GribBinary
{
    public static ushort U16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));

    public static uint U32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));

    public static ulong U64(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt64BigEndian(data.Slice(offset, 8));

    // WMO GRIB regulation 92.1.5 uses sign-and-magnitude, not two's complement.
    public static short S16(ReadOnlySpan<byte> data, int offset)
    {
        var raw = U16(data, offset);
        var magnitude = (short)(raw & 0x7FFF);
        return (raw & 0x8000) != 0 ? (short)-magnitude : magnitude;
    }

    public static int S32(ReadOnlySpan<byte> data, int offset)
    {
        var raw = U32(data, offset);
        var magnitude = (int)(raw & 0x7FFF_FFFF);
        return (raw & 0x8000_0000) != 0 ? -magnitude : magnitude;
    }

    public static sbyte S8(byte raw)
    {
        var magnitude = (sbyte)(raw & 0x7F);
        return (raw & 0x80) != 0 ? (sbyte)-magnitude : magnitude;
    }

    public static float F32(ReadOnlySpan<byte> data, int offset)
    {
        var bits = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset, 4));
        return BitConverter.Int32BitsToSingle(bits);
    }
}
