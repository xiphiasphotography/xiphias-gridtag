namespace XiPHiAS.GridTag.Core;

/// <summary>Normalizes entry-list numbers without interpreting them as integers.</summary>
public static class NumberNormalizer
{
    /// <summary>Trims whitespace, removes a leading # and uppercases, preserving significant leading zeros.</summary>
    /// <exception cref="InvalidDataException">The number is empty.</exception>
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var number = value.Trim();
        if (number.StartsWith('#'))
            number = number[1..].Trim();
        if (number.Length == 0)
            throw new InvalidDataException("An entry number cannot be empty.");
        return number.ToUpperInvariant();
    }
}
