using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using XiPHiAS.GridTag.Core;

namespace XiPHiAS.GridTag.Vision;

/// <summary>Identifies where a decoded preview came from.</summary>
public enum RawPreviewSource
{
    /// <summary>Embedded JPEG thumbnail or preview from the RAW container.</summary>
    EmbeddedJpeg,
    /// <summary>Half-size decode and JPEG re-encode through the Windows decoder.</summary>
    HalfSizeDecode,
    /// <summary>Original JPEG input without thumbnail selection or downscaling.</summary>
    JpegInput,
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

            if (Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = File.ReadAllBytes(path);
                using var stream = new MemoryStream(bytes, writable: false);
                using var image = Image.FromStream(stream, false, true);
                return new RawPreview(bytes, image.Width, image.Height, RawPreviewSource.JpegInput);
            }

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
        var bytes = File.ReadAllBytes(path);
        RawPreview? best = null;
        for (var offset = 0; offset < bytes.Length - 3; offset++)
        {
            if (bytes[offset] != 0xff || bytes[offset + 1] != 0xd8 || bytes[offset + 2] != 0xff)
                continue;
            var end = FindJpegEnd(bytes, offset);
            if (end < 0)
                continue;
            try
            {
                using var stream = new MemoryStream(bytes, offset, end - offset, writable: false);
                using var image = Image.FromStream(stream, false, true);
                if (best is null || (long)image.Width * image.Height > (long)best.Width * best.Height)
                    best = new RawPreview(bytes.AsSpan(offset, end - offset).ToArray(), image.Width, image.Height, RawPreviewSource.EmbeddedJpeg);
            }
            catch (ArgumentException) { } // Non-display JPEGs such as lossless RAW sensor data.
            catch (OutOfMemoryException) { } // GDI+ also uses this for unsupported JPEG encoding.
        }
        return best;
    }

    private static int FindJpegEnd(byte[] bytes, int start)
    {
        var position = start + 2;
        var entropy = false;
        while (position < bytes.Length - 1)
        {
            if (entropy && bytes[position] != 0xff) { position++; continue; }
            if (bytes[position++] != 0xff) return -1;
            while (position < bytes.Length && bytes[position] == 0xff) position++;
            if (position >= bytes.Length) return -1;
            var marker = bytes[position++];
            if (entropy && (marker == 0 || marker is >= 0xd0 and <= 0xd7)) continue;
            if (marker == 0xd9) return position;
            if (marker == 0xd8 || marker == 0) return -1;
            if (marker == 0x01) continue;
            if (position > bytes.Length - 2) return -1;
            var length = bytes[position] * 256 + bytes[position + 1];
            if (length < 2 || length > bytes.Length - position) return -1;
            position += length;
            entropy = marker == 0xda;
        }
        return -1;
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
