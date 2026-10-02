using XiPHiAS.GridTag.Vision;
using Xunit;

namespace XiPHiAS.GridTag.Core.Tests;

public sealed class BurstReviewTests
{
    [Fact]
    public void PipelineBatch_UsesBurstPostprocessingButSinglePhotoDoesNot()
    {
        var comparison = new Similarity();
        var vision = new FakeVision();
        var pipeline = new TaggingPipeline(Entries(), EventContext.Default("Series", "Event", "Track", "FP2", []),
            vision, vision, vision, burstProcessor: new BurstReviewProcessor(Entries(), comparison));
        var target = Photo(2, 1);
        Assert.Empty(pipeline.ProcessPhoto(target).Cars!);
        var results = pipeline.Process(new Manifest(1, [target, Photo(1, 0)])).Photos;
        Assert.Equal("review", results[0].Status);
        Assert.Equal("burst", Assert.Single(results[0].Cars!).Source);
        Assert.Equal("auto", results[1].Status);
        Assert.Null(results[0].Fields);
    }

    private sealed record Preview(int Width, int Height) : IPreview;

    private sealed class FakeVision : IRawPreviewProvider, ICarDetector, IPlateReader
    {
        public IPreview GetPreview(string path) => new Preview(Path.GetFileName(path) == "1.jpg" ? 100 : 80, 60);
        public IReadOnlyList<DetectedCar> Detect(IPreview preview) => [new("car", 0.99)];
        public IReadOnlyList<NumberHypothesis> ReadNumbers(IPreview preview, DetectedCar car) =>
            preview.Width == 100 ? [new("007", 0.99)] : [];
    }

    private static EntryList Entries() => new([
        new Entry("007", "Bond", "Aston Martin", "PRO", []),
        new Entry("7", "Other", "Car", "PRO", [])]);

    private static ManifestPhoto Photo(int id, double seconds, string? manual = null, string folder = "burst") =>
        new(id, $"uuid-{id}", Path.Combine(folder, $"{id}.jpg"),
            new DateTimeOffset(2026, 10, 2, 14, 0, 0, TimeSpan.Zero).AddSeconds(seconds), manual);

    private static PhotoResult Source(int id, string number = "007", string status = "auto") =>
        new(id, status, [], "FP2", [new CarCandidate(number, 0.99, status == "manual" ? "manual" : "ocr", true)]);

    private static PhotoResult Review(int id, string? session = "FP2") =>
        new(id, "review", ["largest_car_unresolved"], session, []);

    private sealed class Similarity(double? score = 0.99) : IFrameSimilarity
    {
        public int Calls { get; private set; }
        public double? Compare(ManifestPhoto first, ManifestPhoto second)
        {
            Calls++;
            return score;
        }
    }

    [Fact]
    public void Batch_Adds007AsReviewWithoutFieldsOrPrimaryClaim()
    {
        var manifest = new Manifest(1, [Photo(1, 0), Photo(2, 1)]);
        var source = Source(1);
        var result = new BurstReviewProcessor(Entries(), new Similarity()).Apply(manifest, [source, Review(2)]);
        Assert.Same(source, result[0]);
        Assert.Equal("review", result[1].Status);
        Assert.Null(result[1].Fields);
        var candidate = Assert.Single(result[1].Cars!);
        Assert.Equal("007", candidate.Number);
        Assert.Equal("burst", candidate.Source);
        Assert.False(candidate.Primary);
        Assert.Equal(0.99, candidate.Confidence);
        Assert.Contains("burst_source:1:007", result[1].Reasons);
        Assert.Contains("largest_car_unresolved", result[1].Reasons);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("manual")]
    [InlineData("noCar")]
    [InlineData("error")]
    public void ProtectedStatuses_AreNeverModified(string status)
    {
        var comparison = new Similarity();
        var target = new PhotoResult(2, status, []);
        var result = new BurstReviewProcessor(Entries(), comparison).Apply(new Manifest(1, [Photo(1, 0), Photo(2, 1)]), [Source(1), target]);
        Assert.Same(target, result[1]);
        Assert.Equal(0, comparison.Calls);
    }

