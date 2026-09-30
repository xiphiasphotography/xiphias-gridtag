using Xunit;

namespace GridTag.Core.Tests;

public sealed class NumberMatcherTests
{
    [Fact]
    public void StrongSingleHypothesisMatchesAsAuto()
    {
        var entryList = CreateEntryList("59", "66", "69", "96", "99");

        var result = new NumberMatcher().Match(entryList, [new NumberHypothesis("69", 0.98)]);

        Assert.Equal(MatchStatus.Auto, result.Status);
        Assert.Empty(result.Reasons);
        Assert.Equal("69", result.BestNumber);
        Assert.Equal(0.98, result.BestProbability, 5);
    }

    [Fact]
    public void ConfusableClusterIsReview()
    {
        var entryList = CreateEntryList("59", "66", "69", "96", "99");

        var result = new NumberMatcher().Match(entryList,
        [
            new NumberHypothesis("69", 0.96),
            new NumberHypothesis("99", 0.63),
            new NumberHypothesis("66", 0.49),
            new NumberHypothesis("59", 0.47)
        ]);

        Assert.Equal(MatchStatus.Review, result.Status);
        Assert.Contains("confusable:", string.Join("|", result.Reasons));
        Assert.Contains("59", string.Join("|", result.Reasons));
        Assert.Contains("66", string.Join("|", result.Reasons));
    }

    [Fact]
    public void SubstringRiskIsReview()
    {
        var entryList = CreateEntryList("5", "55", "555");

        var result = new NumberMatcher().Match(entryList,
        [
            new NumberHypothesis("5", 0.97),
            new NumberHypothesis("55", 0.93),
            new NumberHypothesis("555", 0.82)
        ]);

        Assert.Equal(MatchStatus.Review, result.Status);
        Assert.Contains("substring_risk:55,555", result.Reasons);
    }

    [Fact]
    public void OutOfListDominanceIsReview()
    {
        var entryList = CreateEntryList("69");

        var result = new NumberMatcher().Match(entryList,
        [
            new NumberHypothesis("69", 0.55),
            new NumberHypothesis("111", 0.70)
        ]);

        Assert.Equal(MatchStatus.Review, result.Status);
        Assert.Contains("out_of_list_mass", result.Reasons);
    }

    [Fact]
    public void EvidenceConflictIsReview()
    {
        var entryList = CreateEntryList("69", "96");
        var evidence = new FixedEvidence("BrandConflict", 0.20);

        var result = new NumberMatcher().Match(entryList,
        [
            new NumberHypothesis("69", 0.95),
            new NumberHypothesis("96", 0.70)
        ], evidence);

        Assert.Equal(MatchStatus.Review, result.Status);
        Assert.Contains("evidence_conflict:BrandConflict", result.Reasons);
    }

    [Fact]
    public void NoReadingReturnsNoMatch()
    {
        var entryList = CreateEntryList("69", "96");

        var result = new NumberMatcher().Match(entryList, [new NumberHypothesis("abc", 0.91)]);

        Assert.Equal(MatchStatus.NoMatch, result.Status);
        Assert.Contains("no_reading", result.Reasons);
    }

    private static EntryList CreateEntryList(params string[] numbers)
    {
        return new EntryList(numbers.Select(number => new Entry(number, "Team", "Car", "PRO", [])));
    }

    private sealed class FixedEvidence : IEvidence
    {
        public FixedEvidence(string name, double weight)
        {
            Name = name;
            Weight = weight;
        }

        public string Name { get; }
        public double Weight { get; }

        public double GetWeight(string candidateNumber, EntryList entryList)
        {
            return Weight;
        }
    }
}
