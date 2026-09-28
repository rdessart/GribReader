# ESky.Grib

Dependency-free GRIB2 reader intended for cross-platform .NET/Avalonia applications.

## Supported

- GRIB edition 2
- Section 0 / 1 framing and identification
- Grid Definition Template 3.0: regular latitude/longitude
- Product Definition Template 4.0
- Data Representation Template 5.0: simple packing
- Data Representation Template 5.42: managed CCSDS/AEC decoding
- Section 6 bitmaps (indicator 0 and 255)
- Multiple fields per GRIB2 message
- Concatenated GRIB2 messages via `ReadAll` / `ReadAllAsync`
- Scan-order-aware `(i,j)` indexing
- O(1)-style nearest-grid lookup for regular latitude/longitude grids
- Bilinear horizontal interpolation
- Log-pressure interpolation on isobaric levels
- Linear interpolation between forecast valid times
- Stream and async Stream APIs
- Extensible grid/data decoder registries
- `GribWeatherDataset` for pressure-level temperature and wind queries
- ISA pressure-altitude convenience queries

## Interpolation

`GribWeatherDataset.GetInterpolatedValueAtValidTime` performs interpolation
in this order:

1. bilinear interpolation inside each regular latitude/longitude field;
2. vertical interpolation between bracketing levels;
3. linear interpolation between bracketing valid times.

Pressure surfaces (WMO surface type 100) are interpolated in
`ln(pressure)`. Other numeric vertical coordinates use linear interpolation.

No extrapolation is performed. Queries outside any available interpolation
domain return `null`.

`GetWindAtPressureAltitudeFeet` and
`GetTemperatureAtPressureAltitudeFeet` convert ISA pressure altitude to
pressure and then use pressure-level fields. This does not yet provide ICON
hybrid/model-level vertical-coordinate reconstruction.

## Not yet supported

- GRIB1
- Rotated/projected/icosahedral grids
- Product templates other than 4.0
- Complex packing (5.2/5.3)
- IEEE packing (5.4)
- JPEG2000 / PNG packing
- Predefined bitmap tables
- ICON hybrid/model-level vertical-coordinate reconstruction

## Example

```csharp
using ESky.Grib.Weather;

await using var stream = File.OpenRead("weather.grib2");
var messages = await new GribReader().ReadAllAsync(stream);
var weather = new GribWeatherDataset(messages);

var wind = weather.GetWindAtPressureAltitudeFeet(
    latitude: 50.9014,
    longitude: 4.4844,
    altitudeFeet: 34_000,
    validTimeUtc: DateTime.UtcNow);
```
