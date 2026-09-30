namespace GridTag.Core;

/// <summary>Represents one OCR or vision hypothesis for a start number.</summary>
/// <param name="Text">The raw hypothesis text.</param>
/// <param name="Probability">The confidence score for this hypothesis.</param>
public sealed record NumberHypothesis(string Text, double Probability);

/// <summary>Defines evidence that can strengthen or weaken a listed candidate.</summary>
public interface IEvidence
{
    /// <summary>The human-readable evidence name used in review reasons.</summary>
    string Name { get; }

    /// <summary>Returns the evidence multiplier for a candidate number.</summary>
    double GetWeight(string candidateNumber, EntryList entryList);
}

/// <summary>Configuration values for the number matcher.</summary>
/// <param name="LowConfidence">Threshold for a sufficient confidence score.</param>
/// <param name="SmallMargin">Minimum gap between best and runner-up.</param>
/// <param name="ConfusableThreshold">Best confidence below which confusable neighbours trigger review.</param>
/// <param name="SubstringThreshold">Best confidence below which substring risk triggers review.</param>
/// <param name="EvidenceConflictThreshold">Evidence multiplier below which evidence conflict triggers review.</param>
public sealed record MatchOptions(
    double LowConfidence = 0.90,
    double SmallMargin = 0.30,
    double ConfusableThreshold = 0.97,
    double SubstringThreshold = 0.98,
    double EvidenceConflictThreshold = 0.25);

/// <summary>Describes the outcome of a number match decision.</summary>
public enum MatchStatus
{
    /// <summary>The best listed candidate is accepted without any review reason.</summary>
    Auto,
    /// <summary>The best candidate is ambiguous or otherwise flagged for manual review.</summary>
    Review,
    /// <summary>No listed reading was available or no entry matched the OCR hypothesis.</summary>
    NoMatch
}

/// <summary>The result for one photo's candidate-number matching step.</summary>
/// <param name="Status">The resulting match status.</param>
/// <param name="BestNumber">The winning entry-list number, if any.</param>
/// <param name="BestProbability">The winning probability after evidence adjustment.</param>
/// <param name="Reasons">Zero or more review reasons, if any.</param>
public sealed record MatchResult(MatchStatus Status, string? BestNumber, double BestProbability, IReadOnlyList<string> Reasons);

/// <summary>Validates OCR hypotheses against the entry list using confidence, evidence, and ambiguity checks.</summary>
public sealed class NumberMatcher
{
    /// <summary>Creates the matcher with the default AGENTS thresholds.</summary>
    public NumberMatcher(MatchOptions? options = null)
    {
        Options = options ?? new MatchOptions();
    }

    /// <summary>The configured thresholds used for decisioning.</summary>
    public MatchOptions Options { get; }

    /// <summary>Matches the hypotheses against the entry list and returns the best candidate plus review reasons.</summary>
    public MatchResult Match(EntryList entryList, IEnumerable<NumberHypothesis> hypotheses, IEvidence? evidence = null)
    {
        ArgumentNullException.ThrowIfNull(entryList);
        ArgumentNullException.ThrowIfNull(hypotheses);

        var hypothesisList = hypotheses.ToArray();
        if (hypothesisList.Length == 0)
            return new MatchResult(MatchStatus.NoMatch, null, 0, ["no_reading"]);

        var scores = AccumulateScores(entryList, hypothesisList);
        if (!scores.SawDigitHypothesis)
            return new MatchResult(MatchStatus.NoMatch, null, 0, ["no_reading"]);

        if (scores.CandidateScores.Count == 0)
            return new MatchResult(MatchStatus.NoMatch, null, 0, ["no_entry_match"]);

        var evidenceWeights = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var candidate in scores.CandidateScores.Keys)
        {
            var weight = evidence is null ? 1.0 : Math.Clamp(evidence.GetWeight(candidate, entryList), 0.0, 1.5);
            evidenceWeights[candidate] = weight;
        }

