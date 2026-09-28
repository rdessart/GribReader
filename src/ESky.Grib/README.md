# ESky.Grib

Small, dependency-free GRIB2 reader intended for cross-platform .NET/Avalonia applications.

## Supported in this initial version

- GRIB edition 2
- Section 0 / 1 framing and identification
- Grid Definition Template 3.0: regular latitude/longitude
- Product Definition Template 4.0
- Data Representation Template 5.0: simple packing
- Section 6 bitmaps (indicator 0 and 255)
- Section 7 bit unpacking
- Multiple fields per GRIB2 message
- Concatenated GRIB2 messages via `ReadAll` / `ReadAllAsync`
- Scan-order aware `(i,j)` indexing
- Stream and async Stream APIs
- Extensible grid/data decoder registries

## Not yet supported

- GRIB1
- Rotated/projected/icosahedral grids
- Product templates other than 4.0
- Complex packing (5.2/5.3)
- IEEE packing (5.4)
- JPEG2000 / PNG packing
- Predefined bitmap tables

## Example

```csharp
await using var stream = File.OpenRead("weather.grib2");
var message = await new GribReader().ReadAsync(stream);

foreach (var field in message.Fields)
{
    if (field.Grid is RegularLatLonGrid grid)
    {
        var nearest = field.GetNearest(50.9014, 4.4844);
        Console.WriteLine($"{nearest.Latitude:F3}, {nearest.Longitude:F3}: {nearest.Value}");
    }
}
```
