namespace ESky.Grib.Models;

public sealed record GribFixedSurface(byte Type, sbyte ScaleFactor, uint ScaledValue)
{
    public bool IsMissing => Type == 255;
    public bool HasValue => !IsMissing && ScaledValue != uint.MaxValue;
    public double? Value => HasValue ? ScaledValue * Math.Pow(10.0, -ScaleFactor) : null;
}

public sealed record GribProductDefinition(
    ushort TemplateNumber,
    byte ParameterCategory,
    byte ParameterNumber,
    byte GeneratingProcessType,
    byte ForecastTimeUnit,
    int ForecastTime,
    GribFixedSurface FirstFixedSurface,
    GribFixedSurface SecondFixedSurface)
{
    public TimeSpan? ForecastOffset => ForecastTimeUnit switch
    {
        0 => TimeSpan.FromMinutes(ForecastTime),
        1 => TimeSpan.FromHours(ForecastTime),
        2 => TimeSpan.FromDays(ForecastTime),
        10 => TimeSpan.FromHours(ForecastTime * 3d),
        11 => TimeSpan.FromHours(ForecastTime * 6d),
        12 => TimeSpan.FromHours(ForecastTime * 12d),
        13 => TimeSpan.FromSeconds(ForecastTime),
        _ => null
    };
}
