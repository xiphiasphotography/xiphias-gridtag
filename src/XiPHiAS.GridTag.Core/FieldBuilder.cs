using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace GridTag.Core;

/// <summary>Represents one named event session with its time window.</summary>
/// <param name="Code">The canonical session code.</param>
/// <param name="Name">The human-readable session name.</param>
/// <param name="Start">The start timestamp for the session.</param>
/// <param name="End">The end timestamp for the session.</param>
public sealed record EventSession(string Code, string Name, DateTimeOffset Start, DateTimeOffset End);

/// <summary>Contains the event-level context used to render metadata fields.</summary>
/// <param name="SeriesName">The series name.</param>
/// <param name="EventFullName">The full event name.</param>
/// <param name="Location">The event location.</param>
/// <param name="DefaultSession">The fallback session code.</param>
/// <param name="Sessions">All configured sessions.</param>
/// <param name="Templates">The field templates for the event.</param>
public sealed record EventContext(
    string SeriesName,
    string EventFullName,
    string Location,
    string DefaultSession,
    IReadOnlyList<EventSession> Sessions,
    IReadOnlyDictionary<string, FieldTemplates> Templates)
{
    /// <summary>Builds a default event context with the standard templates.</summary>
    public static EventContext Default(string seriesName, string eventFullName, string location, string defaultSession, IReadOnlyList<EventSession> sessions)
    {
        var templates = new Dictionary<string, FieldTemplates>(StringComparer.Ordinal)
        {
            ["default"] = FieldTemplates.Default(),
            ["noSession"] = FieldTemplates.NoSessionDefault()
        };

        return new EventContext(seriesName, eventFullName, location, defaultSession, sessions, new ReadOnlyDictionary<string, FieldTemplates>(templates));
    }
}

/// <summary>Provides the template strings used to render metadata fields.</summary>
/// <param name="Headline">Headline template.</param>
/// <param name="Caption">Caption template when a session is present.</param>
/// <param name="CaptionNoSession">Caption template when no session applies.</param>
/// <param name="AltText">Accessibility alt text template.</param>
/// <param name="ExtDescription">Extended description template when a session is present.</param>
/// <param name="ExtDescriptionNoSession">Extended description template when no session applies.</param>
public sealed record FieldTemplates(
    string Headline,
    string Caption,
    string CaptionNoSession,
    string AltText,
    string ExtDescription,
    string ExtDescriptionNoSession)
{
    /// <summary>Gets the default Dutch templates as specified by the project contract.</summary>
    public static FieldTemplates Default() => new(
        "#{nr} {team} {car}",
        "{session} - {drivers} {verb} de {car} met startnummer #{nr} voor {team} tijdens de {event} op {location}.",
        "{drivers} {verb} de {car} met startnummer #{nr} voor {team} tijdens de {event} op {location}.",
        "Raceauto nummer {nr} van {team} in actie op {location} tijdens de {series}.",
        "De {car} met startnummer {nr} van {team} rijdt op {location} tijdens {session} van de {series}.",
        "De {car} met startnummer {nr} van {team} rijdt op {location} tijdens de {series}.");

    /// <summary>Gets the default no-session template set.</summary>
    public static FieldTemplates NoSessionDefault()
    {
        var defaults = Default();
        return defaults with
        {
            Caption = defaults.CaptionNoSession,
            ExtDescription = defaults.ExtDescriptionNoSession
        };
    }
}

/// <summary>Renders a template string using the known field placeholders.</summary>
public sealed class TemplateRenderer
{
    private static readonly HashSet<string> AllowedTokens = new(StringComparer.Ordinal)
    {
        "nr", "team", "car", "class", "drivers", "verb", "session", "series", "event", "location"
    };

    /// <summary>Replaces all supported placeholders in the supplied template.</summary>
    public string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);

        foreach (var token in AllowedTokens)
        {
            if (!values.TryGetValue(token, out var value))
                throw new InvalidOperationException($"Missing template value for '{token}'.");
            template = template.Replace($"{{{token}}}", value, StringComparison.Ordinal);
        }

        var remaining = System.Text.RegularExpressions.Regex.Matches(template, @"\{([^}]+)\}");
        if (remaining.Count > 0)
            throw new InvalidOperationException($"Unknown placeholder '{remaining[0].Groups[1].Value}'.");

        return template;
    }
}

/// <summary>Resolves the active event session based on the photo timestamp.</summary>
public sealed class SessionResolver
{
    /// <summary>Returns the session code matching the capture time, or the default session as fallback.</summary>
    public string? Resolve(DateTimeOffset captureTime, EventContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var session in context.Sessions)
        {
            if (captureTime >= session.Start && captureTime <= session.End)
                return session.Code;
        }

