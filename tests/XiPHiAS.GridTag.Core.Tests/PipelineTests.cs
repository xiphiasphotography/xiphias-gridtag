using System.Text;
using XiPHiAS.GridTag.Core;
using XiPHiAS.GridTag.Vision;
using Xunit;

namespace XiPHiAS.GridTag.Core.Tests;

public sealed class PipelineTests
{
    [Theory]
    [InlineData(0.8, null, 0, "auto", 1)]
    [InlineData(0.8, null, 10, "review", 1)]
    [InlineData(0.99, null, 10, "auto", 0)]
    [InlineData(0.8, "69", 10, "manual", 0)]
    public void TimingOnlyChecksUncertainListedNumbers(double probability, string? manual,
        int passingGap, string status, int expectedCalls)
    {
        var capture = new DateTimeOffset(2026, 10, 2, 14, 0, 0, TimeSpan.FromHours(2));
        var calls = 0;
        var pipeline = new TaggingPipeline(CreateEntryList(), CreateContext(),
            new FakePreviewProvider(new FakePreview(100, 60)),
            new FakeCarDetector(new DetectedCar("large", 0.95)),
            new FakePlateReader(new NumberHypothesis("69", probability)),
            evidenceProvider: photo =>
            {
                calls++;
                return new TimingCrossCheckEvidence(photo.CaptureTime.DateTime,
                    [new("69", capture.DateTime.AddSeconds(5 + passingGap))], TimeSpan.FromSeconds(5));
            });
        var result = pipeline.ProcessPhoto(new ManifestPhoto(1, "u1", "photo.jpg", capture, manual));
        Assert.Equal(status, result.Status);
        Assert.Equal(expectedCalls, calls);
        if (status == "review")
        {
            Assert.Contains("evidence_conflict:timing", result.Reasons);
            Assert.Null(result.Fields);
        }
    }

    [Theory]
    [InlineData(null, "auto")]
    [InlineData("#007", "manual")]
    public void LeadingZeros_ArePreservedInCarIdentityAndGeneratedFields(string? manualNumber, string status)
    {
        var entries = new EntryList([
            new Entry("007", "Bond", "Aston Martin", "PRO", []),
            new Entry("7", "Other", "Car", "PRO", [])]);
        var pipeline = new TaggingPipeline(entries, CreateContext(),
            new FakePreviewProvider(new FakePreview(100, 60)),
            new FakeCarDetector(new DetectedCar("car", 0.99)),
            new FakePlateReader(new NumberHypothesis("007", 0.99)));
        var result = pipeline.ProcessPhoto(new ManifestPhoto(1, "bond", "photo.jpg", DateTimeOffset.UtcNow, manualNumber));
        Assert.Equal(status, result.Status);
        Assert.NotNull(result.Cars);
        Assert.Equal("007", Assert.Single(result.Cars).Number);
        Assert.NotNull(result.Fields);
        Assert.Equal("#007 Bond Aston Martin", result.Fields.Headline);
        Assert.Contains("#007", result.Fields.Keywords);
        Assert.DoesNotContain("#7", result.Fields.Keywords);
    }
    [Theory]
    [InlineData("69", 0.99, null, "auto", 0)]
    [InlineData("69", 0.80, null, "auto", 1)]
    [InlineData("999", 0.80, null, "review", 0)]
    [InlineData("", 0.80, null, "review", 0)]
    [InlineData("69", 0.80, "69", "manual", 0)]
    public void VisualEvidence_IsOnlyReadForUncertainListedNumbers(string number, double probability,
        string? manualNumber, string expectedStatus, int expectedCalls)
    {
        var preview = new FakePreview(100, 60);
        var detection = new DetectedCar("large", 0.95);
        var calls = 0;
        var pipeline = new TaggingPipeline(CreateEntryList(), CreateContext(),
            new FakePreviewProvider(preview), new FakeCarDetector(detection),
            new FakePlateReader(new NumberHypothesis(number, probability)),
            visualEvidenceProvider: (actualPreview, actualDetection) =>
            {
                calls++;
                Assert.Same(preview, actualPreview);
                Assert.Same(detection, actualDetection);
                return new CarModelEvidence(new CarModelObservation("Ferrari 296", 0.9));
            });

        var result = pipeline.ProcessPhoto(new ManifestPhoto(1, "u1", "photo.arw",
            DateTimeOffset.UtcNow, manualNumber));

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedCalls, calls);
    }

    [Fact]
    public void ConflictingVisualEvidence_KeepsUncertainNumberInReview()
    {
        var pipeline = new TaggingPipeline(CreateEntryList(), CreateContext(),
            new FakePreviewProvider(new FakePreview(100, 60)),
            new FakeCarDetector(new DetectedCar("large", 0.95)),
            new FakePlateReader(new NumberHypothesis("69", 0.80)),
            visualEvidenceProvider: (_, _) => new CarModelEvidence(new CarModelObservation("Audi", 0.95)));

        var result = pipeline.ProcessPhoto(new ManifestPhoto(1, "u1", "photo.arw", DateTimeOffset.UtcNow));

        Assert.Equal("review", result.Status);
        Assert.Contains("evidence_conflict:car_model", result.Reasons);
    }

    [Fact]
    public void DriverSupport_CannotHideStrongModelConflict()
    {
        var pipeline = new TaggingPipeline(CreateEntryList(), CreateContext(),
            new FakePreviewProvider(new FakePreview(100, 60)),
            new FakeCarDetector(new DetectedCar("large", 0.95)),
            new FakePlateReader(new NumberHypothesis("69", 0.80)),
            visualEvidenceProvider: (_, _) => new CompositeEvidence([
                new CarModelEvidence(new CarModelObservation("Audi", 0.80)),
                new DriverNameEvidence([new DriverNameObservation("Thierry Vermeulen", 1.0)])]));

        var result = pipeline.ProcessPhoto(new ManifestPhoto(1, "u1", "photo.arw", DateTimeOffset.UtcNow));
        Assert.Equal("review", result.Status);
        Assert.Contains("evidence_conflict:car_model", result.Reasons);
    }

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
