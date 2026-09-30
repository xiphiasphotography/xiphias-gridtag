using System.Text;

namespace GridTag.Core;

internal static class SemicolonCsvReader
{
    internal static IEnumerable<string[]> Read(TextReader reader)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var closedQuote = false;
        var started = false;
        while (reader.Read() is var next && next != -1)
        {
            var character = (char)next;
            if (quoted)
            {
                if (character != '"')
                    cell.Append(character);
                else if (reader.Peek() == '"')
                {
                    reader.Read();
                    cell.Append('"');
                }
                else
                {
                    quoted = false;
                    closedQuote = true;
                }
                continue;
            }
            if (character is ';' or '\r' or '\n')
            {
                if (character == '\r' && reader.Peek() == '\n')
                    reader.Read();
                // Ignore physically empty lines, not delimiter-only records.
                if (character != ';' && !started && cells.Count == 0)
                    continue;
                cells.Add(cell.ToString());
                cell.Clear();
                closedQuote = false;
                started = character == ';';
                if (character != ';')
                {
                    yield return cells.ToArray();
                    cells.Clear();
                }
                continue;
            }
            if (closedQuote)
                throw new InvalidDataException("Unexpected text after a closing CSV quote.");
            if (character == '"')
            {
                if (started)
                    throw new InvalidDataException("A CSV quote must begin a cell.");
                quoted = true;
            }
            else
                cell.Append(character);
            started = true;
        }
        if (quoted)
            throw new InvalidDataException("Unterminated quoted CSV cell.");
        if (started || cells.Count > 0)
        {
            cells.Add(cell.ToString());
            yield return cells.ToArray();
        }
    }
}
