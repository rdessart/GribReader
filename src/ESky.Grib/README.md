# ESky.Grib

Managed GRIB2 reader and DWD ICON-EU ingestion library for cross-platform
.NET/Avalonia applications.

## ICON-EU ingestion

`IconEuClient` supports:

- discovery of the latest published ICON-EU three-hour run;
- parsing DWD's actual pressure-level directory listings;
- selective T/U/V, pressure-level and forecast-hour requests;
- managed BZip2 decompression;
- persistent local caching;
- bounded parallel downloads;
- progress reporting;
- optional tolerance for missing files;
- direct construction of a `GribWeatherDataset`.

```csharp
using ESky.Grib.IconEu;

using var client = new IconEuClient();

var result = await client.LoadAsync(new IconEuRequest
{
    ForecastHours = [0, 3, 6],
    PressureLevelsHpa = [850, 700, 500, 300, 250],
    Parameters =
    [
        IconEuParameter.Temperature,
        IconEuParameter.UComponentOfWind,
        IconEuParameter.VComponentOfWind
    ]
});

var weather = result.Dataset;
```

An explicit run can be supplied through `IconEuRequest.RunUtc`; otherwise the
client discovers the latest available run.

## GRIB support

- GRIB edition 2
- Grid Definition Template 3.0: regular latitude/longitude
- Product Definition Template 4.0
- Data Representation Template 5.0: simple packing
- Data Representation Template 5.42: managed CCSDS/AEC decoding
- Section 6 bitmaps (indicator 0 and 255)
- multiple fields and concatenated GRIB2 messages
- synchronous and asynchronous Stream APIs

## Weather queries and interpolation

`GribWeatherDataset.GetInterpolatedValueAtValidTime` performs:

1. bilinear interpolation inside each regular latitude/longitude field;
2. logarithmic vertical interpolation for pressure surfaces;
3. linear interpolation between forecast valid times.

No extrapolation is performed.

Convenience APIs include `GetInterpolatedWind`,
`GetInterpolatedTemperature`, `GetWindAtPressureAltitudeFeet` and
`GetTemperatureAtPressureAltitudeFeet`.

## AOT

`ESky.Grib` declares `IsAotCompatible` and `IsTrimmable`, with trim/AOT
analyzers enabled. CI publishes and executes a NativeAOT smoke program that
also exercises the ICON-EU/BZip2 ingestion path.

## Not yet supported

- GRIB1
- rotated/projected/icosahedral grids
- Product Definition Templates other than 4.0
- complex packing (5.2/5.3)
- IEEE packing (5.4)
- JPEG2000 / PNG packing
- predefined bitmap tables
- ICON hybrid/model-level vertical-coordinate reconstruction
