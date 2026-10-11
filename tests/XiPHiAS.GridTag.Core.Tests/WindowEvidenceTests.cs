using XiPHiAS.GridTag.Core;
using XiPHiAS.GridTag.Vision;
using Xunit;

namespace XiPHiAS.GridTag.Core.Tests;

public sealed class WindowEvidenceTests
{
    [Fact]
    public void NameReader_PassesOnlyWindowBoundsAndDoesNotChangeOriginalDetection()
    {
        var reader = new Reader();
        var preview = new Preview(1000, 600);
        var car = new DetectedCar("car", 0.9, new DetectionBounds(100, 100, 900, 500));
        var names = new WindowCropDriverNameReader(reader).ReadNames(preview, car);
        Assert.Equal("VERMEULEN", Assert.Single(names).Name);
        Assert.Same(preview, reader.Preview);
        Assert.NotNull(reader.Car);
        Assert.Equal(car.Id, reader.Car.Id);
        Assert.Equal(car.Confidence, reader.Car.Confidence);
        Assert.Equal(new DetectionBounds(180, 140, 820, 320), reader.Car.Bounds);
        Assert.Equal(new DetectionBounds(100, 100, 900, 500), car.Bounds);
    }

    [Fact]
    public void WindowBounds_ClampCarToImageBeforeCalculatingRegion()
    {
        var result = WindowCropDriverNameReader.GetWindowBounds(new Preview(100, 100),
            new DetectedCar("car", 0.9, new DetectionBounds(-100, -50, 200, 200)));
        Assert.NotNull(result);
        Assert.Equal(10, result.Left);
        Assert.Equal(10, result.Top);
        Assert.Equal(90, result.Right);
        Assert.Equal(55, result.Bottom);
    }

    [Theory]
    [InlineData(100, 0, 50, 80)]
    [InlineData(0, 60, 80, 50)]
    [InlineData(110, 0, 120, 80)]
    [InlineData(float.NaN, 0, 80, 80)]
    [InlineData(0, 0, float.PositiveInfinity, 80)]
    public void InvalidWindowBounds_DoNotCallOcr(float left, float top, float right, float bottom)
    {
        var reader = new Reader();
        var names = new WindowCropDriverNameReader(reader).ReadNames(new Preview(100, 100),
            new DetectedCar("car", 0.9, new DetectionBounds(left, top, right, bottom)));
        Assert.Empty(names);
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public void MissingBounds_DoNotFallBackToWholeImageOcr()
    {
        var reader = new Reader();
        Assert.Empty(new WindowCropDriverNameReader(reader).ReadNames(new Preview(100, 100), new DetectedCar("car", 1)));
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public void EntryListVocabulary_KeepsExporterOrderAndRequiresEveryModel()
    {
        string[] models = ["Porsche 911 GT3 R", "Mercedes-AMG GT3", "McLaren 720S GT3", "Aston Martin Vantage",
            "Ferrari 296 GT3", "BMW M4 GT3", "Audi R8 LMS", "Lamborghini Temerario"];
        var ordered = models.Reverse().ToArray();
        EntryListCarModelLabels.Validate(ordered, models);
        var logits = new float[8];
        logits[2] = 8;
        Assert.Equal(ordered[2], EvidenceOutputDecoder.DecodeModel(logits, ordered)!.Model);
        Assert.Throws<InvalidDataException>(() => EntryListCarModelLabels.Validate(ordered[..7], models));
        Assert.Throws<InvalidDataException>(() => EntryListCarModelLabels.Validate([.. ordered, "Other car"], models));
        Assert.Throws<InvalidDataException>(() => EntryListCarModelLabels.Validate([.. ordered[..7], ordered[0]], models));
        Assert.Throws<InvalidDataException>(() => EntryListCarModelLabels.Validate(["Porsche", "Mercedes"], models));
    }

    private sealed record Preview(int Width, int Height) : IPreview;

    private sealed class Reader : IDriverNameReader
    {
        public int Calls { get; private set; }
        public IPreview? Preview { get; private set; }
        public DetectedCar? Car { get; private set; }

        public IReadOnlyList<DriverNameObservation> ReadNames(IPreview preview, DetectedCar detectedCar)
        {
            Calls++;
            Preview = preview;
            Car = detectedCar;
            return [new DriverNameObservation("VERMEULEN", 0.9)];
        }
    }
}