    [Fact]
    public void ManualSource_IsAllowedButManualTargetInputAlwaysWins()
    {
        var processor = new BurstReviewProcessor(Entries(), new Similarity());
        var results = processor.Apply(new Manifest(1, [Photo(1, 0, "007"), Photo(2, 1), Photo(3, 1, "unknown")]),
            [Source(1, status: "manual"), Review(2), Review(3)]);
        Assert.Contains("burst_propagation_review", results[1].Reasons);
        Assert.DoesNotContain("burst_propagation_review", results[2].Reasons);
    }

    [Fact]
    public void Proposals_AreNotPropagatedAgain()
    {
        var results = new BurstReviewProcessor(Entries(), new Similarity()).Apply(
            new Manifest(1, [Photo(3, 3), Photo(2, 1.8), Photo(1, 0)]), [Review(3), Review(2), Source(1)]);
        Assert.Empty(results[0].Cars!);
        Assert.Single(results[1].Cars!);
    }

    [Fact]
    public void DifferentSourceNumbers_RemainAmbiguousReview()
    {
        var results = new BurstReviewProcessor(Entries(), new Similarity()).Apply(
            new Manifest(1, [Photo(1, 0), Photo(2, 1), Photo(3, 2)]), [Source(1), Review(2), Source(3, "7")]);
        Assert.Equal("review", results[1].Status);
        Assert.Contains("burst_ambiguous", results[1].Reasons);
        Assert.Equal(new[] { "007", "7" }, results[1].Cars!.Select(car => car.Number));
        Assert.Null(results[1].Fields);
    }

    [Fact]
    public void RepeatedNumberSources_AreDeduplicatedAndApplicationIsIdempotent()
    {
        var manifest = new Manifest(1, [Photo(1, 0), Photo(2, 1), Photo(3, 2)]);
        var processor = new BurstReviewProcessor(Entries(), new Similarity());
        var first = processor.Apply(manifest, [Source(1), Review(2), Source(3)]);
        var second = processor.Apply(manifest, first);
        Assert.Single(second[1].Cars!);
        Assert.Equal(first[1].Cars, second[1].Cars);
        Assert.Equal(first[1].Reasons, second[1].Reasons);
    }

    [Theory]
    [InlineData(3, "burst", "FP2")]
    [InlineData(1, "other", "FP2")]
    [InlineData(1, "burst", "Race")]
    public void TimeFolderAndSessionBoundaries_AreCheckedBeforeSimilarity(double seconds, string folder, string session)
    {
        var comparison = new Similarity();
        var result = new BurstReviewProcessor(Entries(), comparison).Apply(
            new Manifest(1, [Photo(1, 0), Photo(2, seconds, folder: folder)]), [Source(1), Review(2, session)]);
        Assert.Empty(result[1].Cars!);
        Assert.Equal(0, comparison.Calls);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(0.90)]
    public void InvalidOrLowSimilarity_CannotPropose(double score)
    {
        var results = new BurstReviewProcessor(Entries(), new Similarity(score)).Apply(
            new Manifest(1, [Photo(1, 0), Photo(2, 1)]), [Source(1), Review(2)]);
        Assert.Empty(results[1].Cars!);
    }

    [Fact]
    public void MissingTimestampOrPreview_CannotPropose()
    {
        var processor = new BurstReviewProcessor(Entries(), new Similarity());
        var results = processor.Apply(new Manifest(1, [Photo(1, 0), Photo(2, 1) with { CaptureTime = default }]), [Source(1), Review(2)]);
        Assert.Empty(results[1].Cars!);
        results = new BurstReviewProcessor(Entries(), new Similarity(null)).Apply(
            new Manifest(1, [Photo(1, 0), Photo(2, 1)]), [Source(1), Review(2)]);
        Assert.Empty(results[1].Cars!);
    }

