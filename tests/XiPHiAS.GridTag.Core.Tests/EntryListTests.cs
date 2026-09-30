using Xunit;

namespace GridTag.Core.Tests;

public sealed class EntryListTests
{
    [Fact]
    public void RejectsDuplicatesAfterNormalization()
    {
        Assert.Throws<InvalidDataException>(() => new EntryList([
            new Entry("#03", "Team", "Car", "PRO", []),
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
        Assert.True(list.TryGetEntry("#03", out var entry));
        Assert.Equal("Oliver Söderström", Assert.Single(entry.Drivers).Name);
        Assert.False(list.TryGetEntry("99", out _));
    }
}
