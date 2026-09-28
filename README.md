# GribReader

A managed GRIB2 reader and ICON-EU weather-data client for cross-platform .NET and Avalonia applications.

## Projects

- `src/ESky.Grib` — GRIB2 parser, packing decoders, weather-query API and DWD ICON-EU client
- `tests/ESky.Grib.Tests` — xUnit tests using synthetic messages, real GRIB2 fixtures and live DWD validation
- `tests/ESky.Grib.AotSmoke` — NativeAOT smoke application

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
- Managed BZip2 decompression for DWD transport files
- Fast nearest-point and bilinear regular-grid lookup
- Log-pressure interpolation between pressure levels
- Linear interpolation between forecast valid times
- Temperature/wind queries by position, pressure level, pressure altitude and valid time
- DWD ICON-EU run discovery, selective download and local caching
- NativeAOT/trimming validation in CI

## ICON-EU client

`IconEuClient` discovers the latest available three-hour ICON-EU run from
DWD's Open Data directory listings, selects only the requested pressure-level
files, downloads/decompresses them and returns a ready-to-query
`GribWeatherDataset`.

```csharp
using ESky.Grib.IconEu;

using var client = new IconEuClient();

var progress = new Progress<IconEuLoadProgress>(p =>
{
    Console.WriteLine(
        $"{p.Stage}: {p.Completed}/{p.Total} {p.CurrentFile}");
});

var result = await client.LoadAsync(
    new IconEuRequest
    {
        ForecastHours = [0, 3, 6, 9, 12],
        PressureLevelsHpa =
            [1000, 925, 850, 700, 500, 400, 300, 250, 200],
        Parameters =
        [
            IconEuParameter.Temperature,
            IconEuParameter.UComponentOfWind,
            IconEuParameter.VComponentOfWind
        ]
    },
    progress);

Console.WriteLine($"ICON-EU run: {result.RunUtc:u}");
Console.WriteLine(
    $"Files: {result.FileCount}; downloaded: {result.DownloadedFileCount}; cache hits: {result.CacheHitCount}");
```

By default the cache is stored below the platform's
`Environment.SpecialFolder.LocalApplicationData` directory. Set
`IconEuClientOptions.CacheDirectory` to use an application-specific location.

Downloads are written to temporary files and atomically moved into the cache
only after BZip2 decompression succeeds. Subsequent requests for the same run,
parameter, forecast hour and pressure level reuse the decompressed GRIB2 file.

For dependency-injection scenarios, pass an application-managed `HttpClient`:

```csharp
using var client = new IconEuClient(
    httpClient,
    new IconEuClientOptions
    {
        CacheDirectory = myCacheDirectory,
        MaxConcurrentDownloads = 4
    });
```

## Querying the loaded data

```csharp
var wind = result.Dataset.GetWindAtPressureAltitudeFeet(
    latitude: 50.90,
    longitude: 4.48,
    altitudeFeet: 34_000,
    validTimeUtc: DateTime.UtcNow);

if (wind is { } w)
{
    Console.WriteLine(
        $"{w.SpeedMetersPerSecond:F1} m/s from {w.DirectionFromDegrees:F0}°");
}
```

The interpolated APIs do not extrapolate. Requests outside the loaded
horizontal, pressure or forecast-time range return `null`.

Pressure-altitude helpers use ISA pressure altitude and pressure-level fields.
ICON hybrid/model-level vertical-coordinate reconstruction is not yet
implemented.

## AOT

The library enables the trimming and AOT analyzers and is exercised by a
`linux-x64` NativeAOT smoke application in CI. The smoke path includes GRIB
parsing, managed CCSDS/AEC decoding, interpolation, BZip2 decompression,
`HttpClient`-based ICON-EU ingestion and cache I/O.

## Next extensions

- ICON hybrid/model-level vertical-coordinate support
- Additional Product Definition Templates such as 4.8 for accumulated/statistical fields
- Cache retention/eviction policy
- Additional WMO parameter constants and aviation-derived products
- Route/profile weather sampling

See `THIRD_PARTY_NOTICES.md` for third-party attributions.
