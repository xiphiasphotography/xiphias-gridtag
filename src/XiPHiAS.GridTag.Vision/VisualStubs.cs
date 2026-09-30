using GridTag.Core;

namespace GridTag.Vision;

/// <summary>Null preview provider used when no preview is available.</summary>
public sealed class StubRawPreviewProvider : IRawPreviewProvider
{
    /// <summary>Returns null for all photos, which maps to the noCar/no_preview outcome.</summary>
    public IPreview? GetPreview(string path) => null;
}

/// <summary>Null car detector returning no detections.</summary>
public sealed class NullCarDetector : ICarDetector
{
    /// <summary>Always returns an empty detection set.</summary>
    public IReadOnlyList<DetectedCar> Detect(IPreview preview) => Array.Empty<DetectedCar>();
}

/// <summary>Null plate reader returning no OCR hypotheses.</summary>
public sealed class NullPlateReader : IPlateReader
{
    /// <summary>Always returns an empty hypothesis set.</summary>
    public IReadOnlyList<NumberHypothesis> ReadNumbers(IPreview preview, DetectedCar detectedCar) => Array.Empty<NumberHypothesis>();
}
