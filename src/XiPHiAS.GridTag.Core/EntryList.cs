using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace GridTag.Core;

/// <summary>A driver and optional nationality abbreviation.</summary>
/// <param name="Name">The driver's name, preserving Unicode.</param>
/// <param name="Nationality">The nationality abbreviation, or an empty string.</param>
public sealed record Driver(string Name, string Nationality);

/// <summary>One car in the event entry list.</summary>
public sealed record Entry
{
    /// <summary>Creates an entry and snapshots the ordered driver collection.</summary>
    public Entry(string number, string team, string car, string @class, IEnumerable<Driver> drivers)
    {
        Number = NumberNormalizer.Normalize(number);
        Team = team;
        Car = car;
        Class = @class;
        Drivers = Array.AsReadOnly(drivers.ToArray());
    }

    /// <summary>The normalized start number.</summary>
    public string Number { get; }
    /// <summary>The team name.</summary>
    public string Team { get; }
    /// <summary>The car model.</summary>
    public string Car { get; }
    /// <summary>The competition class.</summary>
    public string Class { get; }
    /// <summary>Drivers in driver-column order.</summary>
    public IReadOnlyList<Driver> Drivers { get; }
}

/// <summary>An immutable snapshot of entries indexed by normalized number.</summary>
public sealed class EntryList
{
    private readonly ReadOnlyDictionary<string, Entry> byNumber;

    /// <summary>Creates a list, rejecting duplicate normalized numbers.</summary>
    /// <exception cref="InvalidDataException">Two entries have the same normalized number.</exception>
    public EntryList(IEnumerable<Entry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var snapshot = entries.ToArray();
        var index = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var entry in snapshot)
            if (!index.TryAdd(entry.Number, entry))
                throw new InvalidDataException($"Duplicate entry number '{entry.Number}'.");
        Entries = Array.AsReadOnly(snapshot);
        byNumber = new ReadOnlyDictionary<string, Entry>(index);
    }

    /// <summary>Entries in source order.</summary>
    public IReadOnlyList<Entry> Entries { get; }
    /// <summary>The number of entries.</summary>
    public int Count => Entries.Count;
    /// <summary>Looks up a number using the same normalization as the loader.</summary>
    public bool TryGetEntry(string number, [NotNullWhen(true)] out Entry? entry) =>
        byNumber.TryGetValue(NumberNormalizer.Normalize(number), out entry);
}
