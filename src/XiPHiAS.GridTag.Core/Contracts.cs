using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GridTag.Core;

/// <summary>Single manifest photo as passed from the Lightroom plugin to the CLI.</summary>
/// <param name="Id">The Lightroom local identifier.</param>
/// <param name="Uuid">The Lightroom UUID.</param>
/// <param name="Path">The source RAW path.</param>
/// <param name="CaptureTime">The capture timestamp.</param>
/// <param name="ManualNumber">Optional manual override, using comma/semicolon/space separators.</param>
public sealed record ManifestPhoto(int Id, string Uuid, string Path, DateTimeOffset CaptureTime, string? ManualNumber = null);

/// <summary>Manifest file read from disk before processing.</summary>
/// <param name="SchemaVersion">Version of the manifest contract.</param>
/// <param name="Photos">The photos to process.</param>
public sealed record Manifest(int SchemaVersion, IReadOnlyList<ManifestPhoto> Photos);

/// <summary>One detected car within a photo.</summary>
/// <param name="Id">Detection identifier.</param>
/// <param name="Confidence">Detection confidence.</param>
/// <param name="Bounds">Optional image-space bounds used by later crop-based stages.</param>
public sealed record DetectedCar(string Id, double Confidence, DetectionBounds? Bounds = null);

/// <summary>Image-space detection bounds using left, top, right and bottom pixels.</summary>
/// <param name="Left">Left edge in the original preview.</param>
/// <param name="Top">Top edge in the original preview.</param>
/// <param name="Right">Right edge in the original preview.</param>
/// <param name="Bottom">Bottom edge in the original preview.</param>
public sealed record DetectionBounds(float Left, float Top, float Right, float Bottom);

/// <summary>One candidate car result after match resolution.</summary>
/// <param name="Number">Matched number.</param>
/// <param name="Confidence">Confidence after matching.</param>
/// <param name="Source">Origin of the candidate, such as manual or ocr.</param>
/// <param name="Primary">Whether this is the primary car for the photo.</param>
public sealed record CarCandidate(string Number, double Confidence, string Source, bool Primary);

/// <summary>A per-photo result written back to the plugin.</summary>
/// <param name="Id">Photo identifier.</param>
/// <param name="Status">Status enum value in camelCase.</param>
/// <param name="Reasons">Zero or more reasoning notes.</param>
/// <param name="Session">Resolved session code or name.</param>
/// <param name="Cars">Matched cars for the photo.</param>
/// <param name="Fields">Generated metadata when the photo resolved.</param>
public sealed record PhotoResult(
    int Id,
    string Status,
    IReadOnlyList<string> Reasons,
    string? Session = null,
    IReadOnlyList<CarCandidate>? Cars = null,
    GeneratedFields? Fields = null);

/// <summary>Container for a whole result file.</summary>
/// <param name="SchemaVersion">Version of the result contract.</param>
/// <param name="ToolVersion">Tool version label.</param>
/// <param name="GeneratedAt">UTC timestamp when these results were generated.</param>
/// <param name="Photos">All per-photo results.</param>
public sealed record ResultFile(int SchemaVersion, string ToolVersion, DateTimeOffset GeneratedAt, IReadOnlyList<PhotoResult> Photos);

/// <summary>Helpers for JSON serialization and deserialization of the manifest/result file contracts.</summary>
public static class GridTagJson
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    /// <summary>Deserializes a manifest file from disk.</summary>
    public static Manifest ReadManifest(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var resolvedPath = ResolveInputPath(path);
        var content = File.ReadAllText(resolvedPath);
        return JsonSerializer.Deserialize<Manifest>(content, JsonOptions)
            ?? throw new InvalidOperationException($"Manifest file '{resolvedPath}' could not be deserialized.");
    }

    /// <summary>Deserializes a result file from disk.</summary>
    public static ResultFile ReadResultFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var resolvedPath = ResolveInputPath(path);
        var content = File.ReadAllText(resolvedPath);
        return JsonSerializer.Deserialize<ResultFile>(content, JsonOptions)
            ?? throw new InvalidOperationException($"Result file '{resolvedPath}' could not be deserialized.");
    }

    private static string ResolveInputPath(string path)
    {
        if (File.Exists(path))
            return path;

        var candidate = Path.GetFullPath(path, Directory.GetCurrentDirectory());
        if (File.Exists(candidate))
            return candidate;

        var baseDirectory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(baseDirectory))
        {
            var searchPath = Path.GetFullPath(Path.Combine(baseDirectory, path));
            if (File.Exists(searchPath))
                return searchPath;

            var parent = Directory.GetParent(baseDirectory);
            if (parent is null)
                break;
            baseDirectory = parent.FullName;
        }

        return path;
    }

    /// <summary>Serializes a result file without a UTF-8 BOM.</summary>
    public static void WriteResultFile(string path, ResultFile result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(result);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }
}
