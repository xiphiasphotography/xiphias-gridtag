using GridTag.Vision;
using Xunit;

namespace GridTag.Core.Tests;

public sealed class CarDetectionTests
{
    [Fact]
    public void Letterbox_PreservesAspectRatioAndUsesChwNormalization()
    {
        var pixels = new RgbPixel[2 * 1]
        {
            new(255, 0, 0),
            new(0, 255, 0),
        };

        var result = LetterboxPreprocessor.Apply(pixels, 2, 1, 4);

        Assert.Equal(2f, result.Scale);
        Assert.Equal(0f, result.PadX);
        Assert.Equal(1f, result.PadY);
        Assert.Equal(48, result.Tensor.Length);
        Assert.Equal(1f, result.Tensor[4]);
        Assert.Equal(0f, result.Tensor[20]);
        Assert.Equal(1f, result.Tensor[22]);
    }

    [Fact]
    public void Postprocessor_FiltersConfidenceAndSuppressesOverlappingBoxes()
    {
        var letterbox = new LetterboxResult(new float[3 * 100 * 100], 1f, 0f, 0f, 100);
        var output = new float[]
        {
            50, 50, 40, 40, 0.9f, 0.9f,
            52, 52, 40, 40, 0.8f, 0.9f,
            10, 10, 10, 10, 0.9f, 0.9f,
            80, 80, 10, 10, 0.1f, 0.9f,
        };

        var detections = YoloPostprocessor.Process(output, 4, 6, 0, 0.25f, 0.5f, letterbox, 100, 100);

        Assert.Equal(2, detections.Count);
        Assert.Equal(0.81f, detections[0].Score, 2);
        Assert.Equal(30f, detections[0].Bounds.Left);
        Assert.Equal(5f, detections[1].Bounds.Left);
    }
}
