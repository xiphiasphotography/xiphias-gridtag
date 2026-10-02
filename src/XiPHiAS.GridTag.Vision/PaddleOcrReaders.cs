using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using XiPHiAS.GridTag.Core;

namespace XiPHiAS.GridTag.Vision;

internal static class EvidenceImagePreprocessor
{
    public static float[] Resize(Bitmap image, int width, int height, bool bgr, bool imageNet = false)
    {
        using var resized = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.InterpolationMode = InterpolationMode.Bilinear;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawImage(image, new Rectangle(0, 0, width, height), new Rectangle(0, 0, image.Width, image.Height), GraphicsUnit.Pixel);
        }
        var data = resized.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var bytes = new byte[Math.Abs(data.Stride) * height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var area = width * height;
            var tensor = new float[3 * area];
            float[] means = [0.485f, 0.456f, 0.406f];
            float[] deviations = [0.229f, 0.224f, 0.225f];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    for (var channel = 0; channel < 3; channel++)
                    {
                        var value = bytes[y * data.Stride + x * 3 + (bgr ? channel : 2 - channel)] / 255f;
                        tensor[channel * area + y * width + x] = imageNet
                            ? (value - means[channel]) / deviations[channel]
                            : (value - 0.5f) / 0.5f;
                    }
            return tensor;
        }
        finally
        {
            resized.UnlockBits(data);
        }
    }

    public static Bitmap? Crop(IPreview preview, DetectedCar car)
    {
        if (preview is not RawPreview raw || car.Bounds is not { } bounds)
            return null;
        using var stream = new MemoryStream(raw.JpegBytes, writable: false);
        using var image = new Bitmap(stream);
        var left = Math.Clamp((int)MathF.Floor(bounds.Left), 0, image.Width);
        var top = Math.Clamp((int)MathF.Floor(bounds.Top), 0, image.Height);
        var right = Math.Clamp((int)MathF.Ceiling(bounds.Right), 0, image.Width);
        var bottom = Math.Clamp((int)MathF.Ceiling(bounds.Bottom), 0, image.Height);
        return right <= left || bottom <= top ? null :
            image.Clone(new Rectangle(left, top, right - left, bottom - top), PixelFormat.Format24bppRgb);
    }
}

/// <summary>One local text-line observation; no entry-list or ground-truth information is used.</summary>
public sealed record OcrTextObservation(string Text, double Confidence);

/// <summary>Local PaddleOCR DB detector and Latin CTC recognizer shared by number and name adapters.</summary>
public sealed class PaddleOcrTextReader : IDisposable
{
    private readonly InferenceSession detector;
    private readonly InferenceSession recognizer;
    private readonly string detectorInput;
    private readonly string recognizerInput;
    private readonly string[] alphabet;
    private IPreview? lastPreview;
    private DetectedCar? lastCar;
    private IReadOnlyList<OcrTextObservation> lastResult = [];

    /// <summary>Loads the PP-OCRv6 detector and PP-OCRv5 Latin recognizer with its original dictionary.</summary>
    public PaddleOcrTextReader(string detectorPath, string recognizerPath, string dictionaryPath)
    {
        using var options = new SessionOptions { IntraOpNumThreads = 4 };
        detector = new InferenceSession(detectorPath, options);
        try
        {
            recognizer = new InferenceSession(recognizerPath, options);
            try
            {
                detectorInput = ValidateInput(detector);
                recognizerInput = ValidateInput(recognizer);
                alphabet = ["blank", .. File.ReadAllLines(dictionaryPath), " "];
                var shape = recognizer.OutputMetadata.Values.Single().Dimensions;
                var detShape = detector.OutputMetadata.Values.Single().Dimensions;
                if (shape.Length != 3 || shape[^1] != alphabet.Length || detShape.Length != 4 || detShape[1] != 1)
                    throw new InvalidDataException("PaddleOCR output/dictionary mismatch.");
                if (recognizer.ModelMetadata.CustomMetadataMap.TryGetValue("character", out var characters) &&
                    !characters.TrimEnd('\n', '\r').Split('\n').Select(value => value.TrimEnd('\r')).SequenceEqual(alphabet.Skip(1).SkipLast(1)))
                    throw new InvalidDataException("PaddleOCR dictionary differs from the model's embedded character order.");
            }
            catch
            {
                recognizer.Dispose();
                throw;
            }
        }
        catch
        {
            detector.Dispose();
            throw;
        }
    }

