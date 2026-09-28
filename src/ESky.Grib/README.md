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
- Stream and async Stream APIs
- Extensible grid/data decoder registries
- `GribWeatherDataset` for pressure-level temperature and wind queries

## Not yet supported

- GRIB1
- Rotated/projected/icosahedral grids
- Product templates other than 4.0
- Complex packing (5.2/5.3)
- IEEE packing (5.4)
- JPEG2000 / PNG packing
- Predefined bitmap tables
- Spatial/time/vertical interpolation

## Example

```csharp
using ESky.Grib.Weather;

await using var stream = File.OpenRead("weather.grib2");
var messages = await new GribReader().ReadAllAsync(stream);
var weather = new GribWeatherDataset(messages);

var wind = weather.GetWind(
    50.9014,
    4.4844,
    pressureHpa: 500,
    forecastOffset: TimeSpan.FromHours(6));
```
