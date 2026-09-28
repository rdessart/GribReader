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

    private static double NormalizeLongitude(double longitude)
    {
        longitude %= 360.0;
        if (longitude > 180.0) longitude -= 360.0;
        if (longitude <= -180.0) longitude += 360.0;
        return longitude;
    }
}
