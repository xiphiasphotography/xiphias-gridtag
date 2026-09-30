using System.Drawing;
using System.Text.Json;
using GridTag.Core;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace GridTag.Vision;

/// <summary>Integer crop rectangle in original preview pixels.</summary>
public sealed record CropRectangle(int Left, int Top, int Width, int Height);

/// <summary>Configuration for selecting a number region inside a car detection.</summary>
public sealed record PlateCropOptions(
    float HorizontalInset = 0.10f,
    float TopInset = 0.15f,
    float BottomFraction = 0.65f)
{
    /// <summary>Validates crop fractions.</summary>
    public void Validate()
    {
        if (HorizontalInset is < 0 or >= 0.5f || TopInset is < 0 or >= 1 || BottomFraction <= TopInset || BottomFraction > 1)
            throw new InvalidDataException("Invalid plate crop fractions.");
    }
}

/// <summary>Pure crop geometry for a probable number region inside a car box.</summary>
public static class PlateCropGeometry
{
    /// <summary>Calculates a clamped upper-middle crop without reading image pixels.</summary>
    public static CropRectangle? Calculate(DetectionBounds car, int imageWidth, int imageHeight, PlateCropOptions? options = null)
    {
        options ??= new PlateCropOptions();
        options.Validate();
        if (imageWidth <= 0 || imageHeight <= 0 || car.Right <= car.Left || car.Bottom <= car.Top)
            return null;

        var carWidth = car.Right - car.Left;
        var carHeight = car.Bottom - car.Top;
        var left = Math.Clamp((int)MathF.Round(car.Left + carWidth * options.HorizontalInset), 0, imageWidth);
        var right = Math.Clamp((int)MathF.Round(car.Right - carWidth * options.HorizontalInset), 0, imageWidth);
        var top = Math.Clamp((int)MathF.Round(car.Top + carHeight * options.TopInset), 0, imageHeight);
        var bottom = Math.Clamp((int)MathF.Round(car.Top + carHeight * options.BottomFraction), 0, imageHeight);
        return right <= left || bottom <= top ? null : new CropRectangle(left, top, right - left, bottom - top);
    }
}

/// <summary>RGB pixels for one cropped number region.</summary>
public sealed record PlateCrop(CropRectangle Bounds, int Width, int Height, RgbPixel[] Pixels);

/// <summary>One digit or blank candidate emitted by a digit recognizer.</summary>
public sealed record DigitCandidate(char Symbol, double Probability);

/// <summary>Digit recognizer abstraction; it does not know the event entry list.</summary>
public interface IDigitRecognizer
{
    /// <summary>Returns candidate digits per character position.</summary>
    IReadOnlyList<IReadOnlyList<DigitCandidate>> Recognize(PlateCrop crop);
}

/// <summary>Pure beam decoder producing an n-best list from per-position digit candidates.</summary>
public static class DigitNBestDecoder
{
    /// <summary>Decodes digit candidates without normalization or entry-list filtering.</summary>
    public static IReadOnlyList<NumberHypothesis> Decode(
        IReadOnlyList<IReadOnlyList<DigitCandidate>> positions,
        int beamWidth = 10,
        int nBest = 10)
    {
        if (beamWidth <= 0 || nBest <= 0)
            throw new ArgumentOutOfRangeException(nameof(beamWidth));

        var beams = new List<(string Text, double Probability)> { (string.Empty, 1.0) };
        foreach (var position in positions)
        {
            beams = beams
                .SelectMany(beam => position.Select(candidate =>
                    (Text: candidate.Symbol == '_' ? beam.Text : beam.Text + candidate.Symbol,
                     Probability: beam.Probability * Math.Clamp(candidate.Probability, 0, 1))))
                .Where(beam => beam.Probability > 0)
                .OrderByDescending(beam => beam.Probability)
                .Take(beamWidth)
                .ToList();
        }

        return beams
            .Where(beam => beam.Text.Length > 0)
            .GroupBy(beam => beam.Text, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.Probability).First())
            .OrderByDescending(beam => beam.Probability)
            .Take(nBest)
            .Select(beam => new NumberHypothesis(beam.Text, beam.Probability))
            .ToArray();
    }
}

/// <summary>Configuration for the optional ONNX digit recognizer.</summary>
public sealed record PlateReaderConfig(
    string ModelPath,
    int InputWidth = 160,
    int InputHeight = 64,
    int BeamWidth = 10,
    int NBest = 10,
    int BlankClassId = 10,
    int DeviceId = 0,
    PlateCropOptions? Crop = null)
{
    /// <summary>Loads camelCase JSON configuration and resolves a relative model path.</summary>
    public static PlateReaderConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Plate reader config '{path}' was not found.", path);
        var config = JsonSerializer.Deserialize<PlateReaderConfig>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidDataException("Plate reader config is empty.");
        config.Crop?.Validate();
        if (string.IsNullOrWhiteSpace(config.ModelPath) || config.InputWidth <= 0 || config.InputHeight <= 0 || config.BeamWidth <= 0 || config.NBest <= 0)
            throw new InvalidDataException("Plate reader config contains invalid values.");
        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory();
        return Path.IsPathRooted(config.ModelPath) ? config : config with { ModelPath = Path.GetFullPath(config.ModelPath, baseDirectory) };
    }
}