        var ranked = scores.CandidateScores
            .ToDictionary(pair => pair.Key, pair => pair.Value * evidenceWeights[pair.Key], StringComparer.Ordinal)
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .ToArray();

        var bestNumber = ranked[0].Key;
        var bestProbability = ranked[0].Value;
        var reasons = EvaluateReasons(entryList, scores, ranked, evidence, evidenceWeights, bestNumber, bestProbability);

        return new MatchResult(
            reasons.Count == 0 ? MatchStatus.Auto : MatchStatus.Review,
            bestNumber,
            bestProbability,
            reasons);
    }

    /// <summary>Sums hypothesis probability per listed number and tracks the non-listed mass.</summary>
    private static HypothesisScores AccumulateScores(EntryList entryList, IReadOnlyList<NumberHypothesis> hypotheses)
    {
        var candidates = new Dictionary<string, double>(StringComparer.Ordinal);
        var outOfListMass = 0.0;
        var sawDigitHypothesis = false;

        foreach (var hypothesis in hypotheses)
        {
            if (string.IsNullOrWhiteSpace(hypothesis.Text) || !hypothesis.Text.Any(char.IsDigit))
            {
                outOfListMass += hypothesis.Probability;
                continue;
            }

            sawDigitHypothesis = true;
            try
            {
                var normalized = NumberNormalizer.Normalize(hypothesis.Text);
                if (entryList.TryGetEntry(normalized, out var entry))
                {
                    candidates[entry.Number] = candidates.GetValueOrDefault(entry.Number) + hypothesis.Probability;
                }
                else
                {
                    outOfListMass += hypothesis.Probability;
                }
            }
            catch (InvalidDataException)
            {
                outOfListMass += hypothesis.Probability;
            }
        }

        return new HypothesisScores(candidates, outOfListMass, sawDigitHypothesis);
    }

    /// <summary>Collects every review reason that applies to the best candidate.</summary>
    private List<string> EvaluateReasons(
        EntryList entryList,
        HypothesisScores scores,
        IReadOnlyList<KeyValuePair<string, double>> ranked,
        IEvidence? evidence,
        IReadOnlyDictionary<string, double> evidenceWeights,
        string bestNumber,
        double bestProbability)
    {
        var analysis = new EntryListAnalysis(entryList, new ConfusionMap());
        var runnerUpProbability = ranked.Count > 1 ? ranked[1].Value : 0.0;
        var reasons = new List<string>();

        if (bestProbability < Options.LowConfidence)
            reasons.Add("low_confidence");

        if (bestProbability - runnerUpProbability < Options.SmallMargin)
            reasons.Add("small_margin");

        if (scores.OutOfListMass > scores.CandidateScores[bestNumber])
            reasons.Add("out_of_list_mass");

        if (analysis.Confusables.TryGetValue(bestNumber, out var confusables) && confusables.Count > 0 && bestProbability < Options.ConfusableThreshold)
            reasons.Add($"confusable:{string.Join(",", confusables)}");

        if (analysis.SubstringHosts.TryGetValue(bestNumber, out var substringHosts) && substringHosts.Count > 0 && bestProbability < Options.SubstringThreshold)
            reasons.Add($"substring_risk:{string.Join(",", substringHosts)}");

        if (evidence is not null && evidenceWeights.TryGetValue(bestNumber, out var evidenceWeight) && evidenceWeight < Options.EvidenceConflictThreshold)
            reasons.Add($"evidence_conflict:{evidence.Name}");

        return reasons;
    }
}

/// <summary>Per-number probability sums plus the mass that matched no listed number.</summary>
/// <param name="CandidateScores">Probability mass summed per listed number.</param>
/// <param name="OutOfListMass">Probability mass of hypotheses outside the entry list.</param>
/// <param name="SawDigitHypothesis">Whether any hypothesis contained a digit.</param>
internal sealed record HypothesisScores(IReadOnlyDictionary<string, double> CandidateScores, double OutOfListMass, bool SawDigitHypothesis);
