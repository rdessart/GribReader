using ESky.Grib.Models;
using ESky.Grib.Weather;

namespace ESky.Grib.Tests;

public sealed class GribInterpolationTests
{
    [Fact]
    public void BilinearInterpolation_AtGridCentre_AveragesFourCorners()
    {
        var message = Read(
            [0, 10, 20, 30],
            parameterCategory: 0,
            parameterNumber: 0,
            pressurePa: 50_000,
            forecastHours: 6);

        var field = Assert.Single(message.Fields);

        var value = field.GetBilinearInterpolatedValue(50.5, 4.5);

        Assert.NotNull(value);
        Assert.Equal(15.0, value.Value, 12);
    }

    [Fact]
    public void BilinearInterpolation_OutsideGrid_DoesNotExtrapolate()
    {
        var message = Read(
            [0, 10, 20, 30],
            parameterCategory: 0,
            parameterNumber: 0,
            pressurePa: 50_000,
            forecastHours: 6);

        var field = Assert.Single(message.Fields);

        Assert.Null(field.GetBilinearInterpolatedValue(52.0, 4.5));
        Assert.Null(field.GetBilinearInterpolatedValue(50.5, 6.0));
    }

    [Fact]
    public void InterpolatedTemperature_InterpolatesHorizontalPressureAndTime()
    {
        var messages = new[]
        {
            Read(
                [0, 10, 20, 30],
                parameterCategory: 0,
                parameterNumber: 0,
                pressurePa: 100_000,
                forecastHours: 0),
            Read(
                [100, 110, 120, 130],
                parameterCategory: 0,
                parameterNumber: 0,
                pressurePa: 50_000,
                forecastHours: 0),
            Read(
                [60, 70, 80, 90],
                parameterCategory: 0,
                parameterNumber: 0,
                pressurePa: 100_000,
                forecastHours: 6),
            Read(
                [160, 170, 180, 190],
                parameterCategory: 0,
                parameterNumber: 0,
                pressurePa: 50_000,
                forecastHours: 6)
        };

        var dataset = new GribWeatherDataset(messages);
        var reference = messages[0].Identification.ReferenceTimeUtc;

        // Geometric mean pressure is exactly halfway in ln(p).
        var pressureHpa = Math.Sqrt(100_000.0 * 50_000.0) / 100.0;

        var value = dataset.GetInterpolatedTemperature(
            latitude: 50.5,
            longitude: 4.5,
            pressureHpa: pressureHpa,
            validTimeUtc: reference + TimeSpan.FromHours(3));

        // Horizontal centres:
        // t0: 15 @ 1000 hPa, 115 @ 500 hPa => 65 vertically.
        // t6: 75 @ 1000 hPa, 175 @ 500 hPa => 125 vertically.
        // Mid-time => 95.
        Assert.NotNull(value);
        Assert.Equal(95.0, value.Value, 10);
    }

    [Fact]
    public void InterpolatedValue_DoesNotExtrapolatePressureOrTime()
    {
        var messages = new[]
        {
            Read(
                [10, 10, 10, 10],
                parameterCategory: 0,
                parameterNumber: 0,
                pressurePa: 100_000,
                forecastHours: 0),
            Read(
                [20, 20, 20, 20],
                parameterCategory: 0,
                parameterNumber: 0,
                pressurePa: 50_000,
                forecastHours: 6)
        };

        var dataset = new GribWeatherDataset(messages);
        var reference = messages[0].Identification.ReferenceTimeUtc;

        Assert.Null(dataset.GetInterpolatedTemperature(
            50.5,
            4.5,
            pressureHpa: 300,
            validTimeUtc: reference + TimeSpan.FromHours(3)));

        Assert.Null(dataset.GetInterpolatedTemperature(
            50.5,
            4.5,
            pressureHpa: 700,
            validTimeUtc: reference + TimeSpan.FromHours(12)));
    }

