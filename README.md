# GribReader

A dependency-free GRIB2 reader core for cross-platform .NET and Avalonia applications.

## Projects

- `src/ESky.Grib` — GRIB2 parser, models, packing decoders and weather-query API
- `tests/ESky.Grib.Tests` — xUnit tests using synthetic messages and real GRIB2 fixtures

## Build

```bash
dotnet test GribReader.slnx
```

## Supported

- GRIB edition 2
- Grid Definition Template 3.0 — regular latitude/longitude
- Product Definition Template 4.0 — analysis/forecast at a point in time
- Data Representation Template 5.0 — simple packing
- Data Representation Template 5.42 — CCSDS/AEC adaptive entropy coding
- Optional Section 6 bitmaps
- Multiple fields per message and concatenated GRIB2 messages
- Synchronous and asynchronous Stream APIs
- Fast nearest-point lookup on regular latitude/longitude grids
- Bilinear interpolation on regular latitude/longitude grids
- Log-pressure interpolation between pressure levels
- Linear interpolation between forecast valid times
- Application-facing temperature/wind queries by position, pressure level, pressure altitude and valid time

Template 5.42 is implemented entirely in managed C#, without a native
`libaec` dependency. This keeps the core library usable on Avalonia desktop,
iOS and Android targets.

## ICON-EU

Current DWD ICON/ICON-EU Open Data uses CCSDS packing (GRIB2 template 5.42).
The reader can decode current regular-latitude/longitude instantaneous fields
that use Product Definition Template 4.0.

DWD distributes many files as `.grib2.bz2`. BZip2 transport decompression is
outside the GRIB format and is intentionally not part of this core reader:
pass the decompressed GRIB2 stream to `GribReader`.

The GitHub Actions workflow also contains a scheduled/manual live integration
job that downloads a current DWD ICON-EU U-wind field and decodes it end-to-end.

## Low-level example

```csharp
await using var stream = File.OpenRead("weather.grib2");
var message = await new GribReader().ReadAsync(stream);

foreach (var field in message.Fields)
{
    var nearest = field.GetNearest(50.9014, 4.4844);
    Console.WriteLine(
        $"{nearest.Latitude:F3}, {nearest.Longitude:F3}: {nearest.Value}");
}
```

## Weather dataset example

```csharp
using ESky.Grib.Weather;

var reader = new GribReader();
var messages = reader.ReadAll(gribStream);
var weather = new GribWeatherDataset(messages);

var wind = weather.GetWind(
    latitude: 50.90,
    longitude: 4.48,
    pressureHpa: 500,
    forecastOffset: TimeSpan.FromHours(6));

var interpolatedWind = weather.GetInterpolatedWind(
    latitude: 50.90,
    longitude: 4.48,
    pressureHpa: 475,
    validTimeUtc: new DateTime(2026, 9, 28, 11, 30, 0, DateTimeKind.Utc));

var cruiseWind = weather.GetWindAtPressureAltitudeFeet(
    latitude: 50.90,
    longitude: 4.48,
    altitudeFeet: 34_000,
    validTimeUtc: new DateTime(2026, 9, 28, 11, 30, 0, DateTimeKind.Utc));

if (cruiseWind is { } w)
{
    Console.WriteLine(
        $"{w.SpeedMetersPerSecond:F1} m/s from {w.DirectionFromDegrees:F0}°");
}
```

The interpolated APIs do not extrapolate. A request outside the grid, outside
the available pressure levels, or outside the available forecast-time range
returns `null`.

Pressure-altitude helpers interpret altitude using the International Standard
Atmosphere and query pressure-level GRIB fields. They do **not** derive pressure
for ICON hybrid/model levels; supporting model-level vertical coordinates will
require the corresponding model-level pressure/geopotential metadata.

For non-convenience parameters, use
`GribWeatherDataset.GetInterpolatedValueAtValidTime` with a `GribParameter`
and `GribLevel`.

## Next extensions

- Additional Product Definition Templates such as 4.8 for accumulated/statistical fields
- Additional grid definitions where useful
- ICON hybrid/model-level vertical-coordinate support
- Additional WMO parameter constants as application requirements grow

See `THIRD_PARTY_NOTICES.md` for attribution for the managed AEC decoder and
the CCSDS integration fixture.
