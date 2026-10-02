using System.Text.Json;
using XiPHiAS.GridTag.Review;
using Xunit;

namespace XiPHiAS.GridTag.Core.Tests;

public sealed class ReviewSessionTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "gridtag-review-" + Guid.NewGuid());

    public ReviewSessionTests() => Directory.CreateDirectory(folder);

    private string Write(string name, string json)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public async Task Load_FiltersStatusesPreservesOrderAndJoinsById()
    {
        var results = Write("results.json", """
            {"schemaVersion":1,"photos":[
              {"id":2,"status":"noCar","reasons":["no_car_detected"]},
              {"id":1,"status":"review","reasons":["low_confidence"],"session":"FP2","cars":[{"number":"007","confidence":0.8,"source":"burst"}]},
              {"id":3,"status":"auto"},{"id":4,"status":"manual"},{"id":5,"status":"error"}]}
            """);
        var manifest = Write("manifest.json", """
            {"schemaVersion":1,"photos":[{"id":1,"path":"one.ARW"},{"id":2,"path":"two.ARW"}]}
            """);
        var photos = await ReviewSession.LoadAsync(results, manifest, CancellationToken.None);
        Assert.Equal([2, 1], photos.Select(photo => photo.Id));
        Assert.Equal(Path.Combine(folder, "two.ARW"), photos[0].Path);
        Assert.Equal("low_confidence", photos[1].Reasons);
        Assert.Equal("FP2", photos[1].Session);
        Assert.Contains("#007", photos[1].Candidates);
    }

    [Fact]
    public async Task Load_WithoutManifestKeepsPhotosWithoutPaths()
    {
        var results = Write("results.json", """{"schemaVersion":1,"photos":[{"id":1,"status":"review"}]}""");
        var photo = Assert.Single(await ReviewSession.LoadAsync(results, null, CancellationToken.None));
        Assert.Empty(photo.Path);
        Assert.Empty(photo.Reasons);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2,\"photos\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"photos\":{}}")]
    [InlineData("{\"schemaVersion\":1,\"photos\":[{\"id\":1,\"status\":\"review\"},{\"id\":1,\"status\":\"noCar\"}]}")]
    public async Task Load_RejectsInvalidContracts(string json)
    {
        var results = Write("results.json", json);
        await Assert.ThrowsAsync<InvalidDataException>(() => ReviewSession.LoadAsync(results, null, CancellationToken.None));
    }

    [Fact]
    public async Task Load_RejectsInvalidManifestVersion()
    {
        var results = Write("results.json", """{"schemaVersion":1,"photos":[]}""");
        var manifest = Write("manifest.json", """{"schemaVersion":9,"photos":[]}""");
        await Assert.ThrowsAsync<InvalidDataException>(() => ReviewSession.LoadAsync(results, manifest, CancellationToken.None));
    }

    [Fact]
    public async Task Load_RejectsMalformedJson()
    {
        var results = Write("results.json", "invalid");
        await Assert.ThrowsAnyAsync<JsonException>(() => ReviewSession.LoadAsync(results, null, CancellationToken.None));
    }

    [Fact]
    public async Task Load_HonorsCancellation()
    {
        var results = Write("results.json", """{"schemaVersion":1,"photos":[]}""");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReviewSession.LoadAsync(results, null, cancellation.Token));
    }

    public void Dispose() => Directory.Delete(folder, recursive: true);
}