    [Fact]
    public void InterpolatedWind_InterpolatesComponentsBeforeDirection()
    {
        var messages = new[]
        {
            Read(
                [0, 0, 0, 0],
                parameterCategory: 2,
                parameterNumber: 2,
                pressurePa: 50_000,
                forecastHours: 0),
            Read(
                [10, 10, 10, 10],
                parameterCategory: 2,
                parameterNumber: 2,
                pressurePa: 50_000,
                forecastHours: 6),
            Read(
                [10, 10, 10, 10],
                parameterCategory: 2,
                parameterNumber: 3,
                pressurePa: 50_000,
                forecastHours: 0),
            Read(
                [0, 0, 0, 0],
                parameterCategory: 2,
                parameterNumber: 3,
                pressurePa: 50_000,
                forecastHours: 6)
        };

        var dataset = new GribWeatherDataset(messages);
        var reference = messages[0].Identification.ReferenceTimeUtc;

        var wind = dataset.GetInterpolatedWind(
            50.5,
            4.5,
            pressureHpa: 500,
            validTimeUtc: reference + TimeSpan.FromHours(3));

        Assert.NotNull(wind);
        Assert.Equal(5.0, wind.Value.U, 12);
        Assert.Equal(5.0, wind.Value.V, 12);
        Assert.Equal(Math.Sqrt(50.0), wind.Value.SpeedMetersPerSecond, 12);
        Assert.Equal(225.0, wind.Value.DirectionFromDegrees, 10);
    }

    [Fact]
    public void PressureAltitudeWind_UsesIsaPressureConversion()
    {
        var pressureAt34K = GribStandardAtmosphere.PressureHpaFromAltitudeFeet(34_000);

        var messages = new[]
        {
            Read(
                [30, 30, 30, 30],
                parameterCategory: 2,
                parameterNumber: 2,
                pressurePa: 30_000,
                forecastHours: 0),
            Read(
                [20, 20, 20, 20],
                parameterCategory: 2,
                parameterNumber: 2,
                pressurePa: 20_000,
                forecastHours: 0),
            Read(
                [0, 0, 0, 0],
                parameterCategory: 2,
                parameterNumber: 3,
                pressurePa: 30_000,
                forecastHours: 0),
            Read(
                [0, 0, 0, 0],
                parameterCategory: 2,
                parameterNumber: 3,
                pressurePa: 20_000,
                forecastHours: 0)
        };

        var dataset = new GribWeatherDataset(messages);
        var validTime = messages[0].Identification.ReferenceTimeUtc;

        var byAltitude = dataset.GetWindAtPressureAltitudeFeet(
            50.5,
            4.5,
            34_000,
            validTime);

        var byPressure = dataset.GetInterpolatedWind(
            50.5,
            4.5,
            pressureAt34K,
            validTime);

        Assert.NotNull(byAltitude);
        Assert.NotNull(byPressure);
        Assert.Equal(byPressure.Value.U, byAltitude.Value.U, 12);
        Assert.Equal(byPressure.Value.V, byAltitude.Value.V, 12);
    }

    [Theory]
    [InlineData(0, 1013.25, 0.01)]
    [InlineData(11000, 226.32, 0.02)]
    [InlineData(20000, 54.75, 0.02)]
    public void StandardAtmosphere_ReturnsExpectedPressure(
        double altitudeMetres,
        double expectedHpa,
        double toleranceHpa)
    {
        var pressure =
            GribStandardAtmosphere.PressurePaFromAltitudeMetres(altitudeMetres) /
            100.0;

        Assert.InRange(
            pressure,
            expectedHpa - toleranceHpa,
            expectedHpa + toleranceHpa);
    }

    private static GribMessage Read(
        byte[] values,
        byte parameterCategory,
        byte parameterNumber,
        uint pressurePa,
        int forecastHours) =>
        new GribReader().Read(
            GribTestMessageBuilder.Create2x2(
                values,
                parameterCategory: parameterCategory,
                parameterNumber: parameterNumber,
                surfaceType: 100,
                surfaceScaledValue: pressurePa,
                forecastHours: forecastHours));
}
