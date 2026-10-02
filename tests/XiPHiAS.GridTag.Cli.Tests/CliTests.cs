using System.Text.Json;
using XiPHiAS.GridTag.Cli;
using Xunit;

namespace XiPHiAS.GridTag.Cli.Tests;

public sealed class CliTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Theory]
    [InlineData("invalid", true)]
    [InlineData("25:00:00", true)]
    [InlineData("00:00:05", false)]
    public void InvalidTimingOptionsReturnInputError(string offset, bool withCsv)
    {
        var path = Path.Combine(Path.GetTempPath(), $"gridtag-timing-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(path, "number;time\n");
            var args = new List<string> { "eval", "--labels", Path.Combine(RepositoryRoot, "samples", "labels.example.csv"),
                "--entrylist", Path.Combine(RepositoryRoot, "samples", "entrylist.csv"),
                "--session", Path.Combine(RepositoryRoot, "samples", "session.example.json"), "--clock-offset", offset };
            if (withCsv)
                args.AddRange(["--timing-csv", path]);
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(3, CliApp.Run(args.ToArray(), output, error));
            Assert.Contains("clock-offset", error.ToString());
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("--burst-max-gap", "-1")]
    [InlineData("--burst-max-gap", "NaN")]
    [InlineData("--burst-similarity", "1.1")]
    [InlineData("--burst-similarity", "invalid")]
    public void InvalidBurstOptions_ReturnInputError(string option, string value)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = CliApp.Run(["eval", "--labels", Path.Combine(RepositoryRoot, "samples", "labels.example.csv"),
            "--entrylist", Path.Combine(RepositoryRoot, "samples", "entrylist.csv"),
            "--session", Path.Combine(RepositoryRoot, "samples", "session.example.json"), option, value], output, error);
        Assert.Equal(3, code);
        Assert.Contains("burst", error.ToString());
    }

    [Fact]
    public void Eval_WritesOptionalPerPhotoResultsWithoutChangingTheContract()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gridtag-eval-{Guid.NewGuid():N}.json");
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var code = CliApp.Run(["eval", "--labels", Path.Combine(RepositoryRoot, "samples", "labels.example.csv"),
                "--entrylist", Path.Combine(RepositoryRoot, "samples", "entrylist.csv"),
                "--session", Path.Combine(RepositoryRoot, "samples", "session.example.json"), "--out", path], output, error);
            Assert.Equal(0, code);
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(4, json.RootElement.GetProperty("photos").GetArrayLength());
            Assert.Contains("auto photos: 0", output.ToString());
            Assert.Contains("model observations: 0", output.ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("--number-ocr", "number-ocr-model")]
    [InlineData("--driver-name-detector", "driver-name-model")]
    public void PaddleEvidenceOptions_RequireRecognizer(string option, string missingOption)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = CliApp.Run(["eval", "--labels", Path.Combine(RepositoryRoot, "samples", "labels.example.csv"),
            "--entrylist", Path.Combine(RepositoryRoot, "samples", "entrylist.csv"),
            "--session", Path.Combine(RepositoryRoot, "samples", "session.example.json"), option, "missing.onnx"], output, error);
        Assert.Equal(3, code);
        Assert.Contains(missingOption, error.ToString());
    }

    [Theory]
    [InlineData("--car-model", "missing.onnx", "car-model-labels")]
    [InlineData("--driver-name-model", "missing.onnx", "driver-name-alphabet")]
    public void EvidenceOptions_RequireMatchingVocabulary(string option, string path, string missingOption)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = CliApp.Run(["eval", "--labels", Path.Combine(RepositoryRoot, "samples", "labels.example.csv"),
            "--entrylist", Path.Combine(RepositoryRoot, "samples", "entrylist.csv"),
            "--session", Path.Combine(RepositoryRoot, "samples", "session.example.json"), option, path], output, error);
        Assert.Equal(3, code);
        Assert.Contains(missingOption, error.ToString());
    }

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
