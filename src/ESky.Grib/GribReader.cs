using System.Buffers.Binary;
using ESky.Grib.Internal;
using ESky.Grib.Models;

namespace ESky.Grib;

public sealed class GribReader
{
    private readonly GribDecoderRegistry _registry;

    public GribReader(GribDecoderRegistry? registry = null) =>
        _registry = registry ?? new GribDecoderRegistry();

    public GribMessage Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Read(ms.ToArray());
    }

    public async Task<GribMessage> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        return Read(ms.ToArray());
    }

    public IReadOnlyList<GribMessage> ReadAll(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ReadAll(ms.ToArray());
    }

    public async Task<IReadOnlyList<GribMessage>> ReadAllAsync(
        Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        return ReadAll(ms.ToArray());
    }

    public IReadOnlyList<GribMessage> ReadAll(ReadOnlySpan<byte> data)
    {
        var messages = new List<GribMessage>();
        var offset = 0;

        while (offset < data.Length)
        {
            if (data.Length - offset < 16)
                throw new GribException("Trailing bytes do not contain a complete GRIB2 Section 0.");
            if (!data.Slice(offset, 4).SequenceEqual("GRIB"u8))
                throw new GribException($"GRIB marker not found at byte {offset}.");

            var length = GribBinary.U64(data, offset + 8);
            if (length > int.MaxValue)
                throw new GribException("A GRIB2 message is too large for this in-memory reader.");
            if (length < 20 || (ulong)offset + length > (ulong)data.Length)
                throw new GribException($"Invalid or truncated GRIB2 message at byte {offset}.");

            messages.Add(Read(data.Slice(offset, checked((int)length))));
            offset += checked((int)length);
        }

        return messages;
    }

    public GribMessage Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 20)
            throw new GribException("Input is too short to be a GRIB2 message.");

        if (!data[..4].SequenceEqual("GRIB"u8))
            throw new GribException("GRIB marker not found at the beginning of the message.");

        var discipline = data[6];
        var edition = data[7];
        if (edition != 2)
            throw new GribException($"Only GRIB edition 2 is supported; file edition is {edition}.");

        var totalLength = GribBinary.U64(data, 8);
        if (totalLength > (ulong)data.Length)
            throw new GribException($"GRIB2 message advertises {totalLength} bytes but only {data.Length} are available.");
        if (totalLength < 20)
            throw new GribException("GRIB2 message length is invalid.");

        var message = data[..checked((int)totalLength)];
        var offset = 16;

        GribIdentification? identification = null;
        GribGrid? currentGrid = null;
        GribProductDefinition? currentProduct = null;
        GribDataRepresentation? currentRepresentation = null;
        byte[]? currentBitmap = null;
        byte currentBitmapIndicator = 255;
        var fields = new List<GribField>();

        while (offset < message.Length)
        {
            if (offset + 4 <= message.Length && message.Slice(offset, 4).SequenceEqual("7777"u8))
            {
                offset += 4;
                break;
            }

            if (offset + 5 > message.Length)
                throw new GribException("Truncated GRIB2 section header.");

            var sectionLength = BinaryPrimitives.ReadUInt32BigEndian(message.Slice(offset, 4));
            if (sectionLength < 5)
                throw new GribException($"Invalid GRIB2 section length {sectionLength} at byte {offset}.");

            var end = (ulong)offset + sectionLength;
            if (end > (ulong)message.Length)
                throw new GribException("GRIB2 section extends beyond the message boundary.");

            var section = message.Slice(offset, checked((int)sectionLength));
            var sectionNumber = section[4];

            switch (sectionNumber)
            {
                case 1:
                    identification = ParseIdentification(section);
                    break;

                case 2:
                    // Local-use section: intentionally preserved as an extension point, ignored by core parser.
                    break;

                case 3:
                {
                    if (section.Length < 14) throw new GribException("Section 3 is truncated.");
                    var template = GribBinary.U16(section, 12);
                    currentGrid = _registry.GetGridDecoder(template).Decode(section);
                    break;
                }

                case 4:
                    currentProduct = ParseProductDefinition(section);
                    break;

                case 5:
                {
                    if (section.Length < 11) throw new GribException("Section 5 is truncated.");
                    var template = GribBinary.U16(section, 9);
                    currentRepresentation = _registry.GetDataDecoder(template).ReadRepresentation(section);
                    break;
                }

                case 6:
                    ParseBitmap(section, out currentBitmapIndicator, out currentBitmap);
                    break;

                case 7:
                {
                    if (currentGrid is null) throw new GribException("Section 7 encountered before a supported Section 3 grid definition.");
                    if (currentProduct is null) throw new GribException("Section 7 encountered before Section 4 product definition.");
                    if (currentRepresentation is null) throw new GribException("Section 7 encountered before Section 5 data representation.");

                    var decoder = _registry.GetDataDecoder(currentRepresentation.TemplateNumber);
                    var packed = decoder.DecodeValues(section[5..], currentRepresentation);
                    var values = ApplyBitmap(packed, currentGrid.PointCount, currentBitmapIndicator, currentBitmap);

                    fields.Add(new GribField
                    {
                        Grid = currentGrid,
                        Product = currentProduct,
                        DataRepresentation = currentRepresentation,
                        Values = values
                    });

                    currentProduct = null;
                    currentRepresentation = null;
                    currentBitmap = null;
                    currentBitmapIndicator = 255;
                    break;
                }

                default:
                    throw new GribException($"Unexpected GRIB2 section number {sectionNumber}.");
            }

            offset += checked((int)sectionLength);
        }

        if (offset != message.Length)
            throw new GribException("GRIB2 message contains trailing or malformed data after Section 8.");
        if (identification is null)
            throw new GribException("GRIB2 message does not contain Section 1 (Identification).");
        if (fields.Count == 0)
            throw new GribException("GRIB2 message does not contain a decodable data field.");

        return new GribMessage
        {
            Discipline = discipline,
            Edition = edition,
            TotalLength = totalLength,
            Identification = identification,
            Fields = fields
        };
    }

    private static GribIdentification ParseIdentification(ReadOnlySpan<byte> section)
    {
        if (section.Length < 21)
            throw new GribException("Section 1 is too short.");

        var year = GribBinary.U16(section, 12);
        var referenceTime = new DateTime(
            year, section[14], section[15], section[16], section[17], section[18], DateTimeKind.Utc);

        return new GribIdentification(
            OriginatingCenter: GribBinary.U16(section, 5),
            OriginatingSubCenter: GribBinary.U16(section, 7),
            MasterTablesVersion: section[9],
            LocalTablesVersion: section[10],
            ReferenceTimeSignificance: section[11],
            ReferenceTimeUtc: referenceTime,
            ProductionStatus: section[19],
            ProcessedDataType: section[20]);
    }

    private static GribProductDefinition ParseProductDefinition(ReadOnlySpan<byte> section)
    {
        if (section.Length < 34)
            throw new GribException("Section 4 is too short for Product Definition Template 4.0.");

        var template = GribBinary.U16(section, 7);
        if (template != 0)
            throw new UnsupportedGribTemplateException("Product Definition (4)", template);

        return new GribProductDefinition(
            TemplateNumber: template,
            ParameterCategory: section[9],
            ParameterNumber: section[10],
            GeneratingProcessType: section[11],
            ForecastTimeUnit: section[17],
            ForecastTime: GribBinary.S32(section, 18),
            FirstFixedSurface: ParseFixedSurface(section, 22),
            SecondFixedSurface: ParseFixedSurface(section, 28));
    }

    private static GribFixedSurface ParseFixedSurface(ReadOnlySpan<byte> section, int offset)
    {
        var type = section[offset];
        var rawScale = section[offset + 1];
        var scaledValue = GribBinary.U32(section, offset + 2);

        // 255 is the GRIB missing-value marker for these one-octet fields.
        var scale = rawScale == 255 ? (sbyte)0 : GribBinary.S8(rawScale);
        return new GribFixedSurface(type, scale, scaledValue);
    }

    private static void ParseBitmap(ReadOnlySpan<byte> section, out byte indicator, out byte[]? bitmap)
    {
        if (section.Length < 6)
            throw new GribException("Section 6 is too short.");

        indicator = section[5];
        bitmap = indicator switch
        {
            255 => null,
            0 => section[6..].ToArray(),
            _ => throw new GribException($"Predefined GRIB2 bitmap indicator {indicator} is not supported.")
        };
    }

    private static double[] ApplyBitmap(double[] packed, uint gridPointCount, byte indicator, byte[]? bitmap)
    {
        if (indicator == 255)
        {
            if ((uint)packed.Length != gridPointCount)
                throw new GribException($"Decoded {packed.Length} values for a grid containing {gridPointCount} points.");
            return packed;
        }

        if (indicator != 0 || bitmap is null)
            throw new GribException("Invalid bitmap state.");

        var output = new double[checked((int)gridPointCount)];
        var packedIndex = 0;
        for (var i = 0; i < output.Length; i++)
        {
            var byteIndex = i >> 3;
            if (byteIndex >= bitmap.Length)
                throw new GribException("Bitmap is shorter than the grid point count.");

            var bit = 7 - (i & 7);
            var present = ((bitmap[byteIndex] >> bit) & 1) != 0;
            if (!present)
            {
                output[i] = double.NaN;
                continue;
            }

            if (packedIndex >= packed.Length)
                throw new GribException("Bitmap references more present values than Section 7 contains.");

            output[i] = packed[packedIndex++];
        }

        if (packedIndex != packed.Length)
            throw new GribException("Section 7 contains more values than are referenced by the bitmap.");

        return output;
    }
}
