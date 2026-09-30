using System.Text;
using GridTag.Core;
using GridTag.Vision;
using Xunit;

namespace GridTag.Core.Tests;

public sealed class PipelineTests
{
    [Fact]
    public void ManualSingleNumber_IsStoredAsManual()
    {
        var pipeline = CreatePipeline();
        var result = pipeline.ProcessPhoto(new ManifestPhoto(1, "u1", "D:/photo.arw", new DateTimeOffset(2026, 9, 18, 13, 52, 0, TimeSpan.Zero), "69"));
        var cars = result.Cars ?? throw new InvalidOperationException("Expected manual photos to produce car candidates.");

        Assert.Equal("manual", result.Status);
        Assert.Single(cars);
        Assert.Equal("69", cars[0].Number);
        Assert.Equal("manual", cars[0].Source);
        Assert.NotNull(result.Fields);
    }

    [Fact]
    public void ManualMultipleNumbers_KeepPrimaryAndReasons()
    {
        var pipeline = CreatePipeline();
        var result = pipeline.ProcessPhoto(new ManifestPhoto(2, "u2", "D:/photo.arw", new DateTimeOffset(2026, 9, 18, 13, 52, 0, TimeSpan.Zero), "69, 3"));
        var cars = result.Cars ?? throw new InvalidOperationException("Expected manual photos to produce car candidates.");

        Assert.Equal("manual", result.Status);
        Assert.Equal(2, cars.Count);
        Assert.Equal("69", cars[0].Number);
        Assert.Equal("3", cars[1].Number);
    }

    [Fact]
    public void ManualUnknownNumber_IsReview()
    {
        var pipeline = CreatePipeline();
        var result = pipeline.ProcessPhoto(new ManifestPhoto(3, "u3", "D:/photo.arw", new DateTimeOffset(2026, 9, 18, 13, 52, 0, TimeSpan.Zero), "999"));

        Assert.Equal("review", result.Status);
        Assert.Contains("unknown_number:999", result.Reasons);
    }

    [Fact]
    public void TwoCars_UseLargestDetectionAsPrimary()
    {
        var pipeline = new TaggingPipeline(
            CreateEntryList(),
            CreateContext(),
            new FakePreviewProvider(new FakePreview(100, 60)),
            new FakeCarDetector(
                new DetectedCar("large", 0.95),
                new DetectedCar("small", 0.70)),
            new FakePlateReader(
                new NumberHypothesis("69", 0.98),
                new NumberHypothesis("3", 0.93)),
            fieldBuilder: new FieldBuilder());

        var result = pipeline.ProcessPhoto(new ManifestPhoto(4, "u4", "D:/photo.arw", new DateTimeOffset(2026, 9, 18, 13, 52, 0, TimeSpan.Zero)));
        var cars = result.Cars ?? throw new InvalidOperationException("Expected an auto result to include a car list.");

        Assert.Equal("auto", result.Status);
        Assert.Equal(2, cars.Count);
        Assert.Equal("69", cars[0].Number);
    }

    [Fact]
    public void LargestCarUnresolved_IsReview()
    {
        var pipeline = new TaggingPipeline(
            CreateEntryList(),
            CreateContext(),
            new FakePreviewProvider(new FakePreview(100, 60)),
            new FakeCarDetector(
                new DetectedCar("large", 0.95),
                new DetectedCar("small", 0.80)),
            new FakePlateReader(
                new NumberHypothesis("abc", 0.95),
                new NumberHypothesis("3", 0.90)),
            fieldBuilder: new FieldBuilder());

        var result = pipeline.ProcessPhoto(new ManifestPhoto(5, "u5", "D:/photo.arw", new DateTimeOffset(2026, 9, 18, 13, 52, 0, TimeSpan.Zero)));

        Assert.Equal("review", result.Status);
        Assert.Contains(result.Reasons, reason => reason == "no_reading" || reason == "largest_car_unresolved");
    }

    [Fact]
    public void NoCar_WhenDetectorReturnsEmptyList()
    {
        var pipeline = new TaggingPipeline(
            CreateEntryList(),
            CreateContext(),
            new FakePreviewProvider(new FakePreview(100, 60)),
            new FakeCarDetector(),
            new FakePlateReader(),
            fieldBuilder: new FieldBuilder());

        var result = pipeline.ProcessPhoto(new ManifestPhoto(6, "u6", "D:/photo.arw", new DateTimeOffset(2026, 9, 18, 13, 52, 0, TimeSpan.Zero)));

        Assert.Equal("noCar", result.Status);
        Assert.Contains("no_car_detected", result.Reasons);
    }

    [Fact]
    public void NoPreview_IsReviewWithNoPreviewReason()
    {
        var pipeline = new TaggingPipeline(
            CreateEntryList(),
            CreateContext(),
            new StubRawPreviewProvider(),
            new NullCarDetector(),
            new NullPlateReader(),
            fieldBuilder: new FieldBuilder());

        var result = pipeline.ProcessPhoto(new ManifestPhoto(7, "u7", "D:/photo.arw", new DateTimeOffset(2026, 9, 18, 13, 52, 0, TimeSpan.Zero)));

        Assert.Equal("error", result.Status);
        Assert.Contains("no_preview", result.Reasons);
    }

