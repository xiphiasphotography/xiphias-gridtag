using System.Drawing;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using XiPHiAS.GridTag.Core;

namespace XiPHiAS.GridTag.Vision;

/// <summary>Decodes classifier logits and CTC text without consulting an entry list.</summary>
public static class EvidenceOutputDecoder
{
    /// <summary>Returns the most probable model label from finite logits.</summary>
    public static CarModelObservation? DecodeModel(IReadOnlyList<float> logits, IReadOnlyList<string> labels)
    {
        if (logits.Count != labels.Count || labels.Count == 0)
            throw new InvalidDataException("Model output must have one logit per car label.");
        var probabilities = Probabilities(logits);
        if (probabilities is null)
            return null;
        var best = Enumerable.Range(0, labels.Count).MaxBy(index => probabilities[index]);
        return string.IsNullOrWhiteSpace(labels[best]) ? null : new CarModelObservation(labels[best], probabilities[best]);
    }

    /// <summary>Greedy CTC decoding; symbol zero is blank, repeated symbols collapse unless separated by blank.</summary>
    public static IReadOnlyList<DriverNameObservation> DecodeNames(float[] logits, int steps, IReadOnlyList<string> alphabet)
    {
        if (steps <= 0 || alphabet.Count < 2 || logits.Length != steps * alphabet.Count)
            throw new InvalidDataException("Name output must be time steps by alphabet classes (blank first).");
        var text = new System.Text.StringBuilder();
        var previous = -1;
        var logConfidence = 0.0;
        var emitted = 0;
        for (var step = 0; step < steps; step++)
        {
            var probabilities = Probabilities(logits.AsSpan(step * alphabet.Count, alphabet.Count).ToArray());
            if (probabilities is null)
                return [];
            var best = Enumerable.Range(0, alphabet.Count).MaxBy(index => probabilities[index]);
            if (best != 0 && best != previous)
            {
                text.Append(alphabet[best]);
                logConfidence += Math.Log(probabilities[best]);
                emitted++;
            }
            previous = best;
        }
        var name = text.ToString().Trim();
        return name.Length == 0 ? [] : [new DriverNameObservation(name, Math.Exp(logConfidence / emitted))];
    }

    /// <summary>Decodes PaddleOCR probabilities without applying softmax a second time.</summary>
    public static IReadOnlyList<DriverNameObservation> DecodeNameProbabilities(float[] probabilities, int steps, IReadOnlyList<string> alphabet)
    {
        if (steps <= 0 || probabilities.Length != steps * alphabet.Count || alphabet.Count < 2)
            throw new InvalidDataException("CTC output must be time steps by alphabet classes.");
        var text = new System.Text.StringBuilder();
        var previous = -1;
        var confidence = 0.0;
        var count = 0;
        for (var step = 0; step < steps; step++)
        {
            var offset = step * alphabet.Count;
            var best = 0;
            for (var symbol = 0; symbol < alphabet.Count; symbol++)
            {
                var value = probabilities[offset + symbol];
                if (!float.IsFinite(value) || value is < 0 or > 1)
                    return [];
                if (value > probabilities[offset + best])
                    best = symbol;
            }
            if (best != 0 && best != previous)
            {
                text.Append(alphabet[best]);
                confidence += probabilities[offset + best];
                count++;
            }
            previous = best;
        }
        return count == 0 || string.IsNullOrWhiteSpace(text.ToString()) ? [] :
            [new DriverNameObservation(text.ToString().Trim(), confidence / count)];
    }

    private static double[]? Probabilities(IReadOnlyList<float> logits)
    {
        if (logits.Any(value => !float.IsFinite(value)))
            return null;
        var max = logits.Max();
        var values = logits.Select(value => Math.Exp(value - max)).ToArray();
        var sum = values.Sum();
        return values.Select(value => value / sum).ToArray();
    }
}

/// <summary>Local ONNX classifier of a detected car crop; expects RGB NCHW input in [0,1] and class logits.</summary>
public sealed class OnnxCarModelClassifier : ICarModelClassifier, IDisposable
{
    private readonly EvidenceCropSession model;
    private readonly string[] labels;

    /// <summary>Loads a classifier and its ordered, one-label-per-line vocabulary.</summary>
    public OnnxCarModelClassifier(string modelPath, string labelsPath, string format = "generic")
    {
        labels = File.ReadAllLines(labelsPath);
        if (labels.Length == 0 || labels.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Car-model labels must be nonempty, one per line.");
        if (format is not ("generic" or "stanford-imagenet"))
            throw new InvalidDataException("Car classifier format must be generic or stanford-imagenet.");
        this.format = format;
        model = new EvidenceCropSession(modelPath, format == "stanford-imagenet");
    }

    /// <inheritdoc />
    public CarModelObservation? Classify(IPreview preview, DetectedCar detectedCar)
    {
        var output = model.Run(preview, detectedCar);
        if (output is null)
            return null;
        if (output.Value.Shape.Length != 2 || output.Value.Shape[0] != 1)
            throw new InvalidDataException("Car classifier output must have shape [1,classes].");
        // Road-car model/year classes are not GT3 classes: use manufacturer evidence only.
        var vocabulary = format == "stanford-imagenet" ? labels.Select(StanfordCarLabels.Manufacturer).ToArray() : labels;
        return EvidenceOutputDecoder.DecodeModel(output.Value.Values, vocabulary);
    }

    /// <inheritdoc />
    public void Dispose() => model.Dispose();

    private readonly string format;
}

/// <summary>Local ONNX CTC driver-name reader on a car crop; expects [1,time,classes] logits.</summary>
public sealed class OnnxDriverNameReader : IDriverNameReader, IDisposable
{
    private readonly EvidenceCropSession model;
    private readonly string[] alphabet;