    [Fact]
    public void Limits_AreInclusive()
    {
        var result = new BurstReviewProcessor(Entries(), new Similarity(0.95)).Apply(
            new Manifest(1, [Photo(1, 0), Photo(2, 2)]), [Source(1), Review(2)]);
        Assert.Single(result[1].Cars!);
    }

    [Fact]
    public void MultiCarOrUnknownNumberSource_CannotPropose()
    {
        var multi = Source(1) with { Cars = [new("007", 0.99, "ocr", true), new("7", 0.99, "ocr", false)] };
        var processor = new BurstReviewProcessor(Entries(), new Similarity());
        var manifest = new Manifest(1, [Photo(1, 0), Photo(2, 1)]);
        Assert.Empty(processor.Apply(manifest, [multi, Review(2)])[1].Cars!);
        Assert.Empty(processor.Apply(manifest, [Source(1, "999"), Review(2)])[1].Cars!);
    }

    [Fact]
    public void CameraClockTime_IsUsedWithoutOffsetConversion()
    {
        var source = Photo(1, 0);
        var target = Photo(2, 1) with { CaptureTime = new DateTimeOffset(source.CaptureTime.DateTime.AddSeconds(1), TimeSpan.FromHours(2)) };
        Assert.Single(BurstPropagation.Propose(source, "007", [new(target, 1)], TimeSpan.FromSeconds(2), 0.95));
    }

    [Theory]
    [InlineData(double.NaN, 0.95)]
    [InlineData(2, double.PositiveInfinity)]
    [InlineData(-1, 0.95)]
    public void InvalidOptions_AreRejected(double gap, double similarity) =>
        Assert.Throws<InvalidDataException>(() => new BurstOptions(gap, similarity).Validate());

    [Fact]
    public void AppearanceScore_IsSpatialAndBounded()
    {
        Assert.Equal(1, PreviewFrameSimilarity.Score([0, 100, 255], [0, 100, 255]));
        Assert.Equal(0, PreviewFrameSimilarity.Score([0, 0, 0], [255, 255, 255]));
        Assert.InRange(PreviewFrameSimilarity.Score([255, 0], [0, 255]), 0, 0.01);
        Assert.Throws<ArgumentException>(() => PreviewFrameSimilarity.Score([1], [1, 2]));
    }

    [Fact]
    public void MissingOrDuplicateResultIds_AreRejected()
    {
        var processor = new BurstReviewProcessor(Entries(), new Similarity());
        var manifest = new Manifest(1, [Photo(1, 0), Photo(2, 1)]);
        Assert.Throws<InvalidDataException>(() => processor.Apply(manifest, [Source(1)]));
        Assert.Throws<InvalidDataException>(() => processor.Apply(manifest, [Source(1), Source(1)]));
    }

    private sealed class FailingSimilarity(bool cancel) : IFrameSimilarity
    {
        public double? Compare(ManifestPhoto first, ManifestPhoto second) =>
            throw (cancel ? new OperationCanceledException() : new IOException("Unavailable preview"));
    }

    [Fact]
    public void ComparisonFailure_PreservesReviewAndCancellationIsNotSwallowed()
    {
        var manifest = new Manifest(1, [Photo(1, 0), Photo(2, 1)]);
        PhotoResult[] results = [Source(1), Review(2)];
        var result = new BurstReviewProcessor(Entries(), new FailingSimilarity(false)).Apply(manifest, results)[1];
        Assert.Equal("review", result.Status);
        Assert.Contains("burst_similarity_error:1:IOException", result.Reasons);
        Assert.Empty(result.Cars!);
        Assert.Throws<OperationCanceledException>(() => new BurstReviewProcessor(Entries(), new FailingSimilarity(true)).Apply(manifest, results));
    }

    [Theory]
    [InlineData("123", 1230000)]
    [InlineData("123456789", 1234567)]
    [InlineData("invalid", 0)]
    public void ExifSubseconds_ArePreserved(string fraction, long ticks)
    {
        var time = new DateTime(2026, 10, 2, 14, 0, 0);
        Assert.Equal(time.AddTicks(ticks), ExifCaptureTimeReader.AddSubseconds(time, fraction).DateTime);
    }
}