    [Fact]
    public void ExceptionInProcessing_IsError()
    {
        var pipeline = new TaggingPipeline(
            CreateEntryList(),
            CreateContext(),
            new FakePreviewProvider(new FakePreview(100, 60)),
            new FakeCarDetector(new DetectedCar("fail", 0.90)),
            new ThrowingPlateReader(),
            fieldBuilder: new FieldBuilder());

        var result = pipeline.ProcessPhoto(new ManifestPhoto(8, "u8", "D:/photo.arw", new DateTimeOffset(2026, 9, 18, 13, 52, 0, TimeSpan.Zero)));

        Assert.Equal("error", result.Status);
        Assert.Contains("exception:", result.Reasons[0]);
    }

    [Fact]
    public void SampleFiles_DeserializeAndResultJson_WritesWithoutBom()
    {
        var manifest = GridTagJson.ReadManifest("samples/manifest.example.json");
        var resultFile = new ResultFile(1, "0.1.0", DateTimeOffset.Parse("2026-09-18T14:10:00Z"),
            [
                new PhotoResult(1001, "auto", [], "FP2", [new CarCandidate("69", 0.98, "ocr", true)],
                    new GeneratedFields("#69", "Caption", "Alt", "Ext", ["A"], ["B"]))
            ]);

        var tempPath = Path.Combine(Path.GetTempPath(), $"gridtag-result-{Guid.NewGuid():N}.json");
        GridTagJson.WriteResultFile(tempPath, resultFile);

        var bytes = File.ReadAllBytes(tempPath);
        Assert.NotEmpty(bytes);
        Assert.False(bytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));

        var roundTrip = GridTagJson.ReadResultFile(tempPath);
        Assert.Equal(1, roundTrip.SchemaVersion);
        Assert.Equal("auto", roundTrip.Photos[0].Status);

        File.Delete(tempPath);
        Assert.NotEmpty(manifest.Photos);
    }

    private static TaggingPipeline CreatePipeline()
    {
        return new TaggingPipeline(
            CreateEntryList(),
            CreateContext(),
            new FakePreviewProvider(new FakePreview(100, 60)),
            new FakeCarDetector(new DetectedCar("car-1", 0.90)),
            new FakePlateReader(new NumberHypothesis("69", 0.98)),
            fieldBuilder: new FieldBuilder());
    }

    private static EntryList CreateEntryList()
    {
        return new EntryList(
        [
            new Entry("3", "Mercedes - AMG Team Verstappen Racing", "Mercedes-AMG GT3 EVO", "PRO", []),
            new Entry("69", "Emil Frey Racing", "Ferrari 296 GT3 EVO", "PRO", [new Driver("Thierry Vermeulen", "NED"), new Driver("Ben Green", "GBR")]),
            new Entry("96", "Audi Sport Team", "Audi R8 LMS GT3 EVO", "PRO", []),
            new Entry("99", "Team 99", "Car 99", "PRO", []),
        ]);
    }

    private static EventContext CreateContext()
    {
        return EventContext.Default(
            "GT World Challenge Europe",
            "GT World Challenge Europe powered by AWS",
            "Circuit Zandvoort",
            "FP2",
            [
                new EventSession("FP2", "Free Practice 2", new DateTimeOffset(2026, 9, 18, 13, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 18, 15, 0, 0, TimeSpan.Zero))
            ]);
    }

    private sealed class FakePreviewProvider : IRawPreviewProvider
    {
        private readonly IPreview? preview;

        public FakePreviewProvider(IPreview? preview) => this.preview = preview;

        public IPreview? GetPreview(string path) => preview;
    }

    private sealed class FakeCarDetector : ICarDetector
    {
        private readonly IReadOnlyList<DetectedCar> detections;

        public FakeCarDetector(params DetectedCar[] detections) => this.detections = detections;

        public IReadOnlyList<DetectedCar> Detect(IPreview preview) => detections;
    }

    private sealed class FakePlateReader : IPlateReader
    {
        private readonly IReadOnlyList<NumberHypothesis> hypotheses;

        public FakePlateReader(params NumberHypothesis[] hypotheses) => this.hypotheses = hypotheses;

        public IReadOnlyList<NumberHypothesis> ReadNumbers(IPreview preview, DetectedCar detectedCar)
        {
            if (hypotheses.Count == 0)
                return Array.Empty<NumberHypothesis>();

            if (hypotheses.Count == 1)
                return [hypotheses[0]];

            return string.Equals(detectedCar.Id, "small", StringComparison.OrdinalIgnoreCase)
                ? [hypotheses[^1]]
                : [hypotheses[0]];
        }
    }

    private sealed class ThrowingPlateReader : IPlateReader
    {
        public IReadOnlyList<NumberHypothesis> ReadNumbers(IPreview preview, DetectedCar detectedCar)
            => throw new InvalidOperationException("plate reader exploded");
    }

    private sealed record FakePreview(int Width, int Height) : IPreview;
}
