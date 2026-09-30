using Xunit;

namespace GridTag.Core.Tests;

public sealed class ConfusionMapTests
{
    [Fact]
    public void HasExactlyTheEightSymmetricDigitPairs()
    {
        string[] expected = ["08", "17", "27", "38", "56", "68", "69", "89"];
        var map = new ConfusionMap();
        for (var first = '0'; first <= '9'; first++)
        for (var second = '0'; second <= '9'; second++)
        {
            var pair = string.Concat((char)Math.Min(first, second), (char)Math.Max(first, second));
            Assert.Equal(expected.Contains(pair), map.AreConfusableDigits(first, second));
        }
    }

    [Theory]
    [InlineData("69", "59", true)]
    [InlineData("69", "66", true)]
    [InlineData("69", "89", true)]
    [InlineData("69", "99", true)]
    [InlineData("69", "96", false)]
    [InlineData("69", "69", false)]
    [InlineData("5", "55", false)]
    [InlineData("59", "99", false)]
    [InlineData("#069", "066", true)]
    public void RequiresExactlyOneConfusableDifference(string first, string second, bool expected)
    {
        var map = new ConfusionMap();
        Assert.Equal(expected, map.AreConfusable(first, second));
        Assert.Equal(expected, map.AreConfusable(second, first));
    }
}
