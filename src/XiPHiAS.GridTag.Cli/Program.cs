using System.Text.Json;
using GridTag.Core;
using GridTag.Vision;

namespace GridTag.Cli;

/// <summary>Command-line dispatcher for GridTag.</summary>
public static class CliApp
{
	private const string ToolVersion = "0.1.0";

	/// <summary>Runs one CLI command and returns its documented process exit code.</summary>
	public static int Run(string[] args, TextWriter output, TextWriter error)
	{
		ArgumentNullException.ThrowIfNull(args);
		ArgumentNullException.ThrowIfNull(output);
		ArgumentNullException.ThrowIfNull(error);
		if (args.Length == 0)
			return Usage(error, "A command is required.");

		try
		{
			return args[0] switch
			{
				"run" => RunCommand(args[1..], output, error),
				"preview" => PreviewCommand(args[1..], output, error),
				"eval" => EvalCommand(args[1..], output, error),
				"check-entrylist" => CheckEntryListCommand(args[1..], output, error),
				"fields" => FieldsCommand(args[1..], output, error),
				"version" => VersionCommand(args[1..], output, error),
				_ => Usage(error, $"Unknown command '{args[0]}'.")
			};
		}
		catch (FileNotFoundException exception)
		{
			error.WriteLine(exception.Message);
			return 3;
		}
		catch (DirectoryNotFoundException exception)
		{
			error.WriteLine(exception.Message);
			return 3;
		}
		catch (InvalidDataException exception)
		{
			error.WriteLine(exception.Message);
			return 3;
		}
		catch (Exception exception)
		{
			error.WriteLine(exception.Message);
			return 1;
		}
	}

	private static int RunCommand(string[] args, TextWriter output, TextWriter error)
	{
		var options = ParseOptions(args, error);
		if (options is null)
			return 2;

		var manifest = GridTagJson.ReadManifest(Required(options, "manifest"));
		var entryList = new EntryListLoader().Load(Required(options, "entrylist"));
		var context = LoadEventContext(Required(options, "session"));
		var pipeline = new TaggingPipeline(entryList, context, new EmbeddedJpegRawPreviewProvider(), CreateCarDetector(options), CreatePlateReader(options), evidenceProvider: CreateEvidenceProvider(options));
		var result = pipeline.Process(manifest);
		GridTagJson.WriteResultFile(Required(options, "out"), result);
		output.WriteLine($"Processed {result.Photos.Count} photo(s).");
		return 0;
	}

	private static int PreviewCommand(string[] args, TextWriter output, TextWriter error)
	{
		var options = ParseOptions(args, error);
		if (options is null)
			return 2;

		var preview = new EmbeddedJpegRawPreviewProvider().GetPreview(Required(options, "file")) as RawPreview;
		if (preview is null)
		{
			error.WriteLine("Geen preview kon uit het bestand worden gelezen.");
			return 1;
		}

		File.WriteAllBytes(Required(options, "out"), preview.JpegBytes);
		output.WriteLine($"Preview: {preview.Width}x{preview.Height}, source={preview.Source}.");
		return 0;
	}

	private static int CheckEntryListCommand(string[] args, TextWriter output, TextWriter error)
	{
		var options = ParseOptions(args, error);
		if (options is null)
			return 2;
		var entryList = new EntryListLoader().Load(Required(options, "entrylist"));
		output.WriteLine($"Valid entry list: {entryList.Count} entries.");
		return 0;
	}

	private static int EvalCommand(string[] args, TextWriter output, TextWriter error)
	{
		var options = ParseOptions(args, error);
		if (options is null)
			return 2;

		var labels = EvaluationInput.ReadLabels(Required(options, "labels"));
		var entryList = new EntryListLoader().Load(Required(options, "entrylist"));
		var context = LoadEventContext(Required(options, "session"));
		var pipeline = new TaggingPipeline(entryList, context, new EmbeddedJpegRawPreviewProvider(), CreateCarDetector(options), CreatePlateReader(options), evidenceProvider: CreateEvidenceProvider(options));
		var labelIndex = 0;
		var report = new EvaluationRunner().Evaluate(labels, label => pipeline.ProcessPhoto(new ManifestPhoto(
			++labelIndex,
			$"eval-{labelIndex}",
			label.Path,
			new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))));

