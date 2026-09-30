using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using GridTag.Core;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace GridTag.Vision;

/// <summary>Configuration for an ONNX car detector.</summary>
public sealed record CarDetectorConfig(
    string ModelPath,
    int InputSize = 640,
    float ConfidenceThreshold = 0.25f,
    float NmsThreshold = 0.45f,
    int CarClassId = 2,
    int DeviceId = 0)
{
    /// <summary>Loads and validates detector configuration from JSON.</summary>
    public static CarDetectorConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Detector config '{path}' was not found.", path);
        var config = JsonSerializer.Deserialize<CarDetectorConfig>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })
            ?? throw new InvalidDataException("Detector config is empty.");
        if (string.IsNullOrWhiteSpace(config.ModelPath) || config.InputSize <= 0 || config.InputSize % 32 != 0 ||
            config.ConfidenceThreshold is < 0 or > 1 || config.NmsThreshold is < 0 or > 1 || config.CarClassId < 0)
            throw new InvalidDataException("Detector config contains invalid model path, size, threshold, or class values.");
        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory();
        return Path.IsPathRooted(config.ModelPath)
            ? config
            : config with { ModelPath = Path.GetFullPath(config.ModelPath, baseDirectory) };
    }
}

/// <summary>One RGB pixel used by the pure letterbox preprocessor.</summary>
public readonly record struct RgbPixel(byte Red, byte Green, byte Blue);

/// <summary>Result of letterboxing an RGB image into a square tensor input.</summary>
public sealed record LetterboxResult(float[] Tensor, float Scale, float PadX, float PadY, int InputSize);

/// <summary>Pure letterbox preprocessing for an RGB image.</summary>
public static class LetterboxPreprocessor
{
    /// <summary>Creates a normalized CHW tensor with gray padding and preserved aspect ratio.</summary>
    public static LetterboxResult Apply(ReadOnlySpan<RgbPixel> pixels, int width, int height, int inputSize)
    {
        if (width <= 0 || height <= 0 || pixels.Length != width * height || inputSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        var scale = Math.Min(inputSize / (float)width, inputSize / (float)height);
        var resizedWidth = Math.Max(1, (int)Math.Round(width * scale));
        var resizedHeight = Math.Max(1, (int)Math.Round(height * scale));
        var padX = (inputSize - resizedWidth) / 2f;
        var padY = (inputSize - resizedHeight) / 2f;
        var tensor = new float[3 * inputSize * inputSize];
        Array.Fill(tensor, 114f / 255f);
        Array.Fill(tensor, 114f / 255f, inputSize * inputSize, 2 * inputSize * inputSize);
        Array.Fill(tensor, 114f / 255f, 2 * inputSize * inputSize, inputSize * inputSize);

        for (var y = 0; y < resizedHeight; y++)
        {
            var sourceY = Math.Min(height - 1, (int)(y / scale));
            var targetY = (int)padY + y;
            for (var x = 0; x < resizedWidth; x++)
            {
                var sourceX = Math.Min(width - 1, (int)(x / scale));
                var pixel = pixels[sourceY * width + sourceX];
                var targetX = (int)padX + x;
                var offset = targetY * inputSize + targetX;
                tensor[offset] = pixel.Red / 255f;
                tensor[inputSize * inputSize + offset] = pixel.Green / 255f;
                tensor[2 * inputSize * inputSize + offset] = pixel.Blue / 255f;
            }
        }

        return new LetterboxResult(tensor, scale, padX, padY, inputSize);
    }
}

/// <summary>One decoded YOLO-style detection before conversion to Core's car contract.</summary>
public sealed record YoloDetection(int ClassId, float Score, DetectionBounds Bounds);

/// <summary>Pure YOLO-style output decoding and non-maximum suppression.</summary>
public static class YoloPostprocessor
{
    /// <summary>Decodes rows of [cx, cy, width, height, objectness, class scores...].</summary>
    public static IReadOnlyList<YoloDetection> Process(
        ReadOnlySpan<float> output,
        int rowCount,
        int columnCount,
        int classId,
        float confidenceThreshold,
        float nmsThreshold,
        LetterboxResult letterbox,
        int originalWidth,
        int originalHeight)
    {
        if (rowCount < 0 || columnCount < 6 || classId < 0 || 5 + classId >= columnCount || output.Length < rowCount * columnCount)
            throw new ArgumentOutOfRangeException(nameof(output));

        var candidates = new List<YoloDetection>();
        for (var row = 0; row < rowCount; row++)
        {
            var offset = row * columnCount;
            var score = output[offset + 4] * output[offset + 5 + classId];
            if (score < confidenceThreshold)
                continue;

            var centerX = output[offset];
            var centerY = output[offset + 1];
            var width = output[offset + 2];
            var height = output[offset + 3];
            var left = Math.Clamp((centerX - width / 2 - letterbox.PadX) / letterbox.Scale, 0, originalWidth);
            var top = Math.Clamp((centerY - height / 2 - letterbox.PadY) / letterbox.Scale, 0, originalHeight);
            var right = Math.Clamp((centerX + width / 2 - letterbox.PadX) / letterbox.Scale, 0, originalWidth);
            var bottom = Math.Clamp((centerY + height / 2 - letterbox.PadY) / letterbox.Scale, 0, originalHeight);
            if (right > left && bottom > top)
                candidates.Add(new YoloDetection(classId, score, new DetectionBounds(left, top, right, bottom)));
        }

        var kept = new List<YoloDetection>();
        foreach (var candidate in candidates.OrderByDescending(item => item.Score))
        {
            if (kept.All(existing => IoU(existing.Bounds, candidate.Bounds) <= nmsThreshold))
                kept.Add(candidate);
        }
        return kept;
    }

