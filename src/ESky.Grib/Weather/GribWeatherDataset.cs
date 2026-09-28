using ESky.Grib.Models;

namespace ESky.Grib.Weather;

public readonly record struct GribParameter(
    byte Discipline,
    byte Category,
    byte Number);

public static class GribParameters
{
    public static GribParameter Temperature { get; } = new(0, 0, 0);
    public static GribParameter UComponentOfWind { get; } = new(0, 2, 2);
    public static GribParameter VComponentOfWind { get; } = new(0, 2, 3);
}

public readonly record struct GribLevel(byte SurfaceType, double? Value)
{
    public static GribLevel Surface { get; } = new(1, null);

    public static GribLevel PressureHpa(double pressureHpa) =>
        new(100, pressureHpa * 100.0);

    public static GribLevel HeightAboveGroundMeters(double metres) =>
        new(103, metres);
}

public readonly record struct GribWind(double U, double V)
{
    public double SpeedMetersPerSecond => Math.Sqrt(U * U + V * V);

    /// <summary>
    /// Meteorological wind direction in degrees true: the direction from
    /// which the wind is blowing, clockwise from north.
    /// </summary>
    public double DirectionFromDegrees
    {
        get
        {
            var direction = Math.Atan2(-U, -V) * 180.0 / Math.PI;
            return (direction + 360.0) % 360.0;
        }
    }
}

/// <summary>
/// Application-facing view over one or more decoded GRIB2 messages.
/// </summary>
public sealed class GribWeatherDataset
{
    private readonly List<FieldEntry> _fields = [];

    public GribWeatherDataset(IEnumerable<GribMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        foreach (var message in messages)
        {
            foreach (var field in message.Fields)
            {
                _fields.Add(new FieldEntry(
                    message.Discipline,
                    message.Identification.ReferenceTimeUtc,
                    field));
            }
        }
    }

    public GribWeatherDataset(GribMessage message)
        : this([message])
    {
    }

    public double? GetTemperature(
        double latitude,
        double longitude,
        double pressureHpa,
        TimeSpan forecastOffset) =>
        GetValue(
            GribParameters.Temperature,
            latitude,
            longitude,
            GribLevel.PressureHpa(pressureHpa),
            forecastOffset);

    public GribWind? GetWind(
        double latitude,
        double longitude,
        double pressureHpa,
        TimeSpan forecastOffset)
    {
        var level = GribLevel.PressureHpa(pressureHpa);

        var u = GetValue(
            GribParameters.UComponentOfWind,
            latitude,
            longitude,
            level,
            forecastOffset);

        var v = GetValue(
            GribParameters.VComponentOfWind,
            latitude,
            longitude,
            level,
            forecastOffset);

        return u is not null && v is not null
            ? new GribWind(u.Value, v.Value)
            : null;
    }

    public double? GetValue(
        GribParameter parameter,
        double latitude,
        double longitude,
        GribLevel level,
        TimeSpan forecastOffset)
    {
        foreach (var entry in _fields)
        {
            if (!Matches(entry, parameter, level, forecastOffset))
                continue;

            var point = entry.Field.GetNearest(latitude, longitude);
            return double.IsFinite(point.Value) ? point.Value : null;
        }

        return null;
    }

    public double? GetValueAtValidTime(
        GribParameter parameter,
        double latitude,
        double longitude,
        GribLevel level,
        DateTime validTimeUtc)
    {
        if (validTimeUtc.Kind != DateTimeKind.Utc)
            validTimeUtc = validTimeUtc.ToUniversalTime();

        foreach (var entry in _fields)
        {
            var offset = entry.Field.Product.ForecastOffset;
            if (offset is null ||
                entry.ReferenceTimeUtc + offset.Value != validTimeUtc ||
                !MatchesParameterAndLevel(entry, parameter, level))
            {
                continue;
            }

            var point = entry.Field.GetNearest(latitude, longitude);
            return double.IsFinite(point.Value) ? point.Value : null;
        }

        return null;
    }

    private static bool Matches(
        FieldEntry entry,
        GribParameter parameter,
        GribLevel level,
        TimeSpan forecastOffset) =>
        entry.Field.Product.ForecastOffset == forecastOffset &&
        MatchesParameterAndLevel(entry, parameter, level);

    private static bool MatchesParameterAndLevel(
        FieldEntry entry,
        GribParameter parameter,
        GribLevel level)
    {
        if (entry.Discipline != parameter.Discipline ||
            entry.Field.Product.ParameterCategory != parameter.Category ||
            entry.Field.Product.ParameterNumber != parameter.Number)
        {
            return false;
        }

        var surface = entry.Field.Product.FirstFixedSurface;
        if (surface.Type != level.SurfaceType)
            return false;

        if (level.Value is null)
            return true;

        if (surface.Value is not double surfaceValue)
            return false;

        var tolerance = Math.Max(1e-6, Math.Abs(level.Value.Value) * 1e-9);
        return Math.Abs(surfaceValue - level.Value.Value) <= tolerance;
    }

    private readonly record struct FieldEntry(
        byte Discipline,
        DateTime ReferenceTimeUtc,
        GribField Field);
}
