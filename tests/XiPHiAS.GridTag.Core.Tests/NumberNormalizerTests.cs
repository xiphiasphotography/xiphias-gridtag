using Xunit;

namespace GridTag.Core.Tests;

public sealed class NumberNormalizerTests
{
    [Theory]
    [InlineData("#03", "3")]
    [InlineData("003", "3")]
    [InlineData("0", "0")]
    [InlineData("000", "0")]
    [InlineData("  #003  ", "3")]
    [InlineData(" 00ab ", "AB")]
    [InlineData("991", "991")]
    public void NormalizesWithoutNumericConversion(string input, string expected) =>
        Assert.Equal(expected, NumberNormalizer.Normalize(input));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("#")]
    public void RejectsEmptyNumber(string input) =>
        Assert.Throws<InvalidDataException>(() => NumberNormalizer.Normalize(input));
}