    /// <summary>Reads only the detected car; retains at most one result to avoid repeating OCR for evidence.</summary>
    public IReadOnlyList<OcrTextObservation> Read(IPreview preview, DetectedCar car)
    {
        if (ReferenceEquals(preview, lastPreview) && ReferenceEquals(car, lastCar))
            return lastResult;
        using var crop = EvidenceImagePreprocessor.Crop(preview, car);
        var result = crop is null ? [] : ReadCrop(crop);
        lastPreview = preview;
        lastCar = car;
        lastResult = result;
        return result;
    }

    private IReadOnlyList<OcrTextObservation> ReadCrop(Bitmap crop)
    {
        // Bound CPU work; PP-OCR requires dimensions divisible by 32.
        var scale = Math.Min(1.0, 960.0 / Math.Max(crop.Width, crop.Height));
        var width = Math.Max(32, (int)Math.Round(crop.Width * scale / 32) * 32);
        var height = Math.Max(32, (int)Math.Round(crop.Height * scale / 32) * 32);
        var values = EvidenceImagePreprocessor.Resize(crop, width, height, bgr: true);
        using var detections = detector.Run([NamedOnnxValue.CreateFromTensor(detectorInput, new DenseTensor<float>(values, [1, 3, height, width]))]);
        var map = detections.Single().AsTensor<float>();
        var shape = map.Dimensions.ToArray();
        if (shape.Length != 4 || shape[0] != 1 || shape[1] != 1)
            throw new InvalidDataException("Paddle text detector must output [1,1,height,width].");
        var regions = PaddleTextRegions.Find(map.ToArray(), shape[3], shape[2], crop.Width, crop.Height);
        var observations = new List<OcrTextObservation>();
        foreach (var region in regions)
        {
            using var line = crop.Clone(region, PixelFormat.Format24bppRgb);
            var resizedWidth = Math.Max(1, (int)Math.Ceiling(48.0 * line.Width / line.Height));
            var inputWidth = Math.Max(320, resizedWidth);
            // Ignore extreme non-line regions instead of distorting them into plausible text.
            if (inputWidth > 2048)
                continue;
            var resized = EvidenceImagePreprocessor.Resize(line, resizedWidth, 48, bgr: true);
            var input = new float[3 * 48 * inputWidth];
            for (var channel = 0; channel < 3; channel++)
                for (var y = 0; y < 48; y++)
                    Array.Copy(resized, channel * 48 * resizedWidth + y * resizedWidth, input, channel * 48 * inputWidth + y * inputWidth, resizedWidth);
            using var output = recognizer.Run([NamedOnnxValue.CreateFromTensor(recognizerInput, new DenseTensor<float>(input, [1, 3, 48, inputWidth]))]);
            var probabilities = output.Single().AsTensor<float>();
            var outputShape = probabilities.Dimensions.ToArray();
            if (outputShape.Length != 3 || outputShape[0] != 1 || outputShape[2] != alphabet.Length)
                throw new InvalidDataException("Paddle recognizer must output [1,time,dictionary classes].");
            observations.AddRange(EvidenceOutputDecoder.DecodeNameProbabilities(probabilities.ToArray(), outputShape[1], alphabet)
                .Select(text => new OcrTextObservation(text.Name, text.Confidence)));
        }
        return observations;
    }

    private static string ValidateInput(InferenceSession session)
    {
        var input = session.InputMetadata.Single();
        var shape = input.Value.Dimensions;
        if (input.Value.ElementType != typeof(float) || shape.Length != 4 || shape[1] != 3 || session.OutputMetadata.Count != 1 ||
            session.OutputMetadata.Values.Single().ElementType != typeof(float))
            throw new InvalidDataException("PaddleOCR models require one NCHW float input and one float output.");
        return input.Key;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        recognizer.Dispose();
        detector.Dispose();
    }
}

