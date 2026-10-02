using XiPHiAS.GridTag.Core;
using XiPHiAS.GridTag.Vision;
using Xunit;

namespace XiPHiAS.GridTag.Core.Tests;

public sealed class VisionEvidenceTests
{
    [Fact]
    public void PaddleDecoder_PreservesNativeProbabilitiesAndBlankSeparatedRepeats()
    {
        var name = Assert.Single(EvidenceOutputDecoder.DecodeNameProbabilities([
            0.1f, 0.9f, 0,
            0.1f, 0.9f, 0,
            1, 0, 0,
            0.2f, 0.8f, 0,
            0, 0.1f, 0.9f], 5, ["blank", "S", "ö"]));
        Assert.Equal("SSö", name.Name);
        Assert.Equal((0.9 + 0.8 + 0.9) / 3, name.Confidence, 6);
        Assert.Empty(EvidenceOutputDecoder.DecodeNameProbabilities([float.NaN, 1], 1, ["blank", "A"]));
    }

    [Fact]
    public void PaddleNumbers_RejectSponsorFragmentsAndKeepDuplicateConfidenceUnchanged()
    {
        var numbers = PaddleNumberReader.ExtractNumbers([
            new("#69", 0.8), new("69", 0.9), new("ROWE 69", 0.99), new("Pirelli", 0.99), new("007", 0.7), new("7", 0.6)]);
        Assert.Equal(new[] { "69", "007", "7" }, numbers.Select(number => number.Text));
        Assert.Equal(0.9, numbers[0].Probability);
    }

    [Fact]
    public void TextRegions_ClampExpandedBoundsAndRejectNonfiniteMapValues()
    {
        var map = new float[64];
        for (var y = 0; y < 4; y++)
            for (var x = 0; x < 4; x++)
                map[y * 8 + x] = 0.8f;
        map[63] = float.NaN;
        var region = Assert.Single(PaddleTextRegions.Find(map, 8, 8, 80, 40));
        Assert.Equal(0, region.Left);
        Assert.Equal(0, region.Top);
        Assert.InRange(region.Right, 40, 80);
        Assert.InRange(region.Bottom, 20, 40);
    }

    [Theory]
    [InlineData("Aston Martin V8 Vantage Coupe 2012", "Aston Martin")]
    [InlineData("Mercedes-Benz C-Class Sedan 2012", "Mercedes")]
    [InlineData("Ferrari 458 Italia Coupe 2012", "Ferrari")]
    public void StanfordLabels_ProduceMakeEvidenceInsteadOfInventingGt3Models(string label, string make) =>
        Assert.Equal(make, StanfordCarLabels.Manufacturer(label));

    [Fact]
    public void ModelDecoder_UsesSoftmaxAndOrderedLabels()
    {
        var result = EvidenceOutputDecoder.DecodeModel([0, 2], ["Audi", "Ferrari"]);
        Assert.NotNull(result);
        Assert.Equal("Ferrari", result.Model);
        Assert.InRange(result.Confidence, 0.88, 0.89);
        Assert.Null(EvidenceOutputDecoder.DecodeModel([float.NaN], ["Ferrari"]));
        Assert.Throws<InvalidDataException>(() => EvidenceOutputDecoder.DecodeModel([0], ["Audi", "Ferrari"]));
    }

    [Fact]
    public void NameDecoder_CollapsesRepeatsButPreservesBlankSeparatedCharactersAndUnicode()
    {
        // S S blank S ö -> SSö (class zero is blank).
        var result = EvidenceOutputDecoder.DecodeNames([
            -10, 10, -10,
            -10, 10, -10,
            10, -10, -10,
            -10, 10, -10,
            -10, -10, 10], 5, ["_", "S", "ö"]);
        var name = Assert.Single(result);
        Assert.Equal("SSö", name.Name);
        Assert.InRange(name.Confidence, 0.99, 1.0);
        Assert.Empty(EvidenceOutputDecoder.DecodeNames([10, -10], 1, ["_", "S"]));
        Assert.Empty(EvidenceOutputDecoder.DecodeNames([0, float.NaN], 1, ["_", "S"]));
        Assert.Throws<InvalidDataException>(() => EvidenceOutputDecoder.DecodeNames([0], 1, ["_", "S"]));
    }

    [Fact]
    public void Factory_CombinesObservationsForTheDetectedCar()
    {
        var reader = new Readers();
        var factory = new VisionEvidenceFactory(reader, reader);
        var preview = new Preview(100, 60);
        var car = new DetectedCar("car", 0.9);
        var evidence = factory.Create(preview, car);
        var entries = new EntryList([
            new Entry("69", "Team", "Ferrari 296", "PRO", [new Driver("Thierry Vermeulen", "NED")])]);

        Assert.NotNull(evidence);
        Assert.Equal("car_model+driver_name", evidence.Name);
        Assert.True(evidence.GetWeight("69", entries) > 1);
        Assert.Equal(2, reader.Inputs.Count);
        Assert.Equal(1, factory.InvocationCount);
        Assert.Equal(1, factory.ModelObservationCount);
        Assert.Equal(1, factory.NameObservationCount);
        Assert.All(reader.Inputs, input =>
        {
            Assert.Same(preview, input.Preview);
            Assert.Same(car, input.Car);
        });
    }

    [Fact]
    public void Factory_WithoutReadersReturnsNoEvidence() =>
        Assert.Null(new VisionEvidenceFactory().Create(new Preview(100, 60), new DetectedCar("car", 0.9)));

    private sealed record Preview(int Width, int Height) : IPreview;

    private sealed class Readers : ICarModelClassifier, IDriverNameReader
    {
        public List<(IPreview Preview, DetectedCar Car)> Inputs { get; } = [];

        public CarModelObservation? Classify(IPreview preview, DetectedCar detectedCar)
        {
            Inputs.Add((preview, detectedCar));
            return new CarModelObservation("Ferrari", 0.9);
        }

        public IReadOnlyList<DriverNameObservation> ReadNames(IPreview preview, DetectedCar detectedCar)
        {
            Inputs.Add((preview, detectedCar));
            return [new DriverNameObservation("Thierry Vermeulen", 0.9)];
        }
    }
}
