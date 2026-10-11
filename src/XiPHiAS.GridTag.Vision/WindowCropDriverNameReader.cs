using XiPHiAS.GridTag.Core;

namespace XiPHiAS.GridTag.Vision;

/// <summary>Limits name OCR to the upper central region of a detected car.</summary>
/// <remarks>This is a geometric window-region estimate, not a learned window detector.</remarks>
public sealed class WindowCropDriverNameReader : IDriverNameReader
{
    private readonly IDriverNameReader reader;

    /// <summary>Wraps a text reader; the underlying reader receives only the window-region bounds.</summary>
    public WindowCropDriverNameReader(IDriverNameReader reader)
    {
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    /// <inheritdoc />
    public IReadOnlyList<DriverNameObservation> ReadNames(IPreview preview, DetectedCar detectedCar)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(detectedCar);
        var bounds = GetWindowBounds(preview, detectedCar);
        return bounds is null ? [] : reader.ReadNames(preview, detectedCar with { Bounds = bounds });
    }

    /// <summary>Clamps the car box, then estimates the windscreen/side-window area within it.</summary>
    public static DetectionBounds? GetWindowBounds(IPreview preview, DetectedCar car)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(car);
        if (preview.Width <= 0 || preview.Height <= 0 || car.Bounds is not { } bounds ||
            !float.IsFinite(bounds.Left) || !float.IsFinite(bounds.Top) ||
            !float.IsFinite(bounds.Right) || !float.IsFinite(bounds.Bottom))
            return null;
        var left = Math.Clamp(bounds.Left, 0, preview.Width);
        var top = Math.Clamp(bounds.Top, 0, preview.Height);
        var right = Math.Clamp(bounds.Right, 0, preview.Width);
        var bottom = Math.Clamp(bounds.Bottom, 0, preview.Height);
        if (right <= left || bottom <= top)
            return null;
        var width = right - left;
        var height = bottom - top;
        // One broad upper region covers front and side views without reading lower-body sponsor text.
        return new DetectionBounds(left + width * 0.10f, top + height * 0.10f,
            left + width * 0.90f, top + height * 0.55f);
    }
}

/// <summary>Checks that a classifier vocabulary covers exactly the event's distinct car models.</summary>
public static class EntryListCarModelLabels
{
    /// <summary>Validates labels without reordering them: ONNX class order belongs to the model export.</summary>
    public static void Validate(IReadOnlyList<string> labels, IReadOnlyCollection<string> expectedModels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(expectedModels);
        var expected = expectedModels.Select(model => model.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actual = labels.Select(label => label.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (expected.Count == 0 || expected.Contains("") || actual.Contains("") ||
            labels.Count != actual.Count || !expected.SetEquals(actual))
            throw new InvalidDataException("Car-model labels must cover every distinct entry-list model exactly once, in the model export's class order.");
    }
}
