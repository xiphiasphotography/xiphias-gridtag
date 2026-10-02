using Xunit;

namespace XiPHiAS.GridTag.Core.Tests;

public sealed class NumberNormalizerTests
{
    [Theory]
    [InlineData("#03", "03")]
    [InlineData("003", "003")]
    [InlineData("0", "0")]
    [InlineData("000", "000")]
    [InlineData("  #003  ", "003")]
    [InlineData(" 00ab ", "00AB")]
    [InlineData("007", "007")]
    [InlineData("7", "7")]
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
