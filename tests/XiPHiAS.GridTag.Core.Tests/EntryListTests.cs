using Xunit;

namespace XiPHiAS.GridTag.Core.Tests;

public sealed class EntryListTests
{
    [Fact]
    public void RejectsDuplicatesAfterNormalization()
    {
        Assert.Throws<InvalidDataException>(() => new EntryList([
            new Entry("#003", "Team", "Car", "PRO", []),
            new Entry("003", "Other", "Car", "PRO", [])]));
    }

    [Fact]
    public void SnapshotsEntriesAndDriversAndSupportsNormalizedLookup()
    {
        var drivers = new List<Driver> { new("Oliver Söderström", "SWE") };
        var entries = new List<Entry> { new("003", "Team", "Car", "PRO", drivers) };
        var list = new EntryList(entries);
        entries.Clear();
        drivers.Clear();
        Assert.Single(list.Entries);
        Assert.True(list.TryGetEntry("#003", out var entry));
        Assert.False(list.TryGetEntry("3", out _));
        Assert.Equal("Oliver Söderström", Assert.Single(entry.Drivers).Name);
        Assert.False(list.TryGetEntry("99", out _));
    }

    [Fact]
    public void LeadingZeros_DefineDifferentEntries()
    {
        var list = new EntryList([
            new Entry("007", "Bond", "Aston Martin", "PRO", []),
            new Entry("7", "Other", "Car", "PRO", [])]);
        Assert.Equal(2, list.Count);
        Assert.True(list.TryGetEntry("#007", out var bond));
        Assert.Equal("Bond", bond.Team);
        Assert.True(list.TryGetEntry("7", out var other));
        Assert.Equal("Other", other.Team);
    }
}
