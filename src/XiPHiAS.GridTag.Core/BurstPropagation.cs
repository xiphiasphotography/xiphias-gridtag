namespace XiPHiAS.GridTag.Core;

/// <summary>One neighbouring frame with a measured visual similarity to a source frame.</summary>
/// <param name="Photo">Frame metadata including EXIF capture time.</param>
/// <param name="Similarity">Similarity score in the range 0 to 1.</param>
public sealed record BurstFrame(ManifestPhoto Photo, double Similarity);

/// <summary>A number proposal that must remain review-only.</summary>
/// <param name="PhotoId">Target photo identifier.</param>
/// <param name="Number">Proposed number.</param>
/// <param name="SourcePhotoId">Photo from which the proposal was propagated.</param>
/// <param name="Similarity">Measured similarity.</param>
/// <param name="Reason">Stable review reason.</param>
public sealed record BurstNumberProposal(int PhotoId, string Number, int SourcePhotoId, double Similarity, string Reason);

/// <summary>Propagates a known number to nearby similar frames as review candidates only.</summary>
public static class BurstPropagation
{
    /// <summary>Returns proposals within the EXIF time gap and similarity threshold.</summary>
    public static IReadOnlyList<BurstNumberProposal> Propose(
        ManifestPhoto source,
        string number,
        IEnumerable<BurstFrame> neighbours,
        TimeSpan maxGap,
        double minimumSimilarity)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(neighbours);
        if (maxGap < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxGap));
        if (!double.IsFinite(minimumSimilarity) || minimumSimilarity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(minimumSimilarity));

        var normalizedNumber = NumberNormalizer.Normalize(number);
        if (source.CaptureTime == default)
            return [];

        return neighbours
            .Where(frame => frame.Photo.Id != source.Id)
            .Where(frame => frame.Photo.CaptureTime != default)
            .Where(frame => (frame.Photo.CaptureTime.DateTime - source.CaptureTime.DateTime).Duration() <= maxGap)
            .Where(frame => double.IsFinite(frame.Similarity) && frame.Similarity <= 1 && frame.Similarity >= minimumSimilarity)
            .OrderBy(frame => (frame.Photo.CaptureTime.DateTime - source.CaptureTime.DateTime).Duration())
            .ThenByDescending(frame => frame.Similarity)
            .Select(frame => new BurstNumberProposal(
                frame.Photo.Id,
                normalizedNumber,
                source.Id,
                frame.Similarity,
                "burst_propagation_review"))
            .DistinctBy(proposal => proposal.PhotoId).ToArray();
    }
}

/// <summary>Measures frame appearance in Vision; missing/unusable previews return null.</summary>
public interface IFrameSimilarity
{
    /// <summary>Returns a finite score from zero to one, or null when a comparison is unavailable.</summary>
    double? Compare(ManifestPhoto first, ManifestPhoto second);
}

/// <summary>Opt-in burst proposal limits, independent of OCR matching thresholds.</summary>
public sealed record BurstOptions(double MaxGapSeconds = 2, double MinimumSimilarity = 0.95)
{
    /// <summary>Rejects invalid time gaps and similarity limits.</summary>
    public void Validate()
    {
        if (!double.IsFinite(MaxGapSeconds) || MaxGapSeconds < 0 || MaxGapSeconds >= TimeSpan.MaxValue.TotalSeconds ||
            !double.IsFinite(MinimumSimilarity) || MinimumSimilarity is < 0 or > 1)
            throw new InvalidDataException("Invalid burst gap or similarity threshold.");
    }
}

/// <summary>Adds review-only candidates after independent recognition, without propagating proposals again.</summary>
public sealed class BurstReviewProcessor
{
    private readonly EntryList entryList;
    private readonly IFrameSimilarity similarity;
    private readonly BurstOptions options;

    /// <summary>Creates a batch postprocessor with an explicit similarity provider.</summary>
    public BurstReviewProcessor(EntryList entryList, IFrameSimilarity similarity, BurstOptions? options = null)
    {
        this.entryList = entryList ?? throw new ArgumentNullException(nameof(entryList));
        this.similarity = similarity ?? throw new ArgumentNullException(nameof(similarity));
        this.options = options ?? new BurstOptions();
        this.options.Validate();
    }

