using Xunit;

namespace GridTag.Core.Tests;

public sealed class EntryListAnalysisTests
{
    [Fact]
    public void ComputesConfusablesAndProperHostsWithoutChangingSourceOrder()
    {
        string[] numbers = ["5", "55", "555", "59", "66", "69", "89", "96", "99", "991"];
        var list = new EntryList(numbers.Select(number => new Entry(number, "Team", "Car", "PRO", [])));
        var analysis = new EntryListAnalysis(list, new ConfusionMap());
        Assert.Equal(new[] { "59", "66", "89", "99" }, analysis.Confusables["69"]);
        Assert.Equal(new[] { "55", "555", "59" }, analysis.SubstringHosts["5"]);
        Assert.Equal(new[] { "555" }, analysis.SubstringHosts["55"]);
        Assert.Equal(new[] { "991" }, analysis.SubstringHosts["99"]);
        Assert.Empty(analysis.SubstringHosts["991"]);
        Assert.DoesNotContain("96", analysis.Confusables["69"]);
        Assert.DoesNotContain("69", analysis.Confusables["69"]);
        Assert.Equal(numbers, list.Entries.Select(entry => entry.Number));
    }

    [Fact]
    public void HandlesAnEmptyEntryList()
    {
        var analysis = new EntryListAnalysis(new EntryList([]), new ConfusionMap());
        Assert.Empty(analysis.Confusables);
        Assert.Empty(analysis.SubstringHosts);
    }
}
