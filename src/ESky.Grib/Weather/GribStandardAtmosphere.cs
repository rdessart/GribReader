namespace ESky.Grib.Weather;

/// <summary>
/// Small ISA helper for pressure-altitude based weather queries.
/// The input altitude is treated as geopotential pressure altitude in the
/// International Standard Atmosphere, not geometric terrain altitude.
/// </summary>
public static class GribStandardAtmosphere
{
    private const double SeaLevelPressurePa = 101_325.0;
    private const double SeaLevelTemperatureK = 288.15;
    private const double Gravity = 9.80665;
    private const double AirGasConstant = 287.05287;
    private const double FeetToMetres = 0.3048;

    private const double TropopauseMetres = 11_000.0;
    private const double LowerStratosphereMetres = 20_000.0;
    private const double UpperSupportedMetres = 32_000.0;

    private const double TroposphereLapseRate = -0.0065;
    private const double LowerStratosphereTemperatureK = 216.65;
    private const double UpperStratosphereLapseRate = 0.001;

    private static readonly double PressureAt11KmPa =
        GradientLayerPressure(
            SeaLevelPressurePa,
            SeaLevelTemperatureK,
            TroposphereLapseRate,
            TropopauseMetres);

    private static readonly double PressureAt20KmPa =
        IsothermalLayerPressure(
            PressureAt11KmPa,
            LowerStratosphereTemperatureK,
            LowerStratosphereMetres - TropopauseMetres);

    public static double PressureHpaFromAltitudeFeet(double altitudeFeet) =>
        PressurePaFromAltitudeMetres(altitudeFeet * FeetToMetres) / 100.0;

    public static double PressurePaFromAltitudeMetres(double altitudeMetres)
    {
        if (!double.IsFinite(altitudeMetres))
            throw new ArgumentOutOfRangeException(nameof(altitudeMetres));

        if (altitudeMetres < -2_000 || altitudeMetres > UpperSupportedMetres)
        {
            throw new ArgumentOutOfRangeException(
                nameof(altitudeMetres),
                "ISA conversion is supported from -2,000 m to 32,000 m.");
        }

        if (altitudeMetres <= TropopauseMetres)
        {
            return GradientLayerPressure(
                SeaLevelPressurePa,
                SeaLevelTemperatureK,
                TroposphereLapseRate,
                altitudeMetres);
        }

        if (altitudeMetres <= LowerStratosphereMetres)
        {
            return IsothermalLayerPressure(
                PressureAt11KmPa,
                LowerStratosphereTemperatureK,
                altitudeMetres - TropopauseMetres);
        }

        return GradientLayerPressure(
            PressureAt20KmPa,
            LowerStratosphereTemperatureK,
            UpperStratosphereLapseRate,
            altitudeMetres - LowerStratosphereMetres);
    }

    private static double GradientLayerPressure(
        double basePressurePa,
        double baseTemperatureK,
        double lapseRateKPerMetre,
        double heightDeltaMetres)
    {
        var temperature = baseTemperatureK +
                          lapseRateKPerMetre * heightDeltaMetres;

        return basePressurePa *
               Math.Pow(
                   baseTemperatureK / temperature,
                   Gravity / (AirGasConstant * lapseRateKPerMetre));
    }

    private static double IsothermalLayerPressure(
        double basePressurePa,
        double temperatureK,
        double heightDeltaMetres) =>
        basePressurePa *
        Math.Exp(
            -Gravity * heightDeltaMetres /
            (AirGasConstant * temperatureK));
}
