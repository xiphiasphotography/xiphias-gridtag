using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using XiPHiAS.GridTag.Core;

namespace XiPHiAS.GridTag.Vision;

/// <summary>Local full-frame appearance comparison using small spatial RGB previews.</summary>
public sealed class PreviewFrameSimilarity(IRawPreviewProvider previewProvider) : IFrameSimilarity
{
    private readonly Dictionary<string, byte[]?> descriptors = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public double? Compare(ManifestPhoto first, ManifestPhoto second)
    {
        var left = GetDescriptor(first.Path);
        var right = GetDescriptor(second.Path);
        return left is null || right is null ? null : Score(left, right);
    }

    /// <summary>Returns one minus normalized mean absolute RGB difference; not an identity probability.</summary>
    public static double Score(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        if (first.Length == 0 || first.Length != second.Length)
            throw new ArgumentException("Similarity descriptors must have the same nonzero length.");
        long difference = 0;
        for (var index = 0; index < first.Length; index++)
            difference += Math.Abs(first[index] - second[index]);
        return 1 - difference / (255.0 * first.Length);
    }

    private byte[]? GetDescriptor(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (descriptors.TryGetValue(fullPath, out var cached))
            return cached;
        if (previewProvider.GetPreview(fullPath) is not RawPreview preview)
        {
            descriptors[fullPath] = null;
            return null;
        }
        using var stream = new MemoryStream(preview.JpegBytes, writable: false);
        using var image = Image.FromStream(stream);
        using var bitmap = new Bitmap(32, 32, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.DrawImage(image, new Rectangle(0, 0, 32, 32));
        }
        var descriptor = new byte[32 * 32 * 3];
        for (var y = 0; y < 32; y++)
            for (var x = 0; x < 32; x++)
            {
                var color = bitmap.GetPixel(x, y);
                var offset = (y * 32 + x) * 3;
                descriptor[offset] = color.R;
                descriptor[offset + 1] = color.G;
                descriptor[offset + 2] = color.B;
            }
        descriptors[fullPath] = descriptor;
        return descriptor;
    }
}

/// <summary>Reads original camera clock time for batch evaluation, without filesystem-time fallbacks.</summary>
public static class ExifCaptureTimeReader
{
    /// <summary>Returns DateTimeOriginal including subsecond digits, or null for unavailable EXIF.</summary>
    public static DateTimeOffset? Read(string path)
    {
        try
        {
            var directory = ImageMetadataReader.ReadMetadata(path).OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (directory is null || !directory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var time))
                return null;
            var subsecond = directory.GetString(ExifDirectoryBase.TagSubsecondTimeOriginal);
            return AddSubseconds(time, subsecond);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ImageProcessingException or MetadataException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Preserves clock time and EXIF fractional seconds; offsets are intentionally ignored.</summary>
    public static DateTimeOffset AddSubseconds(DateTime time, string? subsecond)
    {
        var clock = DateTime.SpecifyKind(time, DateTimeKind.Unspecified);
        var digits = subsecond?.Trim();
        if (!string.IsNullOrEmpty(digits) && digits.All(character => character is >= '0' and <= '9'))
        {
            var fraction = digits[..Math.Min(7, digits.Length)].PadRight(7, '0');
            clock = clock.AddTicks(long.Parse(fraction, CultureInfo.InvariantCulture));
        }
        return new DateTimeOffset(clock, TimeSpan.Zero);
    }
}
