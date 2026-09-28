using ESky.Grib.Internal;
using ESky.Grib.Models;

namespace ESky.Grib.Decoding;

public sealed class RegularLatLonGridDecoder : IGridDefinitionDecoder
{
    public ushort TemplateNumber => 0;

    public GribGrid Decode(ReadOnlySpan<byte> section3)
    {
        // Grid Definition Template 3.0 requires at least 72 octets in Section 3.
        if (section3.Length < 72)
            throw new GribException("GRIB2 Section 3 is too short for Grid Definition Template 3.0.");

        var pointCount = GribBinary.U32(section3, 6);
        var ni = GribBinary.U32(section3, 30);
        var nj = GribBinary.U32(section3, 34);
        var basicAngle = GribBinary.U32(section3, 38);
        var subdivisions = GribBinary.U32(section3, 42);

        var angleUnit = basicAngle == 0 || subdivisions == 0
            ? 1e-6
            : basicAngle / (double)subdivisions;

        var lat1 = GribBinary.S32(section3, 46) * angleUnit;
        var lon1 = GribBinary.S32(section3, 50) * angleUnit;
        var lat2 = GribBinary.S32(section3, 55) * angleUnit;
        var lon2 = GribBinary.S32(section3, 59) * angleUnit;
        var di = GribBinary.U32(section3, 63) * angleUnit;
        var dj = GribBinary.U32(section3, 67) * angleUnit;
        var scanningMode = section3[71];

        if (ni == 0 || nj == 0)
            throw new GribException("GRIB2 regular latitude/longitude grid has Ni or Nj equal to zero.");

        var expected = (ulong)ni * nj;
        if (pointCount != expected)
            throw new GribException($"GRIB2 grid reports {pointCount} points, but Ni × Nj = {expected}.");

        return new RegularLatLonGrid(
            ni, nj, lat1, lon1, lat2, lon2, di, dj, scanningMode, pointCount);
    }
}
