using ESky.Grib.Weather;

namespace ESky.Grib.IconEu;

public enum IconEuParameter
{
    Temperature,
    UComponentOfWind,
    VComponentOfWind
}

public static class IconEuParameters
{
    public static GribParameter ToGribParameter(this IconEuParameter parameter) =>
        parameter switch
        {
            IconEuParameter.Temperature => GribParameters.Temperature,
            IconEuParameter.UComponentOfWind => GribParameters.UComponentOfWind,
            IconEuParameter.VComponentOfWind => GribParameters.VComponentOfWind,
            _ => throw new ArgumentOutOfRangeException(nameof(parameter))
        };

    internal static string DirectoryName(this IconEuParameter parameter) =>
        parameter switch
        {
            IconEuParameter.Temperature => "t",
            IconEuParameter.UComponentOfWind => "u",
            IconEuParameter.VComponentOfWind => "v",
            _ => throw new ArgumentOutOfRangeException(nameof(parameter))
        };

    internal static string FileToken(this IconEuParameter parameter) =>
        parameter switch
        {
            IconEuParameter.Temperature => "T",
            IconEuParameter.UComponentOfWind => "U",
            IconEuParameter.VComponentOfWind => "V",
            _ => throw new ArgumentOutOfRangeException(nameof(parameter))
        };
}

public sealed class IconEuRequest
{
    public IReadOnlyCollection<int> ForecastHours { get; init; } = [0, 3, 6, 9, 12];

    public IReadOnlyCollection<int> PressureLevelsHpa { get; init; } =
        [1000, 925, 850, 700, 500, 400, 300, 250, 200];

    public IReadOnlyCollection<IconEuParameter> Parameters { get; init; } =
    [
        IconEuParameter.Temperature,
        IconEuParameter.UComponentOfWind,
        IconEuParameter.VComponentOfWind
    ];

    /// <summary>
    /// Optional explicit ICON-EU initialization time. When null, the latest
    /// discoverable run is used.
    /// </summary>
    public DateTime? RunUtc { get; init; }

    /// <summary>
    /// When false, any requested pressure/forecast/parameter file missing from
    /// the DWD listing causes the load to fail.
    /// </summary>
    public bool AllowMissingFiles { get; init; }
}

public sealed class IconEuClientOptions
{
    public Uri BaseUri { get; init; } =
        new("https://opendata.dwd.de/weather/nwp/icon-eu/grib/");

    public string CacheDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ESky.Grib",
        "icon-eu");

    public int MaxConcurrentDownloads { get; init; } = 4;

    public int DiscoveryLookbackCycles { get; init; } = 8;

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}

public enum IconEuLoadStage
{
    DiscoveringRun,
    ReadingCatalog,
    Downloading,
    Decoding,
    Completed
}

public readonly record struct IconEuLoadProgress(
    IconEuLoadStage Stage,
    int Completed,
    int Total,
    string? CurrentFile = null);

public sealed record IconEuMissingFile(
    IconEuParameter Parameter,
    int ForecastHour,
    int PressureLevelHpa);

public sealed class IconEuLoadResult
{
    public required DateTime RunUtc { get; init; }
    public required GribWeatherDataset Dataset { get; init; }
    public required int FileCount { get; init; }
    public required int DownloadedFileCount { get; init; }
    public required int CacheHitCount { get; init; }
    public required IReadOnlyList<IconEuMissingFile> MissingFiles { get; init; }
}

public sealed class IconEuDataNotAvailableException : GribException
{
    public IconEuDataNotAvailableException(string message) : base(message)
    {
    }
}
