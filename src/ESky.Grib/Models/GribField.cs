namespace ESky.Grib.Models;

public sealed class GribField
{
    public required GribGrid Grid { get; init; }
    public required GribProductDefinition Product { get; init; }
    public required GribDataRepresentation DataRepresentation { get; init; }
    public required double[] Values { get; init; }

    public double this[int index] => Values[index];

    public double GetValue(int i, int j)
    {
        if (Grid is not RegularLatLonGrid grid)
            throw new NotSupportedException("Indexing is currently implemented only for regular latitude/longitude grids.");

        return Values[GetScanIndex(grid, i, j)];
    }

    public IEnumerable<(double Latitude, double Longitude, double Value)> EnumeratePoints()
    {
        if (Grid is not RegularLatLonGrid grid)
            throw new NotSupportedException("Point enumeration is currently implemented only for regular latitude/longitude grids.");

        for (var j = 0; j < grid.Height; j++)
        for (var i = 0; i < grid.Width; i++)
        {
            var index = GetScanIndex(grid, i, j);
            var (latitude, longitude) = grid.GetCoordinate(i, j);
            yield return (latitude, longitude, Values[index]);
        }
    }

    public (double Latitude, double Longitude, double Value) GetNearest(
        double latitude,
        double longitude)
    {
        if (Grid is not RegularLatLonGrid grid)
            throw new NotSupportedException("Nearest-point lookup is currently implemented only for regular latitude/longitude grids.");

        var (i, j) = grid.GetNearestIndices(latitude, longitude);
        var coordinate = grid.GetCoordinate(i, j);

        return (coordinate.Latitude, coordinate.Longitude, GetValue(i, j));
    }

    private static int GetScanIndex(RegularLatLonGrid grid, int i, int j)
    {
        if ((uint)i >= grid.Ni) throw new ArgumentOutOfRangeException(nameof(i));
        if ((uint)j >= grid.Nj) throw new ArgumentOutOfRangeException(nameof(j));

        if (!grid.AdjacentPointsInJDirection)
        {
            var scanI = grid.AlternatingRowScanning && (j & 1) == 1
                ? grid.Width - 1 - i
                : i;
            return checked(j * grid.Width + scanI);
        }

        var scanJ = grid.AlternatingRowScanning && (i & 1) == 1
            ? grid.Height - 1 - j
            : j;
        return checked(i * grid.Height + scanJ);
    }
}
