using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using GridTag.Core;

namespace GridTag.Vision;

/// <summary>Identifies where a decoded preview came from.</summary>
public enum RawPreviewSource
{
    /// <summary>Embedded JPEG thumbnail or preview from the RAW container.</summary>
    EmbeddedJpeg,
    /// <summary>Half-size decode and JPEG re-encode through the Windows decoder.</summary>
    HalfSizeDecode,
}

/// <summary>Decoded preview payload passed to later vision stages.</summary>
public sealed record RawPreview(byte[] JpegBytes, int Width, int Height, RawPreviewSource Source) : IPreview;

/// <summary>Reads an embedded JPEG first and falls back to a half-size Windows decode.</summary>
/// <remarks>
/// The fallback uses the Windows System.Drawing decoder and therefore depends on an installed
/// Windows codec for the particular RAW format. All failures are converted to a null result.
/// </remarks>
public sealed class EmbeddedJpegRawPreviewProvider : IRawPreviewProvider
{
    /// <summary>Returns a preview, or null for missing, unsupported, corrupt, or unreadable files.</summary>
    public IPreview? GetPreview(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            var embedded = TryReadEmbeddedJpeg(path);
            if (embedded is not null)
                return embedded;

            return TryDecodeHalfSize(path);
        }
        catch
        {
            return null;
        }
    }

    private static RawPreview? TryReadEmbeddedJpeg(string path)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(path);
            var directory = directories.OfType<ExifThumbnailDirectory>().FirstOrDefault();
            if (directory is null || !directory.TryGetInt64(ExifThumbnailDirectory.TagThumbnailLength, out var length) || length <= 0)
                return null;

            var offset = directory.AdjustedThumbnailOffset;
            if (!offset.HasValue || offset.Value < 0 || length > int.MaxValue || offset.Value > new FileInfo(path).Length - length)
                return null;
            var thumbnail = new byte[(int)length];
            using (var file = File.OpenRead(path))
            {
                file.Position = offset.Value;
                var read = file.Read(thumbnail, 0, thumbnail.Length);
                if (read != thumbnail.Length)
                    return null;
            }

            using var stream = new MemoryStream(thumbnail, writable: false);
            using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            return new RawPreview(thumbnail, image.Width, image.Height, RawPreviewSource.EmbeddedJpeg);
        }
        catch
        {
            return null;
        }
    }

    private static RawPreview? TryDecodeHalfSize(string path)
    {
        try
        {
            using var source = Image.FromFile(path);
            var width = Math.Max(1, source.Width / 2);
            var height = Math.Max(1, source.Height / 2);
            using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CompositingQuality = CompositingQuality.HighSpeed;
                graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, width, height));
            }

            using var output = new MemoryStream();
            bitmap.Save(output, ImageFormat.Jpeg);
            return new RawPreview(output.ToArray(), width, height, RawPreviewSource.HalfSizeDecode);
        }
        catch
        {
            return null;
        }
    }
}
