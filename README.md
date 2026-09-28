# GRIB Reader starter solution

A dependency-free GRIB2 reader core for a cross-platform Avalonia application.

## Projects

- `src/ESky.Grib` — parser + pluggable template decoders
- `tests/ESky.Grib.Tests` — xUnit tests using synthetic GRIB2 messages

## Build

```bash
dotnet test GribReader.slnx
```

## Initial scope

This intentionally starts with the most useful low-complexity path:

- Grid template 3.0 (regular latitude/longitude)
- Product template 4.0
- Data representation template 5.0 (simple packing)
- Optional Section 6 bitmap

The registry lets future implementations add grid/data templates without rewriting the message parser.