		output.WriteLine($"photos: {report.TotalPhotos}");
		output.WriteLine($"auto precision: {report.AutoPrecision:P2}");
		output.WriteLine($"recall: {report.Recall:P2}");
		output.WriteLine($"review rate: {report.ReviewRate:P2}");
		output.WriteLine($"seconds/photo: {report.SecondsPerPhoto:F4}");
		output.WriteLine($"go/no-go: {(report.MeetsGoNoGo ? "GO" : "NO-GO")}");
		output.WriteLine("reasons:");
		foreach (var reason in report.ReasonCounts.OrderBy(pair => pair.Key))
			output.WriteLine($"  {reason.Key}: {reason.Value}");
		output.WriteLine("confusions:");
		foreach (var confusion in report.Confusions.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key))
			output.WriteLine($"  {confusion.Key}: {confusion.Value}");
		return 0;
	}

	private static int FieldsCommand(string[] args, TextWriter output, TextWriter error)
	{
		var options = ParseOptions(args, error);
		if (options is null)
			return 2;
		var entryList = new EntryListLoader().Load(Required(options, "entrylist"));
		if (!entryList.TryGetEntry(Required(options, "number"), out var entry))
			throw new InvalidDataException("The requested number is not present in the entry list.");
		var fields = new FieldBuilder().Build(entry, LoadEventContext(Required(options, "session")));
		output.WriteLine($"headline: {fields.Headline}");
		output.WriteLine($"caption: {fields.Caption}");
		output.WriteLine($"altText: {fields.AltText}");
		output.WriteLine($"extDescription: {fields.ExtDescription}");
		output.WriteLine($"keywords: {string.Join(", ", fields.Keywords)}");
		output.WriteLine($"persons: {string.Join(", ", fields.Persons)}");
		return 0;
	}

	private static int VersionCommand(string[] args, TextWriter output, TextWriter error)
	{
		if (args.Length != 0)
			return Usage(error, "version does not accept options.");
		output.WriteLine(ToolVersion);
		return 0;
	}

	private static EventContext LoadEventContext(string path)
	{
	    if (!File.Exists(path))
	        throw new FileNotFoundException($"Session file '{path}' was not found.", path);

	    try
	    {
	        using var document = JsonDocument.Parse(File.ReadAllText(path));
	        var root = document.RootElement;
	        var sessions = RequireProperty(root, path, "sessions").EnumerateArray().Select(session => new EventSession(
	            ReadString(session, path, "code"),
	            ReadString(session, path, "name"),
	            ReadTimestamp(session, path, "start"),
	            ReadTimestamp(session, path, "end"))).ToArray();
	        return EventContext.Default(
	            ReadString(root, path, "seriesName"),
	            ReadString(root, path, "eventFullName"),
	            ReadString(root, path, "location"),
	            ReadString(root, path, "defaultSession"),
	            sessions);
	    }
	    catch (JsonException exception)
	    {
	        throw new InvalidDataException($"Session file '{path}' is not valid JSON: {exception.Message}", exception);
	    }
	}

	private static JsonElement RequireProperty(JsonElement element, string path, string name)
	{
	    if (!element.TryGetProperty(name, out var value))
	        throw new InvalidDataException($"Session file '{path}' is missing the required property '{name}'.");
	    return value;
	}

	private static string ReadString(JsonElement element, string path, string name)
	{
	    var property = RequireProperty(element, path, name);
	    if (property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString()))
	        throw new InvalidDataException($"Session file '{path}' has an invalid value for '{name}'.");
	    return property.GetString()!;
	}

	private static DateTimeOffset ReadTimestamp(JsonElement element, string path, string name)
	{
	    var value = ReadString(element, path, name);
	    if (!DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var timestamp))
	        throw new InvalidDataException($"Session file '{path}' has an unparsable timestamp for '{name}': '{value}'.");
	    return timestamp;
	}

	private static ICarDetector CreateCarDetector(IReadOnlyDictionary<string, string> options)
	{
		return options.TryGetValue("vision-config", out var configPath)
			? new OnnxCarDetector(CarDetectorConfig.Load(configPath))
			: new NullCarDetector();
	}

	private static IPlateReader CreatePlateReader(IReadOnlyDictionary<string, string> options)
	{
		if (!options.TryGetValue("plate-config", out var configPath))
			return new NullPlateReader();

		var config = PlateReaderConfig.Load(configPath);
		return new OnnxPlateReader(new OnnxDigitRecognizer(config), config);
	}

	private static Func<ManifestPhoto, IEvidence?>? CreateEvidenceProvider(IReadOnlyDictionary<string, string> options)
	{
		if (!options.TryGetValue("timing-csv", out var timingPath))
			return null;

		var passingTimes = TimingCrossCheckEvidence.LoadCsv(timingPath);
		var offset = options.TryGetValue("clock-offset", out var offsetText)
			? TimeSpan.Parse(offsetText, System.Globalization.CultureInfo.InvariantCulture)
			: TimeSpan.Zero;
		return photo => new TimingCrossCheckEvidence(photo.CaptureTime.DateTime, passingTimes, offset);
	}

	private static Dictionary<string, string>? ParseOptions(string[] args, TextWriter error)
	{
		var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		for (var index = 0; index < args.Length; index++)
		{
			if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
			{
				error.WriteLine($"Invalid option syntax near '{args[index]}'.");
				return null;
			}
			options[args[index][2..]] = args[++index];
		}
		return options;
	}

	private static string Required(IReadOnlyDictionary<string, string> options, string name)
	{
		if (!options.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
			throw new InvalidDataException($"Required option '--{name}' is missing.");
		return value;
	}

	private static int Usage(TextWriter error, string message)
	{
		error.WriteLine(message);
		error.WriteLine("Usage: gridtag <run|preview|eval|check-entrylist|fields|version> [options]");
		return 2;
	}
}

internal static class Program
{
	public static int Main(string[] args) => CliApp.Run(args, Console.Out, Console.Error);
}
