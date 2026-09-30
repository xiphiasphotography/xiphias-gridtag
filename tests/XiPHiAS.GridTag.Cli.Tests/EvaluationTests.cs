using GridTag.Core;
using Xunit;

namespace GridTag.Cli.Tests;

public sealed class EvaluationTests
{
    [Fact]
    public void Evaluation_WithFakePipelineProviders_ReportsRequiredMetrics()
    {
        var labels = new[]
        {
            new EvaluationLabel("correct.arw", ["69"]),
            new EvaluationLabel("wrong.arw", ["69"]),
            new EvaluationLabel("review.arw", ["69"]),
            new EvaluationLabel("no-car.arw", []),
        };
        var pipeline = CreatePipeline();
        var runner = new EvaluationRunner();

        var report = runner.Evaluate(labels, label => ProcessWithFakes(pipeline, label));

        Assert.Equal(4, report.TotalPhotos);
        Assert.Equal(3, report.LabeledCarPhotos);
        Assert.Equal(2, report.AutoPhotos);
        Assert.Equal(1, report.CorrectAutoPhotos);
        Assert.Equal(1, report.ReviewPhotos);
        Assert.Equal(1, report.WrongAutoPhotos);
        Assert.Equal(0.5, report.AutoPrecision);
        Assert.Equal(1.0 / 3.0, report.Recall, 6);
        Assert.Equal(0.25, report.ReviewRate);
        Assert.Equal(1, report.ReasonCounts["small_margin"]);
        Assert.Equal(1, report.ReasonCounts["no_preview"]);
        Assert.Equal(1, report.Confusions["69->3"]);
        Assert.False(report.MeetsGoNoGo);
    }

    [Fact]
    public void Evaluation_CanMeetGoNoGoWithFakeCorrectResults()
    {
        var labels = Enumerable.Range(1, 100)
            .Select(index => new EvaluationLabel($"photo-{index}.arw", ["69"]))
            .ToArray();
        var runner = new EvaluationRunner();

        var report = runner.Evaluate(labels, label => new PhotoResult(
            labels[0].Path == label.Path ? 1 : 2,
            "auto",
            [],
            "FP2",
            [new CarCandidate("69", 0.99, "fake", true)],
            null));

        Assert.Equal(1.0, report.AutoPrecision);
        Assert.Equal(1.0, report.Recall);
        Assert.True(report.MeetsGoNoGo);
    }

    private static PhotoResult ProcessWithFakes(TaggingPipeline pipeline, EvaluationLabel label)
    {
        return pipeline.ProcessPhoto(new ManifestPhoto(
            1,
            label.Path,
            label.Path,
            new DateTimeOffset(2026, 9, 18, 14, 0, 0, TimeSpan.Zero),
            label.Path == "no-car.arw" ? null : null));
    }

    private static TaggingPipeline CreatePipeline()
    {
        return new TaggingPipeline(
            new EntryList(
            [
                new Entry("3", "Team 3", "Car 3", "PRO", []),
                new Entry("69", "Team 69", "Car 69", "PRO", []),
                new Entry("96", "Team 96", "Car 96", "PRO", []),
            ]),
            EventContext.Default(
                "Series",
                "Event",
                "Location",
                "FP2",
                [new EventSession("FP2", "Free Practice 2", new DateTimeOffset(2026, 9, 18, 13, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 18, 15, 0, 0, TimeSpan.Zero))]),
            new FakePreviewProvider(),
            new FakeCarDetector(),
            new FakePlateReader());
    }

    private sealed class FakePreviewProvider : IRawPreviewProvider
    {
        public IPreview? GetPreview(string path) => path == "no-car.arw" ? null : new FakePreview(path);
    }

    private sealed class FakeCarDetector : ICarDetector
    {
        public IReadOnlyList<DetectedCar> Detect(IPreview preview) => [new DetectedCar(((FakePreview)preview).Id, 1.0)];
    }

    private sealed class FakePlateReader : IPlateReader
    {
        public IReadOnlyList<NumberHypothesis> ReadNumbers(IPreview preview, DetectedCar detectedCar) => detectedCar.Id switch
        {
            "correct.arw" => [new NumberHypothesis("69", 0.99)],
            "wrong.arw" => [new NumberHypothesis("3", 0.99)],
            "review.arw" => [new NumberHypothesis("69", 0.91), new NumberHypothesis("96", 0.90)],
            _ => []
        };
    }

    private sealed record FakePreview(string Id) : IPreview
    {
        public int Width => 100;

        public int Height => 60;
    }
}
