using System.Globalization;

namespace XiPHiAS.GridTag.Core;

/// <summary>One car-model observation produced by a vision classifier.</summary>
/// <param name="Model">Recognized model or make text.</param>
/// <param name="Confidence">Observation confidence in the range 0 to 1.</param>
public sealed record CarModelObservation(string Model, double Confidence);

/// <summary>Evidence based on a car-model classifier observation.</summary>
public sealed class CarModelEvidence : IEvidence
{
    private readonly CarModelObservation observation;

    /// <summary>Creates model evidence for one photo.</summary>
    public CarModelEvidence(CarModelObservation observation)
    {
        this.observation = observation ?? throw new ArgumentNullException(nameof(observation));
    }

    /// <inheritdoc />
    public string Name => "car_model";

    /// <inheritdoc />
    public double GetWeight(string candidateNumber, EntryList entryList)
    {
        ArgumentNullException.ThrowIfNull(entryList);
        if (string.IsNullOrWhiteSpace(observation.Model) || !double.IsFinite(observation.Confidence))
            return 1.0;
        if (!entryList.TryGetEntry(candidateNumber, out var entry))
            return 1.0;

        var model = observation.Model.Trim();
        var matches = entry.Car.Contains(model, StringComparison.OrdinalIgnoreCase) ||
                      model.Contains(entry.Car, StringComparison.OrdinalIgnoreCase);
        return matches ? 1.0 + Math.Clamp(observation.Confidence, 0, 1) * 0.5 : Math.Clamp(1.0 - observation.Confidence, 0, 1);
    }
}

/// <summary>Driver-name text observation produced by a visual/text recognizer.</summary>
/// <param name="Name">Recognized driver name text.</param>
/// <param name="Confidence">Observation confidence in the range 0 to 1.</param>
public sealed record DriverNameObservation(string Name, double Confidence);

/// <summary>Evidence based on a driver-name reader observation.</summary>
public sealed class DriverNameEvidence : IEvidence
{
    private readonly IReadOnlyList<DriverNameObservation> observations;

    /// <summary>Creates driver evidence for one or more recognized names.</summary>
    public DriverNameEvidence(IEnumerable<DriverNameObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        this.observations = observations.ToArray();
    }

    /// <inheritdoc />
    public string Name => "driver_name";

    /// <inheritdoc />
    public double GetWeight(string candidateNumber, EntryList entryList)
    {
        ArgumentNullException.ThrowIfNull(entryList);
        if (!entryList.TryGetEntry(candidateNumber, out var entry) || observations.Count == 0)
            return 1.0;

        var best = observations.Where(observation => !string.IsNullOrWhiteSpace(observation.Name) && double.IsFinite(observation.Confidence)).Select(observation =>
            entry.Drivers.Any(driver => MatchesName(observation.Name, driver.Name, entryList))
                ? Math.Clamp(observation.Confidence, 0, 1)
                : 0.0).DefaultIfEmpty(0.0).Max();
        return best > 0 ? 1.0 + best * 0.5 : 1.0;
    }

    private static bool MatchesName(string observed, string driverName, EntryList entryList)
    {
        static string[] Words(string text) =>
            new string(text.Select(character => char.IsLetter(character) ? char.ToUpperInvariant(character) : ' ').ToArray())
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var read = Words(observed);
        var driver = Words(driverName);
        if (driver.Length == 0 || read.Length == 0)
            return false;
        if (read.SequenceEqual(driver))
            return true;
        // Short fragments and sponsor text cannot support a driver. A surname must be unique in the event.
        var surname = driver[^1];
        if (!(read.Length == 1 && read[0] == surname) &&
            !(read.Length == 2 && read[0].Length == 1 && read[0][0] == driver[0][0] && read[1] == surname))
            return false;
        return entryList.Entries.SelectMany(entry => entry.Drivers)
            .Where(candidate => Words(candidate.Name).LastOrDefault() == surname)
            .Select(candidate => string.Join(' ', Words(candidate.Name))).Distinct().Count() == 1;
    }
}

/// <summary>Combines independent evidence sources by multiplying their weights.</summary>
public sealed class CompositeEvidence : IEvidence
{
    private readonly IReadOnlyList<IEvidence> sources;

    /// <summary>Creates a composite evidence source.</summary>
    public CompositeEvidence(IEnumerable<IEvidence> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        this.sources = sources.ToArray();
    }

    /// <inheritdoc />
    public string Name => string.Join("+", sources.Select(source => source.Name));