/// <summary>Conservative axis-aligned DB-map region extraction; rotated/perspective rectification is not implemented.</summary>
public static class PaddleTextRegions
{
    /// <summary>Extracts connected components using fixed DB defaults, expanding tight text bounds.</summary>
    public static IReadOnlyList<Rectangle> Find(float[] map, int width, int height, int originalWidth, int originalHeight)
    {
        if (width <= 0 || height <= 0 || map.Length != width * height)
            throw new InvalidDataException("Invalid text probability map.");
        var visited = new bool[map.Length];
        var queue = new Queue<int>();
        var regions = new List<Rectangle>();
        for (var start = 0; start < map.Length && regions.Count < 1000; start++)
        {
            if (visited[start] || !float.IsFinite(map[start]) || map[start] <= 0.3f)
                continue;
            visited[start] = true;
            queue.Enqueue(start);
            var left = width;
            var top = height;
            var right = 0;
            var bottom = 0;
            var sum = 0.0;
            var count = 0;
            while (queue.TryDequeue(out var index))
            {
                var x = index % width;
                var y = index / width;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
                sum += map[index];
                count++;
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = x + dx;
                        var ny = y + dy;
                        if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                            continue;
                        var next = ny * width + nx;
                        if (visited[next] || !float.IsFinite(map[next]) || map[next] <= 0.3f)
                            continue;
                        visited[next] = true;
                        queue.Enqueue(next);
                    }
            }
            if (right - left < 3 || bottom - top < 3 || sum / count < 0.5)
                continue;
            var distance = (right - left + 1) * (bottom - top + 1) * 1.6f / (2 * (right - left + bottom - top + 2));
            var x0 = Math.Clamp((int)Math.Floor((left - distance) * originalWidth / width), 0, originalWidth);
            var y0 = Math.Clamp((int)Math.Floor((top - distance) * originalHeight / height), 0, originalHeight);
            var x1 = Math.Clamp((int)Math.Ceiling((right + 1 + distance) * originalWidth / width), 0, originalWidth);
            var y1 = Math.Clamp((int)Math.Ceiling((bottom + 1 + distance) * originalHeight / height), 0, originalHeight);
            if (x1 > x0 && y1 > y0)
                regions.Add(Rectangle.FromLTRB(x0, y0, x1, y1));
        }
        return regions.OrderBy(region => region.Top).ThenBy(region => region.Left).ToArray();
    }
}

/// <summary>Reads driver-name text lines from the detected car for Core's entry-list validation.</summary>
public sealed class PaddleDriverNameReader(PaddleOcrTextReader reader) : IDriverNameReader
{
    /// <inheritdoc />
    public IReadOnlyList<DriverNameObservation> ReadNames(IPreview preview, DetectedCar detectedCar) =>
        reader.Read(preview, detectedCar).Where(text => text.Text.Any(char.IsLetter))
            .Select(text => new DriverNameObservation(text.Text, text.Confidence)).ToArray();
}

/// <summary>Primary OCR adapter: accepts complete numeric text lines, without consulting the entry list.</summary>
public sealed partial class PaddleNumberReader(PaddleOcrTextReader reader) : IPlateReader
{
    /// <inheritdoc />
    public IReadOnlyList<NumberHypothesis> ReadNumbers(IPreview preview, DetectedCar detectedCar) =>
        ExtractNumbers(reader.Read(preview, detectedCar));

    /// <summary>Deduplicates repeated text observations without adding their confidence mass.</summary>
    public static IReadOnlyList<NumberHypothesis> ExtractNumbers(IReadOnlyList<OcrTextObservation> texts) =>
        texts.Where(text => NumberLine().IsMatch(text.Text.Trim()))
            .Select(text => new NumberHypothesis(NumberNormalizer.Normalize(text.Text), text.Confidence))
            .GroupBy(text => text.Text, StringComparer.Ordinal)
            .Select(group => group.MaxBy(text => text.Probability)!)
            .OrderByDescending(text => text.Probability).ToArray();

    [GeneratedRegex(@"^#?[0-9]+$")]
    private static partial Regex NumberLine();
}
