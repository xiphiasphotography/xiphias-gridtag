using System.Xml.Linq;
using GridTag.Core;
using Xunit;

namespace GridTag.Core.Tests;

public sealed class FieldBuilderGoldenTests
{
    [Fact]
    public void BuildsFieldsForMercedesEntryMatchingGoldenXmpReference()
    {
        var entry = new Entry(
            "3",
            "Mercedes - AMG Team Verstappen Racing",
            "Mercedes-AMG GT3 EVO",
            "PRO",
            [
                new Driver("Dani Juncadella", "ESP"),
                new Driver("Chris Lulham", "GBR")
            ]);

        var context = EventContext.Default(
            "GT World Challenge Europe",
            "GT World Challenge Europe powered by AWS",
            "Circuit Zandvoort",
            "FP2",
            [
                new EventSession("FP2", "Free Practice 2", new DateTimeOffset(2026, 9, 18, 13, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 18, 15, 0, 0, TimeSpan.Zero))
            ]);

        var fields = new FieldBuilder().Build(entry, context, "FP2");
        var xdoc = XDocument.Load(GetReferencePath("003-Mercedes_-_AMG_Team_Verstappen_Racing.xmp"));

        Assert.Equal("#3 Mercedes - AMG Team Verstappen Racing Mercedes-AMG GT3 EVO", fields.Headline);
        Assert.Equal("Free Practice 2 - Dani Juncadella (ESP) en Chris Lulham (GBR) rijden de Mercedes-AMG GT3 EVO met startnummer #3 voor Mercedes - AMG Team Verstappen Racing tijdens de GT World Challenge Europe powered by AWS op Circuit Zandvoort.", fields.Caption);
        Assert.Equal("Raceauto nummer 3 van Mercedes - AMG Team Verstappen Racing in actie op Circuit Zandvoort tijdens de GT World Challenge Europe.", fields.AltText);
        Assert.Equal("De Mercedes-AMG GT3 EVO met startnummer 3 van Mercedes - AMG Team Verstappen Racing rijdt op Circuit Zandvoort tijdens Free Practice 2 van de GT World Challenge Europe.", fields.ExtDescription);
        Assert.Equal(new[] { "Mercedes - AMG Team Verstappen Racing", "Mercedes-AMG GT3 EVO", "Dani Juncadella", "Chris Lulham", "#3", "Free Practice 2", "PRO" }, fields.Keywords);
        Assert.Equal(new[] { "Dani Juncadella", "Chris Lulham" }, fields.Persons);
        Assert.Equal(GetPhotoshopString(xdoc, "Headline"), fields.Headline);
        Assert.Equal(GetDescription(xdoc), fields.Caption);
        Assert.Equal(GetIptcString(xdoc, "AltTextAccessibility"), fields.AltText);
        Assert.Equal(GetIptcString(xdoc, "ExtDescrAccessibility"), fields.ExtDescription);
        Assert.Equal(GetPersonInImage(xdoc), fields.Persons);
        Assert.Equal(fields.Keywords, GetSubjectTail(xdoc, fields.Keywords.Length));
    }