/// <summary>ONNX digit recognizer using DirectML; output is positions x digit classes.</summary>
public sealed class OnnxDigitRecognizer : IDigitRecognizer, IDisposable
{
    private readonly PlateReaderConfig config;
    private readonly InferenceSession session;
    private readonly string inputName;

    /// <summary>Loads the configured digit model.</summary>
    public OnnxDigitRecognizer(PlateReaderConfig config)
    {
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        var options = new SessionOptions();
        options.AppendExecutionProvider_DML(config.DeviceId);
        session = new InferenceSession(config.ModelPath, options);
        inputName = session.InputMetadata.Keys.Single();
    }

    /// <summary>Runs the model and converts each position's logits to digit probabilities.</summary>
    public IReadOnlyList<IReadOnlyList<DigitCandidate>> Recognize(PlateCrop crop)
    {
        var input = new float[3 * config.InputWidth * config.InputHeight];
        for (var y = 0; y < config.InputHeight; y++)
            for (var x = 0; x < config.InputWidth; x++)
            {
                var sourceX = Math.Min(crop.Width - 1, x * crop.Width / config.InputWidth);
                var sourceY = Math.Min(crop.Height - 1, y * crop.Height / config.InputHeight);
                var pixel = crop.Pixels[sourceY * crop.Width + sourceX];
                var offset = y * config.InputWidth + x;
                input[offset] = pixel.Red / 255f;
                input[config.InputWidth * config.InputHeight + offset] = pixel.Green / 255f;
                input[2 * config.InputWidth * config.InputHeight + offset] = pixel.Blue / 255f;
            }

        var tensor = new DenseTensor<float>(input, [1, 3, config.InputHeight, config.InputWidth]);
        using var results = session.Run([NamedOnnxValue.CreateFromTensor(inputName, tensor)]);
        var output = results.First().AsTensor<float>();
        var dimensions = output.Dimensions.ToArray();
        var positionCount = dimensions[^2];
        var classCount = dimensions[^1];
        var values = output.ToArray();
        var positions = new List<IReadOnlyList<DigitCandidate>>();
        for (var position = 0; position < positionCount; position++)
        {
            var logits = values.AsSpan(position * classCount, classCount);
            var max = logits.ToArray().Max();
            var exponentials = logits.ToArray().Select(value => MathF.Exp(value - max)).ToArray();
            var total = exponentials.Sum();
            positions.Add(exponentials.Select((value, classId) => new DigitCandidate(
                classId == config.BlankClassId ? '_' : (char)('0' + classId), value / total)).ToArray());
        }
        return positions;
    }

    /// <summary>Releases the ONNX Runtime session.</summary>
    public void Dispose() => session.Dispose();
}

/// <summary>Reads number-region crops and returns decoder n-best hypotheses.</summary>
public sealed class OnnxPlateReader : IPlateReader
{
    private readonly IDigitRecognizer recognizer;
    private readonly PlateCropOptions cropOptions;
    private readonly int beamWidth;
    private readonly int nBest;

    /// <summary>Creates a plate reader around a digit recognizer and pure decoder.</summary>
    public OnnxPlateReader(IDigitRecognizer recognizer, PlateReaderConfig config)
    {
        this.recognizer = recognizer ?? throw new ArgumentNullException(nameof(recognizer));
        config.Crop?.Validate();
        cropOptions = config.Crop ?? new PlateCropOptions();
        beamWidth = config.BeamWidth;
        nBest = config.NBest;
    }

    /// <summary>Returns raw n-best hypotheses without consulting an entry list.</summary>
    public IReadOnlyList<NumberHypothesis> ReadNumbers(IPreview preview, DetectedCar detectedCar)
    {
        try
        {
            if (preview is not RawPreview rawPreview || detectedCar.Bounds is null)
                return Array.Empty<NumberHypothesis>();
            using var stream = new MemoryStream(rawPreview.JpegBytes, writable: false);
            using var image = Image.FromStream(stream);
            using var bitmap = new Bitmap(image);
            var bounds = PlateCropGeometry.Calculate(detectedCar.Bounds, bitmap.Width, bitmap.Height, cropOptions);
            if (bounds is null)
                return Array.Empty<NumberHypothesis>();
            var crop = CreateCrop(bitmap, bounds);
            return DigitNBestDecoder.Decode(recognizer.Recognize(crop), beamWidth, nBest);
        }
        catch
        {
            return Array.Empty<NumberHypothesis>();
        }
    }

    private static PlateCrop CreateCrop(Bitmap bitmap, CropRectangle bounds)
    {
        var pixels = new RgbPixel[bounds.Width * bounds.Height];
        for (var y = 0; y < bounds.Height; y++)
            for (var x = 0; x < bounds.Width; x++)
            {
                var color = bitmap.GetPixel(bounds.Left + x, bounds.Top + y);
                pixels[y * bounds.Width + x] = new RgbPixel(color.R, color.G, color.B);
            }
        return new PlateCrop(bounds, bounds.Width, bounds.Height, pixels);
    }
}