    /// <summary>Preserves resolved/manual/noCar/error results; only unresolved review frames receive candidates.</summary>
    public IReadOnlyList<PhotoResult> Apply(Manifest manifest, IReadOnlyList<PhotoResult> results)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(results);
        if (manifest.Photos.Select(photo => photo.Id).Distinct().Count() != manifest.Photos.Count ||
            results.Select(result => result.Id).Distinct().Count() != results.Count ||
            !manifest.Photos.Select(photo => photo.Id).Order().SequenceEqual(results.Select(result => result.Id).Order()))
            throw new InvalidDataException("Burst processing requires unique matching manifest/result IDs.");
        var photos = manifest.Photos.ToDictionary(photo => photo.Id);
        // Snapshot sources: review proposals are never sources, even later in the same batch.
        var sources = results.Where(result => (result.Status is "auto" or "manual") && result.Cars is { Count: 1 } &&
            result.Cars[0].Primary && result.Cars[0].Source != "burst" &&
            entryList.TryGetEntry(result.Cars[0].Number, out _) && photos[result.Id].CaptureTime != default).ToArray();
        return results.Select(target => ApplyToTarget(photos[target.Id], target, sources, photos)).ToArray();
    }

    private PhotoResult ApplyToTarget(ManifestPhoto photo, PhotoResult target, IReadOnlyList<PhotoResult> sources,
        IReadOnlyDictionary<int, ManifestPhoto> photos)
    {
        if (target.Status != "review" || !string.IsNullOrWhiteSpace(photo.ManualNumber) || photo.CaptureTime == default)
            return target;
        var proposals = new List<BurstNumberProposal>();
        var reasons = target.Reasons.ToList();
        foreach (var source in sources)
        {
            var sourcePhoto = photos[source.Id];
            if (source.Id == target.Id || source.Session != target.Session ||
                !SameDirectory(sourcePhoto.Path, photo.Path) ||
                (sourcePhoto.CaptureTime.DateTime - photo.CaptureTime.DateTime).Duration().TotalSeconds > options.MaxGapSeconds)
                continue;
            try
            {
                if (similarity.Compare(sourcePhoto, photo) is { } score)
                    proposals.AddRange(BurstPropagation.Propose(sourcePhoto, source.Cars![0].Number,
                        [new BurstFrame(photo, score)], TimeSpan.FromSeconds(options.MaxGapSeconds), options.MinimumSimilarity));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Optional comparison failure must not invalidate the independent recognition result.
                reasons.Add($"burst_similarity_error:{source.Id}:{exception.GetType().Name}");
            }
        }
        if (proposals.Count == 0)
            return reasons.Count == target.Reasons.Count ? target : target with { Reasons = reasons.Distinct().ToArray() };
        var bestPerNumber = proposals.GroupBy(proposal => proposal.Number, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(proposal => proposal.Similarity)
                .ThenBy(proposal => (photos[proposal.SourcePhotoId].CaptureTime.DateTime - photo.CaptureTime.DateTime).Duration())
                .ThenBy(proposal => proposal.SourcePhotoId).First())
            .OrderByDescending(proposal => proposal.Similarity).ThenBy(proposal => proposal.Number, StringComparer.Ordinal).ToArray();
        reasons.Add("burst_propagation_review");
        if (bestPerNumber.Length > 1)
            reasons.Add("burst_ambiguous");
        reasons.AddRange(bestPerNumber.Select(proposal => $"burst_source:{proposal.SourcePhotoId}:{proposal.Number}"));
        var existing = (target.Cars ?? []).Where(car => car.Source != "burst").ToArray();
        var candidates = bestPerNumber.Where(proposal => !existing.Any(car => car.Number == proposal.Number))
            .Select(proposal => new CarCandidate(proposal.Number, proposal.Similarity, "burst", false));
        return target with
        {
            Status = "review", Fields = null, Reasons = reasons.Distinct().ToArray(),
            Cars = existing.Concat(candidates).ToArray()
        };
    }

    private static bool SameDirectory(string first, string second)
    {
        try
        {
            return string.Equals(Path.GetDirectoryName(Path.GetFullPath(first)), Path.GetDirectoryName(Path.GetFullPath(second)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
