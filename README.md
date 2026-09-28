# GribReader

A dependency-free GRIB2 reader core for cross-platform .NET and Avalonia applications.

## Projects

- `src/ESky.Grib` — GRIB2 parser, models and pluggable template decoders
- `tests/ESky.Grib.Tests` — xUnit tests using synthetic messages and small real GRIB2 fixtures

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

Template 5.42 is implemented entirely in managed C#, without a native
`libaec` dependency. This keeps the core library usable on Avalonia desktop,
iOS and Android targets.

## ICON-EU

Current DWD ICON/ICON-EU Open Data uses CCSDS packing (GRIB2 template 5.42).
The reader can therefore decode the regular-latitude/longitude instantaneous
fields needed for an initial aviation weather layer, provided the GRIB message
uses Product Definition Template 4.0.

DWD distributes many files as `.grib2.bz2`. BZip2 transport decompression is
outside the GRIB format and is intentionally not part of this core reader:
pass the decompressed GRIB2 stream to `GribReader`.

## Example

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

## Next extensions

- Additional Product Definition Templates such as 4.8 for accumulated/statistical fields
- Additional grid definitions where useful
- Higher-level weather dataset queries (wind/temperature by position, pressure and forecast time)
- Additional packing templates as real data requires them

See `THIRD_PARTY_NOTICES.md` for attribution for the managed AEC decoder and
the CCSDS integration fixture.
