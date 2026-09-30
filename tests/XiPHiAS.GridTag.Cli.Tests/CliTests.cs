using System.Text.Json;
using GridTag.Cli;
using Xunit;

namespace GridTag.Cli.Tests;

public sealed class CliTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Run_SampleManifestProducesExpectedStatusesAndManualFields()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"gridtag-cli-{Guid.NewGuid():N}.json");
        var code = Run(
            "run",
            "--manifest", Path.Combine(RepositoryRoot, "samples", "manifest.example.json"),
            "--entrylist", Path.Combine(RepositoryRoot, "samples", "entrylist.csv"),
            "--session", Path.Combine(RepositoryRoot, "samples", "session.example.json"),
            "--out", outputPath);

        Assert.Equal(0, code);
        using var actual = JsonDocument.Parse(File.ReadAllText(outputPath));
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot, "samples", "results.example.json")));
        var photos = actual.RootElement.GetProperty("photos").EnumerateArray().ToArray();
        var expectedPhotos = expected.RootElement.GetProperty("photos").EnumerateArray().ToArray();
        Assert.Equal(new[] { 1001, 1002, 1003, 1004 }, photos.Select(photo => photo.GetProperty("id").GetInt32()));
        Assert.All(photos.Take(3), photo =>
        {
            Assert.Equal("error", photo.GetProperty("status").GetString());
            Assert.Equal("no_preview", photo.GetProperty("reasons")[0].GetString());
        });
        var manual = photos[3];
        Assert.Equal("manual", manual.GetProperty("status").GetString());
        Assert.True(JsonElement.DeepEquals(manual.GetProperty("fields"), expectedPhotos[3].GetProperty("fields")));
        File.Delete(outputPath);
    }

    [Fact]
    public void MissingCommand_ReturnsExitCode2()
    {
        Assert.Equal(2, Run());
    }

    [Fact]
    public void MissingInputFile_ReturnsExitCode3()
    {
        Assert.Equal(3, Run("check-entrylist", "--entrylist", Path.Combine(RepositoryRoot, "missing.csv")));
    }

    [Fact]
    public void Version_ReturnsExitCode0()
    {
        Assert.Equal(0, Run("version"));
    }

    private static int Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        return CliApp.Run(args, output, error);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