    private static float IoU(DetectionBounds first, DetectionBounds second)
    {
        var left = Math.Max(first.Left, second.Left);
        var top = Math.Max(first.Top, second.Top);
        var right = Math.Min(first.Right, second.Right);
        var bottom = Math.Min(first.Bottom, second.Bottom);
        var intersection = Math.Max(0, right - left) * Math.Max(0, bottom - top);
        var firstArea = Math.Max(0, first.Right - first.Left) * Math.Max(0, first.Bottom - first.Top);
        var secondArea = Math.Max(0, second.Right - second.Left) * Math.Max(0, second.Bottom - second.Top);
        return intersection / Math.Max(0.0001f, firstArea + secondArea - intersection);
    }
}

/// <summary>ONNX Runtime car detector using DirectML.</summary>
public sealed class OnnxCarDetector : ICarDetector, IDisposable
{
    private readonly CarDetectorConfig config;
    private readonly InferenceSession session;
    private readonly string inputName;

    /// <summary>Loads the model and pins all inputs to the requested runtime configuration.</summary>
    public OnnxCarDetector(CarDetectorConfig config)
    {
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        var sessionOptions = new SessionOptions();
        sessionOptions.AppendExecutionProvider_DML(config.DeviceId);
        session = new InferenceSession(config.ModelPath, sessionOptions);
        inputName = session.InputMetadata.Keys.Single();
    }

    /// <summary>Runs detection and returns car detections; failed frames return no detections.</summary>
    public IReadOnlyList<DetectedCar> Detect(IPreview preview)
    {
        try
        {
            if (preview is not RawPreview rawPreview)
                return Array.Empty<DetectedCar>();

            using var stream = new MemoryStream(rawPreview.JpegBytes, writable: false);
            using var image = Image.FromStream(stream);
            using var bitmap = new Bitmap(image);
            var pixels = ReadPixels(bitmap);
            var letterbox = LetterboxPreprocessor.Apply(pixels, bitmap.Width, bitmap.Height, config.InputSize);
            var tensor = new DenseTensor<float>(letterbox.Tensor, [1, 3, config.InputSize, config.InputSize]);
            using var results = session.Run([NamedOnnxValue.CreateFromTensor(inputName, tensor)]);
            var output = results.First().AsTensor<float>();
            var dimensions = output.Dimensions.ToArray();
            var columnCount = dimensions[^1];
            var rowCount = dimensions[^2];
            var detections = YoloPostprocessor.Process(output.ToArray(), rowCount, columnCount, config.CarClassId,
                config.ConfidenceThreshold, config.NmsThreshold, letterbox, bitmap.Width, bitmap.Height);
            return detections.Select((detection, index) => new DetectedCar(
                $"car-{index}", detection.Score, detection.Bounds)).ToArray();
        }
        catch
        {
            return Array.Empty<DetectedCar>();
        }
    }

    /// <summary>Releases the ONNX Runtime session.</summary>
    public void Dispose() => session.Dispose();

    private static RgbPixel[] ReadPixels(Bitmap bitmap)
    {
        var pixels = new RgbPixel[bitmap.Width * bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                pixels[y * bitmap.Width + x] = new RgbPixel(color.R, color.G, color.B);
            }
        return pixels;
    }
}
