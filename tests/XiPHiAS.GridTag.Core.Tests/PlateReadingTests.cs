using GridTag.Core;
using GridTag.Vision;
using Xunit;

namespace GridTag.Core.Tests;

public sealed class PlateReadingTests
{
    [Fact]
    public void CropGeometry_ClampsUpperMiddleRegionToImage()
    {
        var crop = PlateCropGeometry.Calculate(new DetectionBounds(-10, 20, 110, 100), 100, 80);

        Assert.NotNull(crop);
        Assert.Equal(2, crop.Left);
        Assert.Equal(32, crop.Top);
        Assert.Equal(96, crop.Width);
        Assert.Equal(40, crop.Height);
    }

    [Fact]
    public void CropGeometry_ReturnsNullForInvalidBox()
    {
        Assert.Null(PlateCropGeometry.Calculate(new DetectionBounds(20, 20, 20, 30), 100, 100));
    }

    [Fact]
    public void Decoder_ReturnsNBestWithoutEntryListFiltering()
    {
        var positions = new IReadOnlyList<DigitCandidate>[]
        {
            [new DigitCandidate('9', 0.8), new DigitCandidate('5', 0.2)],
            [new DigitCandidate('9', 0.7), new DigitCandidate('6', 0.3)],
        };

        var result = DigitNBestDecoder.Decode(positions, beamWidth: 4, nBest: 4);

        Assert.Equal(4, result.Count);
        Assert.Equal("99", result[0].Text);
        Assert.Equal(0.56, result[0].Probability, 6);
        Assert.Contains(result, hypothesis => hypothesis.Text == "56");
    }

    [Fact]
    public void Decoder_RemovesBlankSymbolsButKeepsRawDigits()
    {
        var positions = new IReadOnlyList<DigitCandidate>[]
        {
            [new DigitCandidate('_', 0.8), new DigitCandidate('0', 0.2)],
            [new DigitCandidate('3', 0.9)],
        };

        var result = DigitNBestDecoder.Decode(positions, beamWidth: 4, nBest: 4);

        Assert.Contains(result, hypothesis => hypothesis.Text == "3");
        Assert.Contains(result, hypothesis => hypothesis.Text == "03");
    }
}