    [Fact]
    public void BuildsFieldsForEmilFreyEntryMatchingGoldenXmpReference()
    {
        var entry = new Entry(
            "69",
            "Emil Frey Racing",
            "Ferrari 296 GT3 EVO",
            "PRO",
            [
                new Driver("Thierry Vermeulen", "NED"),
                new Driver("Ben Green", "GBR")
            ]);

        var context = EventContext.Default(
            "GT World Challenge Europe",
            "GT World Challenge Europe powered by AWS",
            "Circuit Zandvoort",
            "FP2",
            [
                new EventSession("FP2", "Free Practice 2", new DateTimeOffset(2026, 9, 18, 13, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 18, 15, 0, 0, TimeSpan.Zero))
            ]);

        var fields = new FieldBuilder().Build(entry, context, "FP2");
        var xdoc = XDocument.Load(GetReferencePath("069-Emil_Frey_Racing.xmp"));

        Assert.Equal("#69 Emil Frey Racing Ferrari 296 GT3 EVO", fields.Headline);
        Assert.Equal("Free Practice 2 - Thierry Vermeulen (NED) en Ben Green (GBR) rijden de Ferrari 296 GT3 EVO met startnummer #69 voor Emil Frey Racing tijdens de GT World Challenge Europe powered by AWS op Circuit Zandvoort.", fields.Caption);
        Assert.Equal("Raceauto nummer 69 van Emil Frey Racing in actie op Circuit Zandvoort tijdens de GT World Challenge Europe.", fields.AltText);
        Assert.Equal("De Ferrari 296 GT3 EVO met startnummer 69 van Emil Frey Racing rijdt op Circuit Zandvoort tijdens Free Practice 2 van de GT World Challenge Europe.", fields.ExtDescription);
        Assert.Equal(new[] { "Emil Frey Racing", "Ferrari 296 GT3 EVO", "Thierry Vermeulen", "Ben Green", "#69", "Free Practice 2", "PRO" }, fields.Keywords);
        Assert.Equal(new[] { "Thierry Vermeulen", "Ben Green" }, fields.Persons);
        Assert.Equal(GetPhotoshopString(xdoc, "Headline"), fields.Headline);
        Assert.Equal(GetDescription(xdoc), fields.Caption);
        Assert.Equal(GetIptcString(xdoc, "AltTextAccessibility"), fields.AltText);
        Assert.Equal(GetIptcString(xdoc, "ExtDescrAccessibility"), fields.ExtDescription);
        Assert.Equal(GetPersonInImage(xdoc), fields.Persons);
        Assert.Equal(fields.Keywords, GetSubjectTail(xdoc, fields.Keywords.Length));
    }

    private static string GetReferencePath(string fileName)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        return Path.Combine(repoRoot, "docs", "reference", fileName);
    }

    private static string GetPhotoshopString(XDocument document, string name)
    {
        var ns = XNamespace.Get("http://ns.adobe.com/photoshop/1.0/");
        return document.Root!
            .Descendants()
            .Attributes()
            .First(x => x.Name.LocalName == name && x.Name.Namespace == ns)
            .Value;
    }

    private static string GetDescription(XDocument document)
    {
        var ns = XNamespace.Get("http://purl.org/dc/elements/1.1/");
        return document.Root!
            .Descendants(ns + "description")
            .Elements()
            .SelectMany(x => x.Elements())
            .First(x => x.Name.LocalName == "li")
            .Value;
    }

    private static string GetIptcString(XDocument document, string name)
    {
        var ns = XNamespace.Get("http://iptc.org/std/Iptc4xmpCore/1.0/xmlns/");
        return document.Root!
            .Descendants(ns + name)
            .Elements()
            .SelectMany(x => x.Elements())
            .First(x => x.Name.LocalName == "li")
            .Value;
    }

    private static string[] GetPersonInImage(XDocument document)
    {
        var ns = XNamespace.Get("http://iptc.org/std/Iptc4xmpExt/2008-02-29/");
        return document.Root!
            .Descendants(ns + "PersonInImage")
            .Elements()
            .Where(x => x.Name.LocalName == "Bag")
            .Elements()
            .Where(x => x.Name.LocalName == "li")
            .Select(x => x.Value)
            .ToArray();
    }

    private static string[] GetSubjectTail(XDocument document, int count)
    {
        var ns = XNamespace.Get("http://purl.org/dc/elements/1.1/");
        var values = document.Root!
            .Descendants(ns + "subject")
            .Elements()
            .Where(x => x.Name.LocalName == "Bag")
            .Elements()
            .Where(x => x.Name.LocalName == "li")
            .Select(x => x.Value)
            .ToArray();
        return values.TakeLast(count).ToArray();
    }
}
