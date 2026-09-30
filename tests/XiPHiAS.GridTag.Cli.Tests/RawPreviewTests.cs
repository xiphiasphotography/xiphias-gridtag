using System.Drawing;
using System.Drawing.Imaging;
using GridTag.Cli;
using GridTag.Vision;
using Xunit;

namespace GridTag.Cli.Tests;

public sealed class RawPreviewTests
{
    [Fact]
    public void Provider_DecodesJpegFallbackToPreview()
    {
        var path = CreateJpegFile();
        try
        {
            var preview = new EmbeddedJpegRawPreviewProvider().GetPreview(path) as RawPreview;

            Assert.NotNull(preview);
            Assert.Equal(RawPreviewSource.HalfSizeDecode, preview.Source);
            Assert.Equal(2, preview.Width);
            Assert.Equal(1, preview.Height);
            Assert.NotEmpty(preview.JpegBytes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Provider_ReturnsNullForMissingOrCorruptInput()
    {
        var provider = new EmbeddedJpegRawPreviewProvider();

        Assert.Null(provider.GetPreview(Path.Combine(Path.GetTempPath(), "gridtag-missing.arw")));
        var path = WriteTempFile([1, 2, 3, 4], ".arw");
        try
        {
            Assert.Null(provider.GetPreview(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PreviewCommand_ReturnsOneWhenNoPreviewExists()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var code = CliApp.Run(["preview", "--file", "missing.arw", "--out", "preview.jpg"], output, error);

        Assert.Equal(1, code);
        Assert.Contains("Geen preview", error.ToString());
    }

    [Fact]
    public void PreviewCommand_WritesDecodedJpeg()
    {
        var inputPath = CreateJpegFile();
        var outputPath = Path.Combine(Path.GetTempPath(), $"gridtag-preview-out-{Guid.NewGuid():N}.jpg");
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var code = CliApp.Run(["preview", "--file", inputPath, "--out", outputPath], output, error);

            Assert.Equal(0, code);
            Assert.True(File.Exists(outputPath));
            Assert.NotEmpty(File.ReadAllBytes(outputPath));
            Assert.Contains("Preview:", output.ToString());
        }
        finally
        {
            File.Delete(inputPath);
            File.Delete(outputPath);
        }
    }

    private static string WriteTempFile(byte[] bytes, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"gridtag-preview-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string CreateJpegFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gridtag-preview-{Guid.NewGuid():N}.jpg");
        using var bitmap = new Bitmap(4, 2);
        bitmap.Save(path, ImageFormat.Jpeg);
        return path;
    }
}
