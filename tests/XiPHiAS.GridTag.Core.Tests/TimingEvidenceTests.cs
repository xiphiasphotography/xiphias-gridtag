using XiPHiAS.GridTag.Core;
using Xunit;

namespace XiPHiAS.GridTag.Core.Tests;

public sealed class TimingEvidenceTests
{
    private static readonly DateTime Capture = new(2026, 10, 2, 14, 0, 0);
    private static EntryList Entries() => new([
        new Entry("007", "Bond", "Aston Martin", "PRO", []),
        new Entry("7", "Other", "Car", "PRO", [])]);

    [Theory]
    [InlineData(5, 5, 1.5)]
    [InlineData(-5, -5, 1.5)]
    [InlineData(0, 1, 1.25)]
    [InlineData(0, 2, 1)]
    [InlineData(0, 3, 0.1)]
    public void OffsetAndToleranceApplyToNearestPassing(int offset, int passing, double weight)
    {
        var evidence = new TimingCrossCheckEvidence(Capture,
            [new("007", Capture.AddSeconds(100)), new("#007", Capture.AddSeconds(passing))],
            TimeSpan.FromSeconds(offset));
        Assert.Equal(weight, evidence.GetWeight("007", Entries()), 8);
        Assert.Equal(1, evidence.GetWeight("7", Entries()));
        Assert.Equal(1, evidence.GetWeight("999", Entries()));
    }

    [Fact]
    public void MissingClocksAndOverflowAreNeutral()
    {
        Assert.Equal(1, new TimingCrossCheckEvidence(default, [new("007", Capture)],
            TimeSpan.Zero).GetWeight("007", Entries()));
        Assert.Equal(1, new TimingCrossCheckEvidence(Capture, [new("007", default)],
            TimeSpan.Zero).GetWeight("007", Entries()));
        Assert.Equal(1, new TimingCrossCheckEvidence(DateTime.MaxValue, [new("007", Capture)],
            TimeSpan.FromSeconds(1)).GetWeight("007", Entries()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimingCrossCheckEvidence(Capture,
            [], TimeSpan.Zero, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("number;time\n\"#007\";\"2026-10-02T14:00:00.123+02:00\"\n", true)]
    [InlineData("number;time\n7;2026-10-02 14:00:00\n", true)]
    [InlineData("number;time\n", true)]
    [InlineData("", false)]
    [InlineData("number;wrong\n007;2026-10-02T14:00:00\n", false)]
    [InlineData("number;time\n;2026-10-02T14:00:00\n", false)]
    [InlineData("number;time\n007;14:00:00\n", false)]
    [InlineData("number;time\n007;02/10/2026 14:00:00\n", false)]
    [InlineData("number;time\n007;invalid\n", false)]
    [InlineData("number;time\n007;2026-10-02T14:00:00;extra\n", false)]
    public void CsvValidatesHeadersAndFullDates(string csv, bool valid)
    {
        var path = Path.Combine(Path.GetTempPath(), $"gridtag-timing-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(path, csv, new System.Text.UTF8Encoding(true));
            if (!valid)
                Assert.Throws<InvalidDataException>(() => TimingCrossCheckEvidence.LoadCsv(path));
            else
            {
                var rows = TimingCrossCheckEvidence.LoadCsv(path);
                if (csv.Contains("#007"))
                {
                    var row = Assert.Single(rows);
                    Assert.Equal("007", row.Number);
                    Assert.Equal(Capture.AddMilliseconds(123), row.Time);
                }
            }
        }
        finally { File.Delete(path); }
    }
}

