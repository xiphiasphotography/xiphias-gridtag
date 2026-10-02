using Xunit;

namespace XiPHiAS.GridTag.Core.Tests;

public sealed class PhotoIOTests
{
    [Fact]
    public void SourceAndSinkUseTheSamePipelineAsManifestProcessing()
    {
        var entries = new EntryList([new Entry("007", "Team", "Car", "PRO", [new Driver("Driver", "NED")])]);
        var context = EventContext.Default("Series", "Event", "Track", "", []);
        var pipeline = new TaggingPipeline(entries, context, new NoPreview(), new NoCars(), new NoNumbers());
        var manifest = new Manifest(1, [new ManifestPhoto(1, "", "photo.ARW", default, "007")]);
        var sink = new CapturingSink();
        var actual = pipeline.Process(new Source(manifest), sink);
        var expected = pipeline.Process(manifest);

        Assert.Same(manifest, sink.Manifest);
        Assert.Same(actual, sink.Results);
        Assert.Equal(expected.Photos[0].Status, actual.Photos[0].Status);
        Assert.Equal(expected.Photos[0].Fields!.Headline, actual.Photos[0].Fields!.Headline);
        Assert.Equal("manual", actual.Photos[0].Status);
    }

    private sealed class Source(Manifest manifest) : IPhotoSource
    {
        public Manifest Read() => manifest;
    }

    private sealed class CapturingSink : IResultSink
    {
        public Manifest? Manifest { get; private set; }
        public ResultFile? Results { get; private set; }

        public ResultFile Write(Manifest manifest, ResultFile results)
        {
            Manifest = manifest;
            Results = results;
            return results;
        }
    }

    private sealed class NoPreview : IRawPreviewProvider
    {
        public IPreview? GetPreview(string path) => null;
    }

    private sealed class NoCars : ICarDetector
    {
        public IReadOnlyList<DetectedCar> Detect(IPreview preview) => [];
    }

    private sealed class NoNumbers : IPlateReader
    {
        public IReadOnlyList<NumberHypothesis> ReadNumbers(IPreview preview, DetectedCar detectedCar) => [];
    }
}
