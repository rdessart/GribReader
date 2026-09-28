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

    /// <summary>
    /// Returns temperature using bilinear horizontal interpolation,
    /// logarithmic pressure interpolation and linear time interpolation.
    /// No extrapolation is performed in any dimension.
    /// </summary>
    public double? GetInterpolatedTemperature(
        double latitude,
        double longitude,
        double pressureHpa,
        DateTime validTimeUtc) =>
        GetInterpolatedValueAtValidTime(
            GribParameters.Temperature,
            latitude,
            longitude,
            GribLevel.PressureHpa(pressureHpa),
            validTimeUtc);

    /// <summary>
    /// Returns wind using bilinear horizontal interpolation,
    /// logarithmic pressure interpolation and linear time interpolation.
    /// U and V are interpolated independently before speed/direction are
    /// derived.
    /// </summary>
    public GribWind? GetInterpolatedWind(
        double latitude,
        double longitude,
        double pressureHpa,
        DateTime validTimeUtc)
    {
        var level = GribLevel.PressureHpa(pressureHpa);

        var u = GetInterpolatedValueAtValidTime(
            GribParameters.UComponentOfWind,
            latitude,
            longitude,
            level,
            validTimeUtc);

        var v = GetInterpolatedValueAtValidTime(
            GribParameters.VComponentOfWind,
            latitude,
            longitude,
            level,
            validTimeUtc);

        return u is not null && v is not null
            ? new GribWind(u.Value, v.Value)
            : null;
    }

    /// <summary>
    /// Interprets altitude as ISA pressure altitude, converts it to pressure,
    /// then interpolates pressure-level temperature data.
    /// </summary>
    public double? GetTemperatureAtPressureAltitudeFeet(
        double latitude,
        double longitude,
        double altitudeFeet,
        DateTime validTimeUtc) =>
        GetInterpolatedTemperature(
            latitude,
            longitude,
            GribStandardAtmosphere.PressureHpaFromAltitudeFeet(altitudeFeet),
            validTimeUtc);

    /// <summary>
    /// Interprets altitude as ISA pressure altitude, converts it to pressure,
    /// then interpolates pressure-level U/V wind data.
    /// </summary>
    public GribWind? GetWindAtPressureAltitudeFeet(
        double latitude,
        double longitude,
        double altitudeFeet,
        DateTime validTimeUtc) =>
        GetInterpolatedWind(
            latitude,
            longitude,
            GribStandardAtmosphere.PressureHpaFromAltitudeFeet(altitudeFeet),
            validTimeUtc);

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
        validTimeUtc = EnsureUtc(validTimeUtc);

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

    /// <summary>
    /// Interpolates a field at a geographic position, vertical level and valid
    /// time. The newest model run that can bracket the request is preferred.
    /// Horizontal interpolation is bilinear, time interpolation is linear,
    /// and pressure levels (surface type 100) are interpolated linearly in
    /// ln(pressure). Other numeric vertical coordinates use linear spacing.
    /// </summary>
    public double? GetInterpolatedValueAtValidTime(
        GribParameter parameter,
        double latitude,
        double longitude,
        GribLevel level,
        DateTime validTimeUtc)
    {
        validTimeUtc = EnsureUtc(validTimeUtc);

        var candidates = _fields
            .Where(entry =>
                MatchesParameterAndSurfaceType(entry, parameter, level.SurfaceType) &&
                entry.Field.Product.ForecastOffset is not null)
            .GroupBy(entry => entry.ReferenceTimeUtc)
            .OrderByDescending(group => group.Key);

        foreach (var run in candidates)
        {
            if (run.Key > validTimeUtc)
                continue;

            var value = InterpolateRun(
                run,
                latitude,
                longitude,
                level,
                validTimeUtc);

            if (value is not null)
                return value;
        }

        return null;
    }

    private static double? InterpolateRun(
        IEnumerable<FieldEntry> run,
        double latitude,
        double longitude,
        GribLevel level,
        DateTime validTimeUtc)
    {
        var samples = run
            .GroupBy(entry =>
                entry.ReferenceTimeUtc + entry.Field.Product.ForecastOffset!.Value)
            .Select(group => new TimeSample(
                group.Key,
                InterpolateVertical(
                    group,
                    latitude,
                    longitude,
                    level)))
            .Where(sample => sample.Value is not null)
            .OrderBy(sample => sample.ValidTimeUtc)
            .ToArray();

        if (samples.Length == 0)
            return null;

        var exact = samples.FirstOrDefault(sample => sample.ValidTimeUtc == validTimeUtc);
        if (exact.Value is not null)
            return exact.Value;

        TimeSample? before = null;
        TimeSample? after = null;

        foreach (var sample in samples)
        {
            if (sample.ValidTimeUtc < validTimeUtc)
                before = sample;
            else if (sample.ValidTimeUtc > validTimeUtc)
            {
                after = sample;
                break;
            }
        }

        if (before is null || after is null)
            return null;

        var span = (after.Value.ValidTimeUtc - before.Value.ValidTimeUtc).TotalSeconds;
        if (span <= 0)
            return null;

        var t = (validTimeUtc - before.Value.ValidTimeUtc).TotalSeconds / span;
        return Lerp(before.Value.Value!.Value, after.Value.Value!.Value, t);
    }

    private static double? InterpolateVertical(
        IEnumerable<FieldEntry> entries,
        double latitude,
        double longitude,
        GribLevel level)
    {
        if (level.Value is null)
        {
            foreach (var entry in entries)
            {
                var value = entry.Field.GetBilinearInterpolatedValue(latitude, longitude);
                if (value is not null)
                    return value;
            }

            return null;
        }

        var target = level.Value.Value;
        var verticalSamples = entries
            .Select(entry =>
            {
                var surfaceValue = entry.Field.Product.FirstFixedSurface.Value;
                var fieldValue = surfaceValue is null
                    ? null
                    : entry.Field.GetBilinearInterpolatedValue(latitude, longitude);

                return new VerticalSample(surfaceValue, fieldValue);
            })
            .Where(sample =>
                sample.LevelValue is not null &&
                sample.Value is not null)
            .OrderBy(sample => sample.LevelValue)
            .ToArray();

        if (verticalSamples.Length == 0)
            return null;

        foreach (var sample in verticalSamples)
        {
            if (NearlyEqual(sample.LevelValue!.Value, target))
                return sample.Value;
        }

        VerticalSample? lower = null;
        VerticalSample? upper = null;

        foreach (var sample in verticalSamples)
        {
            if (sample.LevelValue!.Value < target)
                lower = sample;
            else if (sample.LevelValue.Value > target)
            {
                upper = sample;
                break;
            }
        }

        if (lower is null || upper is null)
            return null;

        var lowerCoordinate = VerticalCoordinate(
            level.SurfaceType,
            lower.Value.LevelValue!.Value);

        var upperCoordinate = VerticalCoordinate(
            level.SurfaceType,
            upper.Value.LevelValue!.Value);

        var targetCoordinate = VerticalCoordinate(level.SurfaceType, target);
        var denominator = upperCoordinate - lowerCoordinate;

        if (Math.Abs(denominator) < double.Epsilon)
            return lower.Value.Value;

        var t = (targetCoordinate - lowerCoordinate) / denominator;
        return Lerp(lower.Value.Value!.Value, upper.Value.Value!.Value, t);
    }

    private static double VerticalCoordinate(byte surfaceType, double value)
    {
        if (surfaceType == 100)
        {
            if (value <= 0)
                throw new GribException("Pressure levels must be greater than zero.");

            return Math.Log(value);
        }

        return value;
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
        if (!MatchesParameterAndSurfaceType(
                entry,
                parameter,
                level.SurfaceType))
        {
            return false;
        }

        if (level.Value is null)
            return true;

        if (entry.Field.Product.FirstFixedSurface.Value is not double surfaceValue)
            return false;

        return NearlyEqual(surfaceValue, level.Value.Value);
    }

    private static bool MatchesParameterAndSurfaceType(
        FieldEntry entry,
        GribParameter parameter,
        byte surfaceType) =>
        entry.Discipline == parameter.Discipline &&
        entry.Field.Product.ParameterCategory == parameter.Category &&
        entry.Field.Product.ParameterNumber == parameter.Number &&
        entry.Field.Product.FirstFixedSurface.Type == surfaceType;

    private static bool NearlyEqual(double a, double b)
    {
        var tolerance = Math.Max(1e-6, Math.Abs(b) * 1e-9);
        return Math.Abs(a - b) <= tolerance;
    }

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : value.ToUniversalTime();

    private static double Lerp(double a, double b, double t) =>
        a + (b - a) * t;

    private readonly record struct FieldEntry(
        byte Discipline,
        DateTime ReferenceTimeUtc,
        GribField Field);

    private readonly record struct TimeSample(
        DateTime ValidTimeUtc,
        double? Value);

    private readonly record struct VerticalSample(
        double? LevelValue,
        double? Value);
}
