namespace ESky.Grib.Models;

public sealed record GribIdentification(
    ushort OriginatingCenter,
    ushort OriginatingSubCenter,
    byte MasterTablesVersion,
    byte LocalTablesVersion,
    byte ReferenceTimeSignificance,
    DateTime ReferenceTimeUtc,
    byte ProductionStatus,
    byte ProcessedDataType);
