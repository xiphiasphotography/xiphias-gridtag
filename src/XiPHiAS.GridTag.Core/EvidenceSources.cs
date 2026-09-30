using System.Globalization;

namespace GridTag.Core;

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
        if (string.IsNullOrWhiteSpace(observation.Model))
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

        var best = observations.Max(observation =>
            entry.Drivers.Any(driver => driver.Name.Contains(observation.Name, StringComparison.OrdinalIgnoreCase) ||
                                        observation.Name.Contains(driver.Name, StringComparison.OrdinalIgnoreCase))
                ? Math.Clamp(observation.Confidence, 0, 1)
                : 0.0);
        return best > 0 ? 1.0 + best * 0.5 : 1.0;
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
    }

    /// <inheritdoc />
    public string Name => "timing";

    /// <inheritdoc />
    public double GetWeight(string candidateNumber, EntryList entryList)
    {
        ArgumentNullException.ThrowIfNull(entryList);
        var expected = captureTime + clockOffset;
        var nearest = passingTimes
            .Where(row => string.Equals(NumberNormalizer.Normalize(row.Number), NumberNormalizer.Normalize(candidateNumber), StringComparison.Ordinal))
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
        foreach (var line in File.ReadLines(path).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var cells = line.Split(';', 2);
            if (cells.Length != 2 || !DateTime.TryParse(cells[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                throw new InvalidDataException("Timing CSV rows must be number;time.");
            rows.Add(new PassingTime(cells[0].Trim(), time));
        }
        return rows;
    }
}
