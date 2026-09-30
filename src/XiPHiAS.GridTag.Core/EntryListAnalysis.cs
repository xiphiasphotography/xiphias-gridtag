using System.Collections.ObjectModel;

namespace GridTag.Core;

/// <summary>Precomputed number relationships within one event's entry list.</summary>
public sealed class EntryListAnalysis
{
    /// <summary>Computes confusable neighbours and proper substring hosts in entry-list order.</summary>
    public EntryListAnalysis(EntryList entryList, ConfusionMap confusionMap)
    {
        ArgumentNullException.ThrowIfNull(entryList);
        ArgumentNullException.ThrowIfNull(confusionMap);
        var confusables = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var hosts = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var numbers = entryList.Entries.Select(entry => entry.Number).ToArray();
        foreach (var number in numbers)
        {
            confusables.Add(number, Array.AsReadOnly(numbers.Where(other =>
                confusionMap.AreConfusable(number, other)).ToArray()));
            hosts.Add(number, Array.AsReadOnly(numbers.Where(other =>
                other.Length > number.Length && other.Contains(number, StringComparison.Ordinal)).ToArray()));
        }
        Confusables = new ReadOnlyDictionary<string, IReadOnlyList<string>>(confusables);
        SubstringHosts = new ReadOnlyDictionary<string, IReadOnlyList<string>>(hosts);
    }

    /// <summary>Each listed number's neighbours differing in one confusable digit.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Confusables { get; }
    /// <summary>Each listed number's strictly longer hosts containing that number.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> SubstringHosts { get; }
}
