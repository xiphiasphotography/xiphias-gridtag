using System.IO;
using System.Text.Json;

namespace XiPHiAS.GridTag.Review;

internal sealed record ReviewPhoto(int Id, string Status, string Reasons, string Path, string Session, string Candidates)
{
    public string Display => $"{(Status == "noCar" ? "Geen auto" : "Review")} · #{Id} · {System.IO.Path.GetFileName(Path)}";
}

internal static class ReviewSession
{
    public static async Task<IReadOnlyList<ReviewPhoto>> LoadAsync(string resultsPath, string? manifestPath, CancellationToken cancellationToken)
    {
        using var results = await ReadAsync(resultsPath, cancellationToken);
        var paths = new Dictionary<int, string>();
        if (manifestPath is not null)
        {
            using var manifest = await ReadAsync(manifestPath, cancellationToken);
            foreach (var photo in manifest.RootElement.GetProperty("photos").EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = photo.GetProperty("path").GetString() ?? string.Empty;
                if (path.Length > 0)
                    path = System.IO.Path.GetFullPath(path, System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(manifestPath))!);
                if (!paths.TryAdd(photo.GetProperty("id").GetInt32(), path))
                    throw new InvalidDataException("Het manifest bevat dubbele foto-ID's.");
            }
        }

        var photos = new List<ReviewPhoto>();
        var ids = new HashSet<int>();
        foreach (var photo in results.RootElement.GetProperty("photos").EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = photo.GetProperty("id").GetInt32();
            if (!ids.Add(id))
                throw new InvalidDataException("De resultaten bevatten dubbele foto-ID's.");
            var status = photo.GetProperty("status").GetString();
            if (status is not ("review" or "noCar"))
                continue;
            var reasons = photo.TryGetProperty("reasons", out var reasonArray)
                ? string.Join(", ", reasonArray.EnumerateArray().Select(reason => reason.GetString())) : string.Empty;
            var session = photo.TryGetProperty("session", out var sessionValue) ? sessionValue.GetString() ?? string.Empty : string.Empty;
            var candidates = photo.TryGetProperty("cars", out var cars) && cars.ValueKind == JsonValueKind.Array
                ? string.Join(", ", cars.EnumerateArray().Select(car => $"#{car.GetProperty("number").GetString()} ({car.GetProperty("confidence").GetDouble():P0}, {car.GetProperty("source").GetString()})"))
                : string.Empty;
            photos.Add(new ReviewPhoto(id, status, reasons, paths.GetValueOrDefault(id, string.Empty), session, candidates));
        }
        return photos;
    }

    private static async Task<JsonDocument> ReadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        try
        {
            if (document.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
                throw new InvalidDataException("Dit bestand heeft een niet-ondersteunde schemaVersion (verwacht: 1).");
            if (document.RootElement.GetProperty("photos").ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Het bestand bevat geen geldige fotolijst.");
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }
}