        return context.DefaultSession;
    }

    /// <summary>Returns the human-readable session name for a code, falling back to the code itself.</summary>
    public static string? ResolveName(string? sessionCode, EventContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(sessionCode))
            return sessionCode;

        foreach (var session in context.Sessions)
        {
            if (string.Equals(session.Code, sessionCode, StringComparison.OrdinalIgnoreCase))
                return session.Name;
        }

        return sessionCode;
    }
}

/// <summary>Contains the generated metadata strings and keywords for one car.</summary>
/// <param name="Headline">The headline text.</param>
/// <param name="Caption">The caption text.</param>
/// <param name="AltText">The alt text.</param>
/// <param name="ExtDescription">The extended description.</param>
/// <param name="Keywords">The generated keyword list.</param>
/// <param name="Persons">The distinct driver names.</param>
public sealed record GeneratedFields(
    string Headline,
    string Caption,
    string AltText,
    string ExtDescription,
    string[] Keywords,
    string[] Persons)
{
    /// <summary>Gets the resolved session code used when building the fields.</summary>
    [JsonIgnore]
    public string? Session { get; init; }
}

/// <summary>One car's entry-list data used when assembling keywords.</summary>
/// <param name="Entry">The entry-list row for the car.</param>
public sealed record CarKeywordSource(Entry Entry);

/// <summary>Assembles the ordered, case-insensitively de-duplicated keyword list for one or more cars.</summary>
public static class KeywordAssembler
{
    /// <summary>Builds keywords as team, car, drivers, #number per car, then the session name, then distinct classes with their phases.</summary>
    public static string[] Assemble(IReadOnlyList<CarKeywordSource> cars, string? sessionName)
    {
        var keywords = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var classes = new List<string>();
        var seenClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string keyword)
        {
            if (!string.IsNullOrWhiteSpace(keyword) && seen.Add(keyword))
                keywords.Add(keyword);
        }

        foreach (var car in cars)
        {
            Add(car.Entry.Team);
            Add(car.Entry.Car);
            foreach (var driver in car.Entry.Drivers)
                Add(driver.Name);
            Add($"#{car.Entry.Number}");
            if (seenClasses.Add(car.Entry.Class))
                classes.Add(car.Entry.Class);
        }

        if (!string.IsNullOrWhiteSpace(sessionName))
            Add(sessionName);
        foreach (var @class in classes)
            Add(@class);

        return keywords.ToArray();
    }
}

/// <summary>Builds the metadata fields for a single entry using the event templates.</summary>
public sealed class FieldBuilder
{
    private readonly TemplateRenderer renderer = new();

    /// <summary>Builds the generated field values for one entry.</summary>
    public GeneratedFields Build(Entry entry, EventContext context, string? sessionCode = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(context);

        var session = sessionCode ?? context.DefaultSession;
        var sessionName = SessionResolver.ResolveName(session, context);
        var drivers = entry.Drivers.Select(d => string.IsNullOrWhiteSpace(d.Nationality) ? d.Name : $"{d.Name} ({d.Nationality})").ToArray();
        var verb = entry.Drivers.Count > 1 ? "rijden" : "rijdt";
        var driverText = drivers.Length switch
        {
            0 => string.Empty,
            1 => drivers[0],
            2 => $"{drivers[0]} en {drivers[1]}",
            _ => $"{string.Join(", ", drivers.Take(drivers.Length - 1))} en {drivers[^1]}"
        };

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["nr"] = entry.Number,
            ["team"] = entry.Team,
            ["car"] = entry.Car,
            ["class"] = entry.Class,
            ["drivers"] = driverText,
            ["verb"] = verb,
            ["session"] = sessionName ?? string.Empty,
            ["series"] = context.SeriesName,
            ["event"] = context.EventFullName,
            ["location"] = context.Location,
        };

        var templates = context.Templates.TryGetValue("default", out var defaultTemplates)
            ? defaultTemplates
            : FieldTemplates.Default();

        var headline = renderer.Render(templates.Headline, values);
        var captionTemplate = string.IsNullOrWhiteSpace(sessionName) ? templates.CaptionNoSession : templates.Caption;
        var caption = renderer.Render(captionTemplate, values);
        var altText = renderer.Render(templates.AltText, values);
        var extDescription = string.IsNullOrWhiteSpace(sessionName)
            ? renderer.Render(templates.ExtDescriptionNoSession, values)
            : renderer.Render(templates.ExtDescription, values);

        var keywords = KeywordAssembler.Assemble([new CarKeywordSource(entry)], sessionName);

        return new GeneratedFields(headline, caption, altText, extDescription, keywords, entry.Drivers.Select(d => d.Name).Distinct(StringComparer.Ordinal).ToArray())
        {
            Session = session
        };
    }
}
