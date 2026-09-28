namespace ESky.Grib.Models;

public sealed class GribMessage
{
    public required byte Discipline { get; init; }
    public required byte Edition { get; init; }
    public required ulong TotalLength { get; init; }
    public required GribIdentification Identification { get; init; }
    public required IReadOnlyList<GribField> Fields { get; init; }
}
