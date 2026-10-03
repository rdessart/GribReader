namespace ESky.Grib.Models;

public abstract record GribGrid(ushort TemplateNumber, uint PointCount);

public sealed record RegularLatLonGrid(
    uint Ni,
    uint Nj,
    double FirstLatitude,
    double FirstLongitude,
    double LastLatitude,
    double LastLongitude,
    double IIncrement,
    double JIncrement,
    byte ScanningMode,
    uint PointCount)
    : GribGrid(0, PointCount)
{
    public int Width => checked((int)Ni);
    public int Height => checked((int)Nj);

    public bool IScansNegatively => (ScanningMode & 0x80) != 0;
    public bool JScansPositively => (ScanningMode & 0x40) != 0;
    public bool AdjacentPointsInJDirection => (ScanningMode & 0x20) != 0;
    public bool AlternatingRowScanning => (ScanningMode & 0x10) != 0;

    public (double Latitude, double Longitude) GetCoordinate(int i, int j)
    {
        if ((uint)i >= Ni) throw new ArgumentOutOfRangeException(nameof(i));
        if ((uint)j >= Nj) throw new ArgumentOutOfRangeException(nameof(j));

        var lonDirection = IScansNegatively ? -1.0 : 1.0;
        var latDirection = JScansPositively ? 1.0 : -1.0;

        var longitude = NormalizeLongitude(FirstLongitude + i * IIncrement * lonDirection);
        var latitude = FirstLatitude + j * JIncrement * latDirection;
        return (latitude, longitude);
    }

    public (int I, int J) GetNearestIndices(double latitude, double longitude)
    {
        var latitudeStep = JScansPositively ? JIncrement : -JIncrement;
        var j = latitudeStep == 0
            ? 0
            : (int)Math.Round(
                (latitude - FirstLatitude) / latitudeStep,
                MidpointRounding.AwayFromZero);

        j = Math.Clamp(j, 0, Height - 1);

        if (IIncrement == 0 || Width == 1)
            return (0, j);

        var longitudeStep = IScansNegatively ? -IIncrement : IIncrement;
        var bestI = 0;
        var bestDistance = double.MaxValue;

        for (var wrap = -2; wrap <= 2; wrap++)
        {
            var unwrappedTarget = longitude + wrap * 360.0;
            var i = (int)Math.Round(
                (unwrappedTarget - FirstLongitude) / longitudeStep,
                MidpointRounding.AwayFromZero);

            i = Math.Clamp(i, 0, Width - 1);

            var candidateLongitude = FirstLongitude + i * longitudeStep;
            var distance = Math.Abs(
                NormalizeLongitudeDelta(candidateLongitude - longitude));

            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestI = i;
        }

        return (bestI, j);
    }

    /// <summary>
    /// Converts a geographic position into fractional grid indices suitable
    /// for bilinear interpolation. Returns false when the position is outside
    /// the encoded regular latitude/longitude grid.
    /// </summary>
    public bool TryGetFractionalIndices(
        double latitude,
        double longitude,
        out double i,
        out double j)
    {
        const double epsilon = 1e-9;

        var latitudeStep = JScansPositively ? JIncrement : -JIncrement;

        if (Height == 1 || latitudeStep == 0)
        {
            j = 0;
        }
        else
        {
            j = (latitude - FirstLatitude) / latitudeStep;

            if (j < -epsilon || j > Height - 1 + epsilon)
            {
                i = double.NaN;
                j = double.NaN;
                return false;
            }

            j = Math.Clamp(j, 0, Height - 1);
        }

        if (Width == 1 || IIncrement == 0)
        {
            i = 0;
            return true;
        }

        var longitudeStep = IScansNegatively ? -IIncrement : IIncrement;
        var found = false;
        var bestI = double.NaN;
        var bestWrapDistance = double.MaxValue;

        // A GRIB regular-lat/lon grid may encode longitudes in either
        // [-180, 180] or [0, 360]. Test equivalent longitudes and retain the
        // representation that falls inside this grid.
        for (var wrap = -2; wrap <= 2; wrap++)
        {
            var unwrappedTarget = longitude + wrap * 360.0;
            var candidateI = (unwrappedTarget - FirstLongitude) / longitudeStep;

            if (candidateI < -epsilon || candidateI > Width - 1 + epsilon)
                continue;

            var clampedI = Math.Clamp(candidateI, 0, Width - 1);
            var encodedLongitude = FirstLongitude + clampedI * longitudeStep;
            var distance = Math.Abs(encodedLongitude - unwrappedTarget);

            if (distance >= bestWrapDistance)
                continue;

            bestWrapDistance = distance;
            bestI = clampedI;
            found = true;
        }

        i = bestI;

        if (found)
            return true;

        i = double.NaN;
        j = double.NaN;
        return false;
    }

    private static double NormalizeLongitude(double longitude)
    {
        longitude %= 360.0;
        if (longitude > 180.0) longitude -= 360.0;
        if (longitude <= -180.0) longitude += 360.0;
        return longitude;
    }

    private static double NormalizeLongitudeDelta(double delta)
    {
        delta %= 360.0;
        if (delta > 180.0) delta -= 360.0;
        if (delta < -180.0) delta += 360.0;
        return delta;
    }
}
