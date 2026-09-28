using ESky.Grib.Models;
using ESky.Grib.Weather;

namespace ESky.Grib.IconEu;

public sealed class IconEuClient : IDisposable
{
    private static readonly int[] CycleHours =
        [0, 3, 6, 9, 12, 15, 18, 21];

    private readonly HttpClient _httpClient;
    private readonly IconEuClientOptions _options;
    private readonly bool _ownsHttpClient;

    public IconEuClient(IconEuClientOptions? options = null)
        : this(new HttpClient(), options, ownsHttpClient: true)
    {
    }

    public IconEuClient(
        HttpClient httpClient,
        IconEuClientOptions? options = null)
        : this(httpClient, options, ownsHttpClient: false)
    {
    }

    private IconEuClient(
        HttpClient httpClient,
        IconEuClientOptions? options,
        bool ownsHttpClient)
    {
        _httpClient = httpClient ??
            throw new ArgumentNullException(nameof(httpClient));

        _options = options ?? new IconEuClientOptions();
        _ownsHttpClient = ownsHttpClient;

        if (!_options.BaseUri.IsAbsoluteUri)
            throw new ArgumentException(
                "ICON-EU BaseUri must be absolute.",
                nameof(options));

        if (_options.MaxConcurrentDownloads <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MaxConcurrentDownloads must be greater than zero.");

        if (_options.DiscoveryLookbackCycles <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "DiscoveryLookbackCycles must be greater than zero.");
    }

    public async Task<DateTime> DiscoverLatestRunAsync(
        CancellationToken cancellationToken = default)
    {
        var now = _options.TimeProvider
            .GetUtcNow()
            .UtcDateTime;

        var roundedHour = now.Hour - now.Hour % 3;
        var newestCandidate = new DateTime(
            now.Year,
            now.Month,
            now.Day,
            roundedHour,
            0,
            0,
            DateTimeKind.Utc);

        var cycleCandidates = Enumerable
            .Range(0, _options.DiscoveryLookbackCycles)
            .Select(index => newestCandidate.AddHours(-3 * index))
            .GroupBy(candidate => candidate.Hour)
            .Select(group => group.First())
            .ToArray();

        var tasks = cycleCandidates
            .Select(candidate =>
                TryReadCatalogAsync(
                    candidate.Hour,
                    IconEuParameter.Temperature,
                    cancellationToken))
            .ToArray();

        var catalogs = await Task
            .WhenAll(tasks)
            .ConfigureAwait(false);

        var latest = catalogs
            .Where(catalog => catalog is not null)
            .SelectMany(catalog => catalog!)
            .Select(entry => entry.RunUtc)
            .Where(run => run <= now)
            .DefaultIfEmpty()
            .Max();

        if (latest == default)
        {
            throw new IconEuDataNotAvailableException(
                "No recent ICON-EU pressure-level run could be discovered from DWD Open Data.");
        }

        return latest;
    }

    public async Task<IconEuLoadResult> LoadAsync(
        IconEuRequest request,
        IProgress<IconEuLoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        progress?.Report(new IconEuLoadProgress(
            IconEuLoadStage.DiscoveringRun,
            0,
            1));

        var runUtc = request.RunUtc is { } requestedRun
            ? NormalizeRun(requestedRun)
            : await DiscoverLatestRunAsync(cancellationToken)
                .ConfigureAwait(false);

        progress?.Report(new IconEuLoadProgress(
            IconEuLoadStage.DiscoveringRun,
            1,
            1));

        var parameters = request.Parameters
            .Distinct()
            .ToArray();

        progress?.Report(new IconEuLoadProgress(
            IconEuLoadStage.ReadingCatalog,
            0,
            parameters.Length));

        var catalogTasks = parameters
            .Select(async parameter =>
            {
                var catalog = await ReadCatalogAsync(
                        runUtc.Hour,
                        parameter,
                        cancellationToken)
                    .ConfigureAwait(false);

                return (parameter, catalog);
            })
            .ToArray();

        var catalogResults = await Task
            .WhenAll(catalogTasks)
            .ConfigureAwait(false);

        var catalogs = new Dictionary<
            IconEuParameter,
            IReadOnlyList<IconEuCatalogEntry>>();

        for (var i = 0; i < catalogResults.Length; i++)
        {
            var result = catalogResults[i];
            catalogs[result.parameter] = result.catalog;

            progress?.Report(new IconEuLoadProgress(
                IconEuLoadStage.ReadingCatalog,
                i + 1,
                catalogResults.Length));
        }

        var requestedFiles = new List<RequestedFile>();
        var missingFiles = new List<IconEuMissingFile>();

        foreach (var parameter in parameters)
        {
            var token = parameter.FileToken();

            var lookup = catalogs[parameter]
                .Where(entry =>
                    entry.RunUtc == runUtc &&
                    string.Equals(
                        entry.ParameterToken,
                        token,
                        StringComparison.OrdinalIgnoreCase))
                .ToDictionary(
                    entry => (
                        entry.ForecastHour,
                        entry.PressureLevelHpa));

            foreach (var forecastHour in request.ForecastHours.Distinct())
            {
                foreach (var pressureLevel in request.PressureLevelsHpa.Distinct())
                {
                    if (lookup.TryGetValue(
                            (forecastHour, pressureLevel),
                            out var entry))
                    {
                        requestedFiles.Add(new RequestedFile(
                            parameter,
                            entry));
                    }
                    else
                    {
                        missingFiles.Add(new IconEuMissingFile(
                            parameter,
                            forecastHour,
                            pressureLevel));
                    }
                }
            }
        }

        if (missingFiles.Count > 0 && !request.AllowMissingFiles)
        {
            var first = missingFiles[0];

            throw new IconEuDataNotAvailableException(
                $"DWD does not list all requested ICON-EU data for run {runUtc:yyyy-MM-dd HH}:00 UTC. " +
                $"First missing file: {first.Parameter}, forecast +{first.ForecastHour} h, {first.PressureLevelHpa} hPa. " +
                $"Missing count: {missingFiles.Count}.");
        }

        var total = requestedFiles.Count;

        progress?.Report(new IconEuLoadProgress(
            IconEuLoadStage.Downloading,
            0,
            total));

        Directory.CreateDirectory(_options.CacheDirectory);

        using var semaphore = new SemaphoreSlim(
            _options.MaxConcurrentDownloads);

        var completed = 0;
        var fileTasks = requestedFiles
            .Select(async requested =>
            {
                await semaphore
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);

                try
                {
                    var cached = await EnsureCachedAsync(
                            runUtc,
                            requested,
                            cancellationToken)
                        .ConfigureAwait(false);

                    var done = Interlocked.Increment(ref completed);

                    progress?.Report(new IconEuLoadProgress(
                        IconEuLoadStage.Downloading,
                        done,
                        total,
                        requested.Entry.FileName));

                    return cached;
                }
                finally
                {
                    semaphore.Release();
                }
            })
            .ToArray();

        var cachedFiles = await Task
            .WhenAll(fileTasks)
            .ConfigureAwait(false);

        var messages = new List<GribMessage>();
        var reader = new GribReader();

        progress?.Report(new IconEuLoadProgress(
            IconEuLoadStage.Decoding,
            0,
            cachedFiles.Length));

        for (var i = 0; i < cachedFiles.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var stream = new FileStream(
                cachedFiles[i].Path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous |
                FileOptions.SequentialScan);

            messages.AddRange(reader.ReadAll(stream));

            progress?.Report(new IconEuLoadProgress(
                IconEuLoadStage.Decoding,
                i + 1,
                cachedFiles.Length,
                Path.GetFileName(cachedFiles[i].Path)));
        }

        progress?.Report(new IconEuLoadProgress(
            IconEuLoadStage.Completed,
            cachedFiles.Length,
            cachedFiles.Length));

        return new IconEuLoadResult
        {
            RunUtc = runUtc,
            Dataset = new GribWeatherDataset(messages),
            FileCount = cachedFiles.Length,
            DownloadedFileCount =
                cachedFiles.Count(file => !file.WasCacheHit),
            CacheHitCount =
                cachedFiles.Count(file => file.WasCacheHit),
            MissingFiles = missingFiles
        };
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    private async Task<CachedFile> EnsureCachedAsync(
        DateTime runUtc,
        RequestedFile requested,
        CancellationToken cancellationToken)
    {
        var parameterDirectory =
            requested.Parameter.DirectoryName();

        var cacheDirectory = Path.Combine(
            _options.CacheDirectory,
            runUtc.ToString("yyyyMMddHH"),
            parameterDirectory);

        Directory.CreateDirectory(cacheDirectory);

        var cachePath = Path.Combine(
            cacheDirectory,
            requested.Entry.FileName[..^4]); // remove .bz2

        var info = new FileInfo(cachePath);
        if (info.Exists && info.Length >= 20)
            return new CachedFile(cachePath, WasCacheHit: true);

        var tempPath = cachePath +
            "." +
            Guid.NewGuid().ToString("N") +
            ".tmp";

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                requested.Entry.Uri);

            request.Headers.UserAgent.ParseAdd(
                "ESky.Grib/1.0 (+https://github.com/rdessart/GribReader)");

            using var response = await _httpClient
                .SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            await using var compressed = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            await using (var destination = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous |
                FileOptions.SequentialScan))
            {
                await IconEuCompression
                    .DecompressBzip2Async(
                        compressed,
                        destination,
                        cancellationToken)
                    .ConfigureAwait(false);

                await destination
                    .FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            if (new FileInfo(tempPath).Length < 20)
            {
                throw new InvalidDataException(
                    $"Decompressed ICON-EU file '{requested.Entry.FileName}' is unexpectedly short.");
            }

            File.Move(tempPath, cachePath, overwrite: true);

            return new CachedFile(
                cachePath,
                WasCacheHit: false);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private async Task<IReadOnlyList<IconEuCatalogEntry>> ReadCatalogAsync(
        int cycleHour,
        IconEuParameter parameter,
        CancellationToken cancellationToken)
    {
        var catalog = await TryReadCatalogAsync(
                cycleHour,
                parameter,
                cancellationToken)
            .ConfigureAwait(false);

        return catalog ??
            throw new IconEuDataNotAvailableException(
                $"Unable to read the DWD ICON-EU {cycleHour:00} UTC '{parameter.DirectoryName()}' catalog.");
    }

    private async Task<IReadOnlyList<IconEuCatalogEntry>?> TryReadCatalogAsync(
        int cycleHour,
        IconEuParameter parameter,
        CancellationToken cancellationToken)
    {
        try
        {
            var directoryUri = GetDirectoryUri(
                cycleHour,
                parameter);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                directoryUri);

            request.Headers.UserAgent.ParseAdd(
                "ESky.Grib/1.0 (+https://github.com/rdessart/GribReader)");

            using var response = await _httpClient
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return null;

            var html = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            return IconEuCatalogParser.Parse(
                html,
                directoryUri);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private Uri GetDirectoryUri(
        int cycleHour,
        IconEuParameter parameter) =>
        new(
            _options.BaseUri,
            $"{cycleHour:00}/{parameter.DirectoryName()}/");

    private static void ValidateRequest(IconEuRequest request)
    {
        if (request.Parameters.Count == 0)
            throw new ArgumentException(
                "At least one ICON-EU parameter must be requested.",
                nameof(request));

        if (request.ForecastHours.Count == 0 ||
            request.ForecastHours.Any(hour => hour < 0))
        {
            throw new ArgumentException(
                "Forecast hours must contain at least one non-negative value.",
                nameof(request));
        }

        if (request.PressureLevelsHpa.Count == 0 ||
            request.PressureLevelsHpa.Any(level => level <= 0))
        {
            throw new ArgumentException(
                "Pressure levels must contain at least one positive hPa value.",
                nameof(request));
        }
    }

    private static DateTime NormalizeRun(DateTime runUtc)
    {
        runUtc = runUtc.Kind == DateTimeKind.Utc
            ? runUtc
            : runUtc.ToUniversalTime();

        if (Array.IndexOf(CycleHours, runUtc.Hour) < 0 ||
            runUtc.Minute != 0 ||
            runUtc.Second != 0 ||
            runUtc.Millisecond != 0)
        {
            throw new ArgumentException(
                "ICON-EU run time must be an exact 3-hour UTC cycle (00, 03, 06, ..., 21).",
                nameof(runUtc));
        }

        return runUtc;
    }

    private readonly record struct RequestedFile(
        IconEuParameter Parameter,
        IconEuCatalogEntry Entry);

    private readonly record struct CachedFile(
        string Path,
        bool WasCacheHit);
}
