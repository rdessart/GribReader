using ESky.Grib.Models;
using ESky.Grib.Weather;

namespace ESky.Grib.Tests;

public sealed class GribWeatherDatasetTests
{
    [Fact]
    public void GetTemperature_ReturnsNearestPressureLevelValue()
    {
        var message = Read(
            [240, 241, 242, 243],
            parameterCategory: 0,
            parameterNumber: 0);

        var dataset = new GribWeatherDataset(message);

        var temperature = dataset.GetTemperature(
            latitude: 50.9,
            longitude: 4.9,
            pressureHpa: 500,
            forecastOffset: TimeSpan.FromHours(6));

        Assert.Equal(243, temperature);
    }

    [Fact]
    public void GetTemperature_WrongLevelOrForecast_ReturnsNull()
    {
        var message = Read(
            [240, 241, 242, 243],
            parameterCategory: 0,
            parameterNumber: 0);

        var dataset = new GribWeatherDataset(message);

        Assert.Null(dataset.GetTemperature(
            50.5,
            4.5,
            700,
            TimeSpan.FromHours(6)));

        Assert.Null(dataset.GetTemperature(
            50.5,
            4.5,
            500,
            TimeSpan.FromHours(3)));
    }

    [Fact]
    public void GetWind_CombinesUAndVAndComputesMeteorologicalDirection()
    {
        var u = Read(
            [3, 3, 3, 3],
            parameterCategory: 2,
            parameterNumber: 2);

        var v = Read(
            [4, 4, 4, 4],
            parameterCategory: 2,
            parameterNumber: 3);

        var dataset = new GribWeatherDataset([u, v]);

        var wind = dataset.GetWind(
            latitude: 50.4,
            longitude: 4.4,
            pressureHpa: 500,
            forecastOffset: TimeSpan.FromHours(6));

        Assert.NotNull(wind);
        Assert.Equal(3, wind.Value.U);
        Assert.Equal(4, wind.Value.V);
        Assert.Equal(5, wind.Value.SpeedMetersPerSecond, 12);
        Assert.Equal(216.86989764584402, wind.Value.DirectionFromDegrees, 10);
    }

    [Fact]
    public void GetValueAtValidTime_MatchesReferencePlusForecastOffset()
    {
        var message = Read(
            [10, 11, 12, 13],
            parameterCategory: 2,
            parameterNumber: 2);

        var dataset = new GribWeatherDataset(message);
        var validTime = message.Identification.ReferenceTimeUtc +
                        TimeSpan.FromHours(6);

        var value = dataset.GetValueAtValidTime(
            GribParameters.UComponentOfWind,
            50.9,
            4.9,
            GribLevel.PressureHpa(500),
            validTime);

        Assert.Equal(13, value);
    }

    [Fact]
    public void GetNearest_UsesRegularGridIndicesAcrossLongitudeWrapping()
    {
        var grid = new RegularLatLonGrid(
            Ni: 3,
            Nj: 1,
            FirstLatitude: 0,
            FirstLongitude: 359,
            LastLatitude: 0,
            LastLongitude: 1,
            IIncrement: 1,
            JIncrement: 1,
            ScanningMode: 0,
            PointCount: 3);

        var (i, j) = grid.GetNearestIndices(0, -0.1);

        Assert.Equal(1, i);
        Assert.Equal(0, j);
        Assert.Equal((0.0, 0.0), grid.GetCoordinate(i, j));
    }

    private static GribMessage Read(
        byte[] values,
        byte parameterCategory,
        byte parameterNumber) =>
        new GribReader().Read(
            GribTestMessageBuilder.Create2x2(
                values,
                parameterCategory: parameterCategory,
                parameterNumber: parameterNumber));
}
