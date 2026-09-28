# Third-party notices

## Managed CCSDS/AEC decoder

The managed CCSDS 121.0-B decoder in `src/ESky.Grib/Internal/AecDecoder.cs`
is a C# port derived from the pure-Go libaec-compatible decoder in
`pspoerri/go-tiled-eccodes`:

https://github.com/pspoerri/go-tiled-eccodes

Copyright (c) 2026 Pascal Spörri

The source project is distributed under the MIT License:

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE.

The small `regular_ll_ccsds.grib2` integration fixture is sourced from the
same project and is covered by that project's MIT license.
