namespace GridTag.Core;

/// <summary>Processes one manifest item through manual, no-preview, detection and matching stages.</summary>
public sealed class TaggingPipeline
{
    private readonly EntryList entryList;
    private readonly EventContext eventContext;
    private readonly IRawPreviewProvider rawPreviewProvider;
    private readonly ICarDetector carDetector;
    private readonly IPlateReader plateReader;
    private readonly NumberMatcher matcher;
    private readonly FieldBuilder fieldBuilder;
    private readonly Func<ManifestPhoto, IEvidence?>? evidenceProvider;

    /// <summary>Creates a pipeline from the event context and the vision interfaces.</summary>
    public TaggingPipeline(
        EntryList entryList,
        EventContext eventContext,
        IRawPreviewProvider rawPreviewProvider,
        ICarDetector carDetector,
        IPlateReader plateReader,
        NumberMatcher? matcher = null,
        FieldBuilder? fieldBuilder = null,
        Func<ManifestPhoto, IEvidence?>? evidenceProvider = null)
    {
        this.entryList = entryList ?? throw new ArgumentNullException(nameof(entryList));
        this.eventContext = eventContext ?? throw new ArgumentNullException(nameof(eventContext));
        this.rawPreviewProvider = rawPreviewProvider ?? throw new ArgumentNullException(nameof(rawPreviewProvider));
        this.carDetector = carDetector ?? throw new ArgumentNullException(nameof(carDetector));
        this.plateReader = plateReader ?? throw new ArgumentNullException(nameof(plateReader));
        this.matcher = matcher ?? new NumberMatcher();
        this.fieldBuilder = fieldBuilder ?? new FieldBuilder();
        this.evidenceProvider = evidenceProvider;
    }

    /// <summary>Processes an entire manifest and returns the result file.</summary>
    public ResultFile Process(Manifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var photos = manifest.Photos.Select(ProcessPhoto).ToArray();
        return new ResultFile(1, "0.1.0", DateTimeOffset.UtcNow, photos);
    }

    /// <summary>Processes a single manifest photo in the same way the CLI would.</summary>
    public PhotoResult ProcessPhoto(ManifestPhoto photo)
    {
        ArgumentNullException.ThrowIfNull(photo);

        var session = ResolveSession(photo);
        try
        {
            if (!string.IsNullOrWhiteSpace(photo.ManualNumber))
                return ProcessManualPhoto(photo, session);

            var preview = rawPreviewProvider.GetPreview(photo.Path);
            if (preview is null)
                return new PhotoResult(photo.Id, "error", ["no_preview"], session, []);

            var detections = carDetector.Detect(preview).ToArray();
            if (detections.Length == 0)
                return new PhotoResult(photo.Id, "noCar", ["no_car_detected"], session, []);

            var resolution = ResolveCars(photo, preview, detections);
            return BuildAutoResult(photo.Id, session, resolution.ResolvedCars, resolution.Reasons, resolution.LargestResolved);
        }
        catch (Exception ex)
        {
            return new PhotoResult(photo.Id, "error", [$"exception:{ex.GetType().Name}:{ex.Message}"], session, []);
        }
    }

    /// <summary>Reads and matches every detection, flagging whether the largest detection was resolved.</summary>
    private CarResolution ResolveCars(ManifestPhoto photo, IPreview preview, IReadOnlyList<DetectedCar> detections)
    {
        var orderedDetections = detections.OrderByDescending(detection => detection.Confidence).ToArray();
        var largestDetection = orderedDetections[0];
        var resolvedCars = new List<CarCandidate>();
        var reasons = new List<string>();
        var largestResolved = false;

        foreach (var detection in orderedDetections)
        {
            var hypotheses = plateReader.ReadNumbers(preview, detection);
            if (hypotheses.Count == 0)
            {
                if (ReferenceEquals(detection, largestDetection))
                    reasons.Add("largest_car_unresolved");
                continue;
            }

            var match = matcher.Match(entryList, hypotheses, evidenceProvider?.Invoke(photo));
            if (match.Status == MatchStatus.Auto)
            {
                if (!entryList.TryGetEntry(match.BestNumber!, out _))
                    continue;

                var isPrimary = ReferenceEquals(detection, largestDetection);
                if (isPrimary)
                    largestResolved = true;
                resolvedCars.Add(new CarCandidate(match.BestNumber!, match.BestProbability, "ocr", isPrimary));
                continue;
            }

            if (ReferenceEquals(detection, largestDetection))
                reasons.AddRange(match.Reasons);
        }

        return new CarResolution(resolvedCars, reasons, largestResolved);
    }

