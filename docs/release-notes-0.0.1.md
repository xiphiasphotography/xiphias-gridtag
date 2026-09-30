# XiPHiAS GridTag 0.0.1 (beta) — pre-release

Eerste pre-release van XiPHiAS GridTag: nummerherkenning op motorsportfoto's, validatie tegen de
entrylist en het wegschrijven van IPTC-metadata in Lightroom Classic.

**Let op:** dit is een beta. De vision-modellen zijn nog niet getraind/gekoppeld, dus zonder
model-configuratie levert `gridtag run` per foto `error` met reden `no_preview` op.

## Wat zit erin

### CLI (`gridtag`)
- `run`, `preview`, `eval`, `check-entrylist`, `fields`, `version`.
- Exit codes volgens contract: `0` ok · `1` onverwachte fout · `2` usage-fout · `3` ongeldig of ontbrekend invoerbestand.
- Per-foto fouten laten de run niet mislukken; ze worden `status: "error"`.
- `results.json` wordt als UTF-8 **zonder BOM** geschreven.

### Domeinlogica
- `EntryList` + `EntryListLoader`: UTF-8 met BOM, `;`-gescheiden, quoted cellen, `driver_N`-kolommen, normalisatie van nummers (`#`, leading zeros).
- `FieldBuilder` met de Nederlandse standaardtemplates, `SessionResolver` voor sessiebepaling opnametijd, en `KeywordAssembler` die de keyword-volgorde (team, auto, rijders, `#nr`, sessie, classes) op één plek vastlegt.
- `NumberMatcher`: lexicaal decoderen van n-best hypotheses, evidence-weging in `[0, 1.5]`, en review-redenen (`low_confidence`, `small_margin`, `out_of_list_mass`, `confusable:…`, `substring_risk:…`, `evidence_conflict:…`).
- `TaggingPipeline`: manueel pad eerst, daarna automatisch pad met de regel dat de grootste detectie moet resolven.

### Lightroom Classic-plugin (Lua)
- Verzamelt de Picks, schrijft `manifest.json`, roept de CLI aan en past `results.json` toe op de catalogus.
- Schrijft uitsluitend de velden die XiPHiAS GridTag bezit, in korte write-gates (chunks).
- Handmatige correctie via het custom veld `manualNumber`.
- Review-collecties voor `review`, `noCar` en `error`.

## Belangrijkste wijzigingen sinds de vorige commit

- Ontbrekende preview wordt nu `status: "error"` met reden `no_preview` in plaats van `review`.
- Evidence wordt altijd in één matcher-call geëvalueerd, zodat een conflict met auto-model/logo de foto naar `review` dwingt in plaats van stil een nummer te accepteren.
- `session.json` wordt gevalideerd; een ontbrekende of onparseerbare sleutel geeft `InvalidDataException` met bestandsnaam en sleutel, dus exit code 3.
- Vision-interfaces gebruiken `IPreview` in plaats van `object`.
- `NoSessionDefault()` levert nu daadwerkelijk de sessie-loze templates.

## Tests

- 62 unit tests (`dotnet test XiPHiAS.GridTag.slnx`) — groen, 0 warnings.
- Golden XMP-tests voor `#3` en `#69`.
- Nieuwe Lua-testharness (`lua run_lua_tests.lua`) die `CatalogWriter` tegen gestubde `Lr*`-API's test: status/manual-bescherming, keyword-opruiming, chunking en het overleven van een falende `setRawMetadata`.

## Nog niet klaar

- Car detection en nummer-OCR achter `ICarDetector`/`IPlateReader` zijn stubs tot er modellen zijn gekoppeld; `gridtag eval` is er om die baseline te meten.
- Car-model- en logo-evidence zijn nog niet geïmplementeerd.
- Openstaande Lightroom SDK-aannames (`personShown` met meerdere rijders, Windows-quoting van `LrTasks.execute`, collection-API's) staan in `docs/open-questions.md` en moeten in LrC getest worden.

## Installatie

1. `dotnet build XiPHiAS.GridTag.slnx`
2. Plugin-map `lightroom/XiPHiAS.GridTag.lrdevplugin` in Lightroom Classic toevoegen via Bestand → Plug-inbeheer.
3. CLI-pad en chunkgrootte instellen in de plug-invoorkeuren.

Vereist .NET 10 en (voor de plugin) Lightroom Classic 6.0 of nieuwer.