    /// <summary>Loads a name reader with one symbol per line, including a blank placeholder on the first line.</summary>
    public OnnxDriverNameReader(string modelPath, string alphabetPath)
    {
        alphabet = File.ReadAllLines(alphabetPath);
        if (alphabet.Length < 2 || alphabet.Skip(1).Any(string.IsNullOrEmpty))
            throw new InvalidDataException("Name alphabet must include blank first and nonempty symbols after it.");
        model = new EvidenceCropSession(modelPath);
    }

    /// <inheritdoc />
    public IReadOnlyList<DriverNameObservation> ReadNames(IPreview preview, DetectedCar detectedCar)
    {
        var output = model.Run(preview, detectedCar);
        if (output is null)
            return [];
        var shape = output.Value.Shape;
        if (shape.Length != 3 || shape[0] != 1 || shape[2] != alphabet.Length)
            throw new InvalidDataException("Name reader output must have shape [1,time,alphabet classes].");
        return EvidenceOutputDecoder.DecodeNames(output.Value.Values, shape[1], alphabet);
    }

    /// <inheritdoc />
    public void Dispose() => model.Dispose();
}

internal sealed class EvidenceCropSession : IDisposable
{
    private readonly InferenceSession session;
    private readonly string inputName;
    private readonly int width;
    private readonly int height;
    private readonly bool imageNet;

    public EvidenceCropSession(string modelPath, bool imageNet = false)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("Evidence model was not found.", modelPath);
        this.imageNet = imageNet;
        session = new InferenceSession(modelPath);
        try
        {
            if (session.InputMetadata.Count != 1 || session.OutputMetadata.Count != 1)
                throw new InvalidDataException("Evidence models must have one input and one output.");
            inputName = session.InputMetadata.Keys.Single();
            var metadata = session.InputMetadata[inputName];
            var shape = metadata.Dimensions;
            if (metadata.ElementType != typeof(float) || shape.Length != 4 || (shape[0] != 1 && shape[0] != -1) || shape[1] != 3 || shape[2] <= 0 || shape[3] <= 0)
                throw new InvalidDataException("Evidence input must be float RGB [1,3,height,width] with fixed positive dimensions.");
            height = shape[2];
            width = shape[3];
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public (float[] Values, int[] Shape)? Run(IPreview preview, DetectedCar car)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(car);
        if (preview is not RawPreview raw || car.Bounds is not { } bounds)
            return null;
        using var stream = new MemoryStream(raw.JpegBytes, writable: false);
        using var image = Image.FromStream(stream);
        using var bitmap = new Bitmap(image);
        var left = Math.Clamp((int)MathF.Floor(bounds.Left), 0, bitmap.Width);
        var top = Math.Clamp((int)MathF.Floor(bounds.Top), 0, bitmap.Height);
        var right = Math.Clamp((int)MathF.Ceiling(bounds.Right), 0, bitmap.Width);
        var bottom = Math.Clamp((int)MathF.Ceiling(bounds.Bottom), 0, bitmap.Height);
        if (right <= left || bottom <= top)
            return null;
        if (imageNet)
        {
            using var crop = bitmap.Clone(new Rectangle(left, top, right - left, bottom - top), System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            var input = EvidenceImagePreprocessor.Resize(crop, width, height, bgr: false, imageNet: true);
            return RunTensor(input, height, width);
        }
        var area = width * height;
        var values = new float[3 * area];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var pixel = bitmap.GetPixel(left + x * (right - left) / width, top + y * (bottom - top) / height);
                var offset = y * width + x;
                values[offset] = pixel.R / 255f;
                values[area + offset] = pixel.G / 255f;
                values[2 * area + offset] = pixel.B / 255f;
            }
        return RunTensor(values, height, width);
    }

    private (float[] Values, int[] Shape) RunTensor(float[] values, int height, int width)
    {
        var tensor = new DenseTensor<float>(values, [1, 3, height, width]);
        using var output = session.Run([NamedOnnxValue.CreateFromTensor(inputName, tensor)]);
        var result = output.Single().AsTensor<float>();
        return (result.ToArray(), result.Dimensions.ToArray());
    }

    public void Dispose() => session.Dispose();
}

/// <summary>Extracts the manufacturer from the ordered Stanford Cars vocabulary.</summary>
public static class StanfordCarLabels
{
    /// <summary>Retains multiword manufacturers without inventing racing model names.</summary>
    public static string Manufacturer(string label)
    {
        if (label.StartsWith("Mercedes-Benz ", StringComparison.Ordinal))
            return "Mercedes";
        foreach (var make in new[] { "AM General", "Aston Martin", "Land Rover" })
            if (label.StartsWith(make + " ", StringComparison.Ordinal))
                return make;
        return label.Split(' ', 2)[0];
    }
}
