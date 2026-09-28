using System.Buffers.Binary;

namespace ESky.Grib.Tests;

internal static class GribTestMessageBuilder
{
    public static byte[] Create2x2(
        byte[] packedValues,
        uint representedValueCount = 4,
        byte bitsPerValue = 8,
        float referenceValue = 0,
        short binaryScale = 0,
        short decimalScale = 0,
        byte? bitmap = null,
        byte scanningMode = 0x40,
        byte parameterCategory = 2,
        byte parameterNumber = 2,
        byte surfaceType = 100,
        uint surfaceScaledValue = 50_000,
        int forecastHours = 6,
        byte discipline = 0)
    {
        var sections = new List<byte[]>
        {
            Identification(),
            Grid2x2(scanningMode),
            Product(
                parameterCategory,
                parameterNumber,
                surfaceType,
                surfaceScaledValue,
                forecastHours),
            Representation(
                representedValueCount,
                referenceValue,
                binaryScale,
                decimalScale,
                bitsPerValue),
            Bitmap(bitmap),
            Data(packedValues)
        };

        var totalLength = 16 + sections.Sum(s => s.Length) + 4;
        var message = new byte[totalLength];
        "GRIB"u8.CopyTo(message);
        message[6] = discipline;
        message[7] = 2;
        BinaryPrimitives.WriteUInt64BigEndian(
            message.AsSpan(8, 8),
            (ulong)totalLength);

        var offset = 16;
        foreach (var section in sections)
        {
            section.CopyTo(message, offset);
            offset += section.Length;
        }

        "7777"u8.CopyTo(message.AsSpan(offset, 4));
        return message;
    }

    private static byte[] Identification()
    {
        var s = Section(1, 21);
        WriteU16(s, 5, 98);
        WriteU16(s, 7, 0);
        s[9] = 2;
        s[10] = 0;
        s[11] = 1;
        WriteU16(s, 12, 2026);
        s[14] = 9;
        s[15] = 28;
        s[16] = 6;
        s[17] = 0;
        s[18] = 0;
        s[19] = 0;
        s[20] = 1;
        return s;
    }

    private static byte[] Grid2x2(byte scanningMode)
    {
        var s = Section(3, 72);
        s[5] = 0;
        WriteU32(s, 6, 4);
        s[10] = 0;
        s[11] = 0;
        WriteU16(s, 12, 0);
        s[14] = 6;
        WriteU32(s, 30, 2);
        WriteU32(s, 34, 2);
        WriteU32(s, 38, 0);
        WriteU32(s, 42, 0);
        WriteS32(s, 46, 50_000_000);
        WriteS32(s, 50, 4_000_000);
        s[54] = 0x30;
        WriteS32(s, 55, 51_000_000);
        WriteS32(s, 59, 5_000_000);
        WriteU32(s, 63, 1_000_000);
        WriteU32(s, 67, 1_000_000);
        s[71] = scanningMode;
        return s;
    }

    private static byte[] Product(
        byte parameterCategory,
        byte parameterNumber,
        byte surfaceType,
        uint surfaceScaledValue,
        int forecastHours)
    {
        var s = Section(4, 34);
        WriteU16(s, 5, 0);
        WriteU16(s, 7, 0);
        s[9] = parameterCategory;
        s[10] = parameterNumber;
        s[11] = 2;
        s[12] = 0;
        s[13] = 0;
        WriteU16(s, 14, 0);
        s[16] = 0;
        s[17] = 1;
        WriteS32(s, 18, forecastHours);
        s[22] = surfaceType;
        s[23] = surfaceScaledValue == uint.MaxValue ? (byte)255 : (byte)0;
        WriteU32(s, 24, surfaceScaledValue);
        s[28] = 255;
        s[29] = 255;
        WriteU32(s, 30, uint.MaxValue);
        return s;
    }

    private static byte[] Representation(
        uint count,
        float reference,
        short binaryScale,
        short decimalScale,
        byte bits)
    {
        var s = Section(5, 21);
        WriteU32(s, 5, count);
        WriteU16(s, 9, 0);
        WriteF32(s, 11, reference);
        WriteS16(s, 15, binaryScale);
        WriteS16(s, 17, decimalScale);
        s[19] = bits;
        s[20] = 0;
        return s;
    }

    private static byte[] Bitmap(byte? bitmap)
    {
        if (bitmap is null)
        {
            var s = Section(6, 6);
            s[5] = 255;
            return s;
        }

        var withBitmap = Section(6, 7);
        withBitmap[5] = 0;
        withBitmap[6] = bitmap.Value;
        return withBitmap;
    }

    private static byte[] Data(byte[] payload)
    {
        var s = Section(7, 5 + payload.Length);
        payload.CopyTo(s, 5);
        return s;
    }

    private static byte[] Section(byte number, int length)
    {
        var bytes = new byte[length];
        WriteU32(bytes, 0, (uint)length);
        bytes[4] = number;
        return bytes;
    }

    private static void WriteU16(byte[] b, int o, ushort v) =>
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(o, 2), v);

    private static void WriteU32(byte[] b, int o, uint v) =>
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(o, 4), v);

    private static void WriteS16(byte[] b, int o, short v)
    {
        var magnitude = (ushort)Math.Abs((int)v);
        var raw = v < 0 ? (ushort)(0x8000 | magnitude) : magnitude;
        WriteU16(b, o, raw);
    }

    private static void WriteS32(byte[] b, int o, int v)
    {
        var magnitude = (uint)Math.Abs((long)v);
        var raw = v < 0 ? 0x8000_0000u | magnitude : magnitude;
        WriteU32(b, o, raw);
    }

    private static void WriteF32(byte[] b, int o, float v) =>
        BinaryPrimitives.WriteInt32BigEndian(
            b.AsSpan(o, 4),
            BitConverter.SingleToInt32Bits(v));
}
