using XiPHiAS.GridTag.Vision;
using Xunit;

namespace XiPHiAS.GridTag.Core.Tests;

public sealed class YoloXTests
{
    [Fact]
    public void Preprocessing_UsesUnnormalizedBgrAndTopLeftPlacement()
    {
        var result = YoloXPreprocessor.Apply([new(10, 20, 30), new(40, 50, 60)], 2, 1, 2);
        Assert.Equal(0, result.PadY);
        Assert.Equal(new float[] { 30, 60, 114, 114, 20, 50, 114, 114, 10, 40, 114, 114 }, result.Tensor);
    }

    [Fact]
    public void Preprocessing_UsesLinearInterpolation()
    {
        var result = YoloXPreprocessor.Apply([new(0, 0, 0), new(100, 100, 100)], 2, 1, 4);
        Assert.Equal(new float[] { 0, 25, 75, 100 }, result.Tensor[..4]);
    }

    [Fact]
    public void Decode_AppliesGridAndStrideAtEveryPyramidBoundary()
    {
        var output = new float[8400 * 85];
        foreach (var row in new[] { 79, 80, 6400, 8000 })
        {
            output[row * 85] = 0.5f;
            output[row * 85 + 1] = 0.25f;
        }
        YoloXPostprocessor.Decode(output, 8400, 85, 640);
        Assert.Equal(636, output[79 * 85]);
        Assert.Equal(10, output[80 * 85 + 1]);
        Assert.Equal(8, output[6400 * 85]);
        Assert.Equal(16, output[6400 * 85 + 2]);
        Assert.Equal(16, output[8000 * 85]);
        Assert.Equal(32, output[8000 * 85 + 2]);
    }

    [Fact]
    public void Decode_RejectsWrongOutputLayout()
    {
        Assert.Throws<InvalidDataException>(() => YoloXPostprocessor.Decode(new float[85], 1, 85, 640));
    }
}
