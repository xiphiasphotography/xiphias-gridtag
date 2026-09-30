using System.Diagnostics;
using GridTag.Core;

namespace GridTag.Cli;

/// <summary>One ground-truth label used by the evaluation harness.</summary>
public sealed record EvaluationLabel(string Path, IReadOnlyList<string> Numbers);

/// <summary>Aggregated metrics from one evaluation run.</summary>
public sealed record EvaluationReport(
    int TotalPhotos,
    int LabeledCarPhotos,
    int AutoPhotos,
    int CorrectAutoPhotos,
    int ReviewPhotos,
    int WrongAutoPhotos,
    double AutoPrecision,
    double Recall,
    double ReviewRate,
    double SecondsPerPhoto,
    IReadOnlyDictionary<string, int> ReasonCounts,
    IReadOnlyDictionary<string, int> Confusions)
{
    /// <summary>Whether the measured run meets every AGENTS.md go/no-go criterion.</summary>
    public bool MeetsGoNoGo => Recall >= 0.80 && WrongAutoPhotos / (double)Math.Max(1, TotalPhotos) <= 0.02 && SecondsPerPhoto <= 2.0;
}

/// <summary>Runs deterministic evaluations against a supplied per-photo processor.</summary>
public sealed class EvaluationRunner
{
    /// <summary>Evaluates labels without changing matcher thresholds or vision configuration.</summary>
    public EvaluationReport Evaluate(IReadOnlyList<EvaluationLabel> labels, Func<EvaluationLabel, PhotoResult> processor)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(processor);
        var stopwatch = Stopwatch.StartNew();
        var reasonCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var confusions = new Dictionary<string, int>(StringComparer.Ordinal);
        var autoPhotos = 0;
        var correctAutoPhotos = 0;
        var wrongAutoPhotos = 0;
        var reviewPhotos = 0;
        var labeledCarPhotos = labels.Count(label => label.Numbers.Count > 0);

        foreach (var label in labels)
        {
            var result = processor(label);
            foreach (var reason in result.Reasons)
                reasonCounts[reason] = reasonCounts.GetValueOrDefault(reason) + 1;

            if (result.Status == "review")
                reviewPhotos++;
            if (result.Status != "auto")
                continue;

            autoPhotos++;
            var predicted = result.Cars?
                .OrderByDescending(car => car.Primary)
                .Select(car => NumberNormalizer.Normalize(car.Number))
                .ToArray() ?? [];
            var expected = label.Numbers.Select(NumberNormalizer.Normalize).ToArray();
            var correct = predicted.SequenceEqual(expected, StringComparer.Ordinal);
            if (correct)
                correctAutoPhotos++;
            else
            {
                wrongAutoPhotos++;
                var expectedText = string.Join(",", expected);
                var predictedText = string.Join(",", predicted);
                var key = $"{expectedText}->{predictedText}";
                confusions[key] = confusions.GetValueOrDefault(key) + 1;
            }
        }

        stopwatch.Stop();
        var total = Math.Max(1, labels.Count);
        return new EvaluationReport(
            labels.Count,
            labeledCarPhotos,
            autoPhotos,
            correctAutoPhotos,
            reviewPhotos,
            wrongAutoPhotos,
            autoPhotos == 0 ? 0 : correctAutoPhotos / (double)autoPhotos,
            labeledCarPhotos == 0 ? 0 : correctAutoPhotos / (double)labeledCarPhotos,
            reviewPhotos / (double)total,
            stopwatch.Elapsed.TotalSeconds / total,
            reasonCounts,
            confusions);
    }
}

internal static class EvaluationInput
{
    public static IReadOnlyList<EvaluationLabel> ReadLabels(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Labels file '{path}' was not found.", path);

        using var reader = new StreamReader(path);
        var labels = new List<EvaluationLabel>();
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var separator = line.IndexOf(';');
            if (separator <= 0)
                throw new InvalidDataException($"Labels row {lineNumber} must use path;numbers.");
            var pathValue = line[..separator].Trim();
            var numbers = line[(separator + 1)..]
                .Split([','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NumberNormalizer.Normalize)
                .ToArray();
            if (lineNumber == 1 && string.Equals(pathValue, "path", StringComparison.OrdinalIgnoreCase))
                continue;
            labels.Add(new EvaluationLabel(pathValue, numbers));
        }

        return labels;
    }
}
