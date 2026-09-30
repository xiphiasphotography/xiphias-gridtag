using System.Text;

namespace GridTag.Core;

/// <summary>Loads the semicolon-delimited event entry list contract.</summary>
public sealed class EntryListLoader
{
    /// <summary>Reads an entry list from a UTF-8 CSV file, accepting an optional BOM.</summary>
    /// <exception cref="InvalidDataException">The file is missing, malformed, or has an invalid header.</exception>
    public EntryList Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Entry list file '{path}' was not found.", path);

        using var reader = new StreamReader(path, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        using var rows = SemicolonCsvReader.Read(reader).GetEnumerator();
        if (!rows.MoveNext())
            throw new InvalidDataException("The entry list is empty.");

        var header = rows.Current.Select(NormalizeHeader).ToArray();
        var numberIndex = RequiredIndex(header, "number");
        var teamIndex = RequiredIndex(header, "team");
        var carIndex = RequiredIndex(header, "car");
        var classIndex = RequiredIndex(header, "class");
        var driverColumns = header
            .Select((name, index) => (name, index))
            .Where(item => item.name.StartsWith("driver_", StringComparison.Ordinal) && item.name.EndsWith("_nat", StringComparison.Ordinal) == false)
            .Select(item => (Index: item.index, Number: ParseDriverNumber(item.name)))
            .OrderBy(item => item.Number)
            .ToArray();

        var entries = new List<Entry>();
        while (rows.MoveNext())
        {
            var cells = rows.Current;
            if (cells.Length != header.Length)
                throw new InvalidDataException($"Entry row has {cells.Length} cells but the header has {header.Length}.");

            var drivers = driverColumns
                .Select(column => new Driver(cells[column.Index].Trim(),
                    FindNationality(cells, header, column.Number)))
                .Where(driver => !string.IsNullOrWhiteSpace(driver.Name))
                .ToArray();

            entries.Add(new Entry(
                cells[numberIndex],
                cells[teamIndex],
                cells[carIndex],
                cells[classIndex],
                drivers));
        }

        return new EntryList(entries);
    }

    private static string FindNationality(string[] cells, string[] header, int driverNumber)
    {
        var index = Array.IndexOf(header, $"driver_{driverNumber}_nat");
        return index >= 0 ? cells[index].Trim() : string.Empty;
    }

    private static int RequiredIndex(string[] header, string name)
    {
        var index = Array.IndexOf(header, name);
        if (index < 0)
            throw new InvalidDataException($"Entry list header is missing '{name}'.");
        return index;
    }

    private static int ParseDriverNumber(string name)
    {
        var value = name["driver_".Length..];
        if (!int.TryParse(value, out var number) || number < 1)
            throw new InvalidDataException($"Invalid driver column '{name}'.");
        return number;
    }

    private static string NormalizeHeader(string value) => value.Trim().TrimStart('\uFEFF').ToLowerInvariant();
}