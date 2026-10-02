using System.Text;
using XiPHiAS.GridTag.Vision;
using Xunit;

namespace XiPHiAS.GridTag.Cli.Tests;

public sealed class BurstExifTests
{
    [Fact]
    public void ReadsOriginalExifClockAndFractionWithoutUsingFileTimestamp()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gridtag-exif-{Guid.NewGuid():N}.jpg");
        try
        {
            // Minimal JPEG with an APP1 EXIF IFD: original time and inline subsecond ASCII.
            using var tiffStream = new MemoryStream();
            using (var writer = new BinaryWriter(tiffStream, Encoding.ASCII, leaveOpen: true))
            {
                writer.Write(new byte[] { (byte)'I', (byte)'I' });
                writer.Write((ushort)42);
                writer.Write(8u);
                writer.Write((ushort)1);
                writer.Write((ushort)0x8769); // EXIF SubIFD pointer.
                writer.Write((ushort)4);
                writer.Write(1u);
                writer.Write(26u);
                writer.Write(0u);
                writer.Write((ushort)2);
                writer.Write((ushort)0x9003); // DateTimeOriginal, ASCII at offset 56.
                writer.Write((ushort)2);
                writer.Write(20u);
                writer.Write(56u);
                writer.Write((ushort)0x9291); // SubSecTimeOriginal, four bytes stored inline.
                writer.Write((ushort)2);
                writer.Write(4u);
                writer.Write(new byte[] { (byte)'1', (byte)'2', (byte)'3', 0 });
                writer.Write(0u);
                writer.Write(Encoding.ASCII.GetBytes("2026:10:02 14:00:00\0"));
            }
            byte[] payload = [.. Encoding.ASCII.GetBytes("Exif\0\0"), .. tiffStream.ToArray()];
            var length = payload.Length + 2;
            File.WriteAllBytes(path, [0xff, 0xd8, 0xff, 0xe1, (byte)(length >> 8), (byte)length, .. payload, 0xff, 0xd9]);
            File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1));
            Assert.Equal(new DateTimeOffset(2026, 10, 2, 14, 0, 0, TimeSpan.Zero).AddMilliseconds(123), ExifCaptureTimeReader.Read(path));
            TimingEvalUsesExifClock(path);
            Assert.Null(ExifCaptureTimeReader.Read(path + ".missing"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void TimingEvalUsesExifClock(string imagePath)
    {
        var prefix = Path.Combine(Path.GetTempPath(), $"gridtag-timing-eval-{Guid.NewGuid():N}");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md")))
            root = root.Parent;
        Assert.NotNull(root);
        try
        {
            File.WriteAllText(prefix + ".csv", $"path;numbers\n{imagePath};968\n");
            File.WriteAllText(prefix + ".timing.csv", "number;time\n");
            File.WriteAllText(prefix + ".session.json", """
                {"seriesName":"Test","eventFullName":"Test","location":"Test","defaultSession":"Fallback",
                "sessions":[{"code":"EXIF","name":"Exif session","start":"2026-10-02T13:59:00Z","end":"2026-10-02T14:01:00Z"}]}
                """);
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(0, CliApp.Run(["eval", "--labels", prefix + ".csv",
                "--entrylist", Path.Combine(root.FullName, "samples", "entrylist.csv"),
                "--session", prefix + ".session.json", "--timing-csv", prefix + ".timing.csv",
                "--out", prefix + ".results.json"], output, error));
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(prefix + ".results.json"));
            Assert.Equal("EXIF", json.RootElement.GetProperty("photos")[0].GetProperty("session").GetString());
        }
        finally
        {
            foreach (var suffix in new[] { ".csv", ".timing.csv", ".session.json", ".results.json" })
                File.Delete(prefix + suffix);
        }
    }
}