    /// <summary>Turns a resolution into the final auto or review result for one photo.</summary>
    private PhotoResult BuildAutoResult(int photoId, string? session, IReadOnlyList<CarCandidate> resolvedCars, IReadOnlyList<string> reasons, bool largestResolved)
    {
        if (resolvedCars.Count == 0)
        {
            return new PhotoResult(photoId, "review", reasons.Count > 0 ? reasons : ["largest_car_unresolved"], session, []);
        }

        if (!largestResolved)
        {
            return new PhotoResult(photoId, "review", reasons.Count > 0 ? reasons : ["largest_car_unresolved"], session, resolvedCars);
        }

        var primary = resolvedCars.OrderByDescending(car => car.Confidence).First();
        if (!entryList.TryGetEntry(primary.Number, out _))
            return new PhotoResult(photoId, "review", ["no_entry_match"], session, resolvedCars);

        var fields = BuildCombinedFields(resolvedCars, session);
        return new PhotoResult(
            photoId,
            "auto",
            [],
            session,
            resolvedCars.Select(car => new CarCandidate(car.Number, car.Confidence, car.Source, car.Number == primary.Number)).ToArray(),
            fields);
    }

    private PhotoResult ProcessManualPhoto(ManifestPhoto photo, string? session)
    {
        var values = photo.ManualNumber!
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var recognized = new List<CarCandidate>();
        var reasons = new List<string>();

        foreach (var value in values)
        {
            var normalized = NumberNormalizer.Normalize(value);
            if (!entryList.TryGetEntry(normalized, out var entry))
            {
                reasons.Add($"unknown_number:{normalized}");
                continue;
            }

            recognized.Add(new CarCandidate(entry.Number, 1.0, "manual", recognized.Count == 0));
        }

        if (recognized.Count == 0)
            return new PhotoResult(photo.Id, "review", reasons, session, []);

        var primaryEntry = entryList.TryGetEntry(recognized[0].Number, out var resolvedPrimary)
            ? resolvedPrimary
            : throw new InvalidOperationException($"Manual number '{recognized[0].Number}' was accepted but was not found in the entry list.");

        var fields = BuildCombinedFields(recognized, session);
        return new PhotoResult(
            photo.Id,
            "manual",
            reasons,
            session,
            recognized,
            fields);
    }

    private GeneratedFields BuildCombinedFields(IReadOnlyList<CarCandidate> cars, string? session)
    {
        var primary = cars.First(car => car.Primary);
        var sources = new List<CarKeywordSource>();
        var persons = new List<string>();
        var seenPersons = new HashSet<string>(StringComparer.Ordinal);

        foreach (var car in cars)
        {
            if (!entryList.TryGetEntry(car.Number, out var carEntry))
                continue;

            sources.Add(new CarKeywordSource(carEntry));

            foreach (var driver in carEntry.Drivers)
            {
                if (seenPersons.Add(driver.Name))
                    persons.Add(driver.Name);
            }
        }

        if (!entryList.TryGetEntry(primary.Number, out var resolvedPrimary))
            throw new InvalidOperationException($"Resolved number '{primary.Number}' was not found.");

        var sessionName = SessionResolver.ResolveName(session, eventContext);
        var keywords = KeywordAssembler.Assemble(sources, sessionName);
        var primaryFields = fieldBuilder.Build(resolvedPrimary, eventContext, session);
        return primaryFields with { Keywords = keywords, Persons = persons.ToArray() };
    }

    private string? ResolveSession(ManifestPhoto photo)
    {
        var resolver = new SessionResolver();
        var capture = photo.CaptureTime;
        return resolver.Resolve(capture, eventContext);
    }
}

/// <summary>Decoded preview passed between the vision stages; width and height are the pixel dimensions.</summary>
public interface IPreview
{
    /// <summary>The decoded preview width in pixels.</summary>
    int Width { get; }

    /// <summary>The decoded preview height in pixels.</summary>
    int Height { get; }
}

/// <summary>Contract for a preview provider used by the pipeline.</summary>
public interface IRawPreviewProvider
{
    /// <summary>Retrieves the decoded preview for a photo path, or null when no preview is available.</summary>
    IPreview? GetPreview(string path);
}

/// <summary>Contract for the car detector used by the pipeline.</summary>
public interface ICarDetector
{
    /// <summary>Detects cars inside a preview.</summary>
    IReadOnlyList<DetectedCar> Detect(IPreview preview);
}

/// <summary>Contract for the number-reading step on a detected car.</summary>
public interface IPlateReader
{
    /// <summary>Reads all candidate numbers for one detected car from the preview.</summary>
    IReadOnlyList<NumberHypothesis> ReadNumbers(IPreview preview, DetectedCar detectedCar);
}

/// <summary>Resolved cars and review reasons collected while scanning one photo's detections.</summary>
/// <param name="ResolvedCars">Cars whose number matched an entry-list row.</param>
/// <param name="Reasons">Review reasons contributed by the largest detection.</param>
/// <param name="LargestResolved">Whether the largest detection itself resolved.</param>
internal sealed record CarResolution(IReadOnlyList<CarCandidate> ResolvedCars, IReadOnlyList<string> Reasons, bool LargestResolved);
