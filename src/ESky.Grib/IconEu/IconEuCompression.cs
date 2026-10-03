using ICSharpCode.SharpZipLib.BZip2;

namespace ESky.Grib.IconEu;

internal static class IconEuCompression
{
    public static async Task DecompressBzip2Async(
        Stream compressed,
        Stream destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compressed);
        ArgumentNullException.ThrowIfNull(destination);

        using var bzip2 = new BZip2InputStream(compressed)
        {
            IsStreamOwner = false
        };

        await bzip2
            .CopyToAsync(destination, 128 * 1024, cancellationToken)
            .ConfigureAwait(false);
    }
}
