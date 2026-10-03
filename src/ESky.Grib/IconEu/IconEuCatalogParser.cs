using System.Globalization;

namespace ESky.Grib.IconEu;

internal readonly record struct IconEuCatalogEntry(
    string FileName,
    Uri Uri,
    DateTime RunUtc,
    int ForecastHour,
    int PressureLevelHpa,
    string ParameterToken);

internal static class IconEuCatalogParser
{
    private const string Prefix =
        "icon-eu_europe_regular-lat-lon_pressure-level_";
    private const string Suffix = ".grib2.bz2";

    public static IReadOnlyList<IconEuCatalogEntry> Parse(
        string html,
        Uri directoryUri)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(directoryUri);

        var entries = new List<IconEuCatalogEntry>();
        var position = 0;

        while (position < html.Length)
        {
            var hrefStart = html.IndexOf(
                "href=",
                position,
                StringComparison.OrdinalIgnoreCase);

            if (hrefStart < 0)
                break;

            var valueStart = hrefStart + 5;
            if (valueStart >= html.Length)
                break;

            var quote = html[valueStart];
            if (quote is not ('\'' or '"'))
            {
                position = valueStart + 1;
                continue;
            }

            valueStart++;
            var valueEnd = html.IndexOf(quote, valueStart);
            if (valueEnd < 0)
                break;

            var href = html[valueStart..valueEnd];
            position = valueEnd + 1;

            if (TryParseHref(href, directoryUri, out var entry))
                entries.Add(entry);
        }

        return entries;
    }

    private static bool TryParseHref(
        string href,
        Uri directoryUri,
        out IconEuCatalogEntry entry)
    {
        entry = default;

        var fileName = href;
        var query = fileName.IndexOfAny(['?', '#']);
        if (query >= 0)
            fileName = fileName[..query];

        var slash = fileName.LastIndexOf('/');
        if (slash >= 0)
            fileName = fileName[(slash + 1)..];

        fileName = Uri.UnescapeDataString(fileName);

        if (!fileName.StartsWith(Prefix, StringComparison.Ordinal) ||
            !fileName.EndsWith(Suffix, StringComparison.Ordinal))
        {
            return false;
        }

        var payload = fileName[
            Prefix.Length..
            ^Suffix.Length];

        var parts = payload.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
            return false;

        if (!TryParseRun(parts[0], out var runUtc) ||
            !int.TryParse(
                parts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var forecastHour) ||
            !int.TryParse(
                parts[2],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var pressureLevel))
        {
            return false;
        }

        entry = new IconEuCatalogEntry(
            fileName,
            new Uri(directoryUri, fileName),
            runUtc,
            forecastHour,
            pressureLevel,
            parts[3]);

        return true;
    }

    private static bool TryParseRun(string value, out DateTime runUtc)
    {
        runUtc = default;

        if (value.Length != 10 ||
            !int.TryParse(value.AsSpan(0, 4), out var year) ||
            !int.TryParse(value.AsSpan(4, 2), out var month) ||
            !int.TryParse(value.AsSpan(6, 2), out var day) ||
            !int.TryParse(value.AsSpan(8, 2), out var hour))
        {
            return false;
        }

        try
        {
            runUtc = new DateTime(
                year,
                month,
                day,
                hour,
                0,
                0,
                DateTimeKind.Utc);

            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
