namespace GridTag.Core;

/// <summary>The fixed symmetric digit confusions specified in AGENTS.md.</summary>
public sealed class ConfusionMap
{
    /// <summary>Returns whether two distinct digits form a configured confusion pair.</summary>
    public bool AreConfusableDigits(char first, char second)
    {
        if (first > second)
            (first, second) = (second, first);
        return (first, second) is ('0', '8') or ('1', '7') or ('2', '7') or
            ('3', '8') or ('5', '6') or ('6', '8') or ('6', '9') or ('8', '9');
    }

    /// <summary>Returns true only for equal-length numbers differing in exactly one confusable digit.</summary>
    public bool AreConfusable(string first, string second)
    {
        first = NumberNormalizer.Normalize(first);
        second = NumberNormalizer.Normalize(second);
        if (first.Length != second.Length)
            return false;
        var differences = 0;
        for (var i = 0; i < first.Length; i++)
        {
            if (first[i] == second[i])
                continue;
            if (!AreConfusableDigits(first[i], second[i]) || ++differences > 1)
                return false;
        }
        return differences == 1;
    }
}
