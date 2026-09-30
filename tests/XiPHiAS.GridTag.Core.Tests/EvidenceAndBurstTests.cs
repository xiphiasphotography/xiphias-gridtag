using GridTag.Core;
using Xunit;

namespace GridTag.Core.Tests;

public sealed class EvidenceAndBurstTests
{
    [Fact]
    public void CarModelEvidence_StrengthensMatchingCandidate()
    {
        var entries = Entries();
        var evidence = new CarModelEvidence(new CarModelObservation("Ferrari 296", 0.8));

        Assert.True(evidence.GetWeight("69", entries) > 1.0);
        Assert.True(evidence.GetWeight("3", entries) < 1.0);
    }

    [Fact]
    public void DriverNameEvidence_StrengthensDriverCandidate()
    {
        var entries = Entries();
        var evidence = new DriverNameEvidence([new DriverNameObservation("Thierry Vermeulen", 0.9)]);

        Assert.True(evidence.GetWeight("69", entries) > 1.0);
        Assert.Equal(1.0, evidence.GetWeight("3", entries));
    }

    [Fact]
    public void TimingEvidence_UsesClockOffset()
    {
        var entries = Entries();
        var capture = new DateTime(2026, 9, 18, 13, 52, 0);
        var evidence = new TimingCrossCheckEvidence(capture, [new PassingTime("69", new DateTime(2026, 9, 18, 13, 52, 5))], TimeSpan.FromSeconds(5));

        Assert.True(evidence.GetWeight("69", entries) > 1.0);
        Assert.Equal(1.0, evidence.GetWeight("3", entries));
    }

    [Fact]
    public void TimingEvidence_LoadsSemicolonCsv()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gridtag-timing-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, "number;time\n69;2026-09-18 13:52:05\n");
        try
        {
            var rows = TimingCrossCheckEvidence.LoadCsv(path);
            Assert.Single(rows);
            Assert.Equal("69", rows[0].Number);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BurstPropagation_RequiresTimeAndSimilarityAndNeverReturnsAuto()
    {
        var source = Photo(1, 10);
        var proposals = BurstPropagation.Propose(
            source,
            "69",
            [
                new BurstFrame(Photo(2, 11), 0.95),
                new BurstFrame(Photo(3, 20), 0.99),
                new BurstFrame(Photo(4, 12), 0.50),
            ],
            TimeSpan.FromSeconds(2),
            0.8);

        var proposal = Assert.Single(proposals);
        Assert.Equal(2, proposal.PhotoId);
        Assert.Equal("69", proposal.Number);
        Assert.Equal("burst_propagation_review", proposal.Reason);
    }

    private static EntryList Entries() => new(
    [
        new Entry("3", "Team 3", "Mercedes-AMG GT3 EVO", "PRO", []),
        new Entry("69", "Team 69", "Ferrari 296 GT3 EVO", "PRO", [new Driver("Thierry Vermeulen", "NED")]),
    ]);

    private static ManifestPhoto Photo(int id, int second) => new(
        id,
        $"u{id}",
        $"photo-{id}.arw",
        new DateTimeOffset(2026, 9, 18, 13, 0, second, TimeSpan.Zero));
}
