namespace GridTag.Core;

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
        if (maxGap < TimeSpan.Zero || minimumSimilarity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(maxGap));

        return neighbours
            .Where(frame => frame.Photo.Id != source.Id)
            .Where(frame => (frame.Photo.CaptureTime - source.CaptureTime).Duration() <= maxGap)
            .Where(frame => frame.Similarity >= minimumSimilarity)
            .OrderBy(frame => (frame.Photo.CaptureTime - source.CaptureTime).Duration())
            .ThenByDescending(frame => frame.Similarity)
            .Select(frame => new BurstNumberProposal(
                frame.Photo.Id,
                NumberNormalizer.Normalize(number),
                source.Id,
                frame.Similarity,
                "burst_propagation_review"))
            .ToArray();
    }
}