    /// <summary>Individual sources, flattened so a strong conflict cannot be hidden by supporting evidence.</summary>
    public IReadOnlyList<IEvidence> Sources => sources.SelectMany(source =>
        source is CompositeEvidence composite ? composite.Sources : new[] { source }).ToArray();

    /// <inheritdoc />
    public double GetWeight(string candidateNumber, EntryList entryList) =>
        sources.Aggregate(1.0, (weight, source) => weight * source.GetWeight(candidateNumber, entryList));
}

/// <summary>A passing-time row used by timing cross-check evidence.</summary>
/// <param name="Number">Normalized or raw car number.</param>
/// <param name="Time">Passing time without an assumed timezone.</param>
public sealed record PassingTime(string Number, DateTime Time);

/// <summary>Evidence from passing times with a configurable camera-clock offset.</summary>
public sealed class TimingCrossCheckEvidence : IEvidence
{
    private readonly DateTime captureTime;
    private readonly IReadOnlyList<PassingTime> passingTimes;
    private readonly TimeSpan clockOffset;
    private readonly TimeSpan tolerance;

    /// <summary>Creates timing evidence for one photo.</summary>
    public TimingCrossCheckEvidence(
        DateTime captureTime,
        IEnumerable<PassingTime> passingTimes,
        TimeSpan clockOffset,
        TimeSpan? tolerance = null)
    {
        this.captureTime = captureTime;
        this.passingTimes = passingTimes?.ToArray() ?? throw new ArgumentNullException(nameof(passingTimes));
        this.clockOffset = clockOffset;
        this.tolerance = tolerance ?? TimeSpan.FromSeconds(2);
        if (this.tolerance <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "Timing tolerance must be positive.");
    }

    /// <inheritdoc />
    public string Name => "timing";

    /// <inheritdoc />
    public double GetWeight(string candidateNumber, EntryList entryList)
    {
        ArgumentNullException.ThrowIfNull(entryList);
        if (captureTime == default || !entryList.TryGetEntry(candidateNumber, out _))
            return 1.0;
        // Compare wall clocks, consistent with session and burst handling. An invalid
        // corrected clock is unavailable evidence, rather than a participant conflict.
        var correctedTicks = (decimal)captureTime.Ticks + clockOffset.Ticks;
        if (correctedTicks < DateTime.MinValue.Ticks || correctedTicks > DateTime.MaxValue.Ticks)
            return 1.0;
        var expected = new DateTime((long)correctedTicks, DateTimeKind.Unspecified);
        var nearest = passingTimes
            .Where(row => row.Time != default && string.Equals(NumberNormalizer.Normalize(row.Number), NumberNormalizer.Normalize(candidateNumber), StringComparison.Ordinal))
            .Select(row => Math.Abs((row.Time - expected).TotalSeconds))
            .DefaultIfEmpty(double.MaxValue)
            .Min();
        if (nearest == double.MaxValue)
            return 1.0;
        return nearest <= tolerance.TotalSeconds ? 1.0 + 0.5 * (1.0 - nearest / tolerance.TotalSeconds) : 0.1;
    }

    /// <summary>Loads passing-time rows from a semicolon CSV: number;time.</summary>
    public static IReadOnlyList<PassingTime> LoadCsv(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Timing CSV '{path}' was not found.", path);
        var rows = new List<PassingTime>();
        using var reader = new StreamReader(path);
        using var csv = SemicolonCsvReader.Read(reader).GetEnumerator();
        if (!csv.MoveNext() || !csv.Current.Select(cell => cell.Trim().ToLowerInvariant())
            .SequenceEqual(new[] { "number", "time" }))
            throw new InvalidDataException("Timing CSV header must be number;time.");
        var rowIndex = 1;
        while (csv.MoveNext())
        {
            rowIndex++;
            var cells = csv.Current;
            // Explicit formats prevent culture-dependent dates and time-only rows.
            string[] formats = ["yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
                "yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
                "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.FFFFFFF"];
            if (cells.Length != 2 || NumberNormalizer.Normalize(cells[0]).Length == 0 ||
                !DateTimeOffset.TryParseExact(cells[1].Trim(), formats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var timestamp) || timestamp.DateTime == default)
                throw new InvalidDataException($"Timing CSV row {rowIndex} must contain a number and a full ISO date/time.");
            rows.Add(new PassingTime(NumberNormalizer.Normalize(cells[0]), timestamp.DateTime));
        }
        return rows;
    }
}
