# Standalone-functionaliteit — overdracht

Vastgelegd op 2 oktober 2026. Repository: `xiphias-gridtag`.

## Opdracht en afspraken

Voeg de standalone-functionaliteit uit `AGENTS.md` toe. `AGENTS.md` is leidend.
Lees vóór verdere implementatie het volledige bestand, README en alle bestanden
onder `docs/` en `samples/`. Meld ambiguïteiten en conflicten aan de eigenaar;
los die niet op door zelf aannames te maken. Rapporteer in het Nederlands.

Het onderstaande voorstel is **gedeeltelijk goedgekeurd**; zie de aangevinkte keuzes.
De opdracht om deze TODO te maken geldt niet als toestemming voor de keuzes.

## Eerst bevestigen met de eigenaar

- [ ] Concrete bestandsadapters onder **Core/IO**, volgens AGENTS §5.
  Conflict: §4 zegt dat adapters niet in Core/Vision mogen terechtkomen,
  terwijl §5 concrete adapters en de XMP-writer onder Core/IO plaatst.
- [x] Niet-GridTag XML-inhoud en waarden exact behouden; **XML-opmaak mag
  veranderen** door herserialisatie met `XDocument`.
  Goedgekeurd door de eigenaar op 3 oktober 2026, met behoud van bruikbaarheid
  in Photo Mechanic en Lightroom. Dit is de expliciet toegestane afwijking van
  het byte-voor-byte behoud in AGENTS §12/§13. Bestaande XMP-namespaces, waarden,
  overige keywords, PM-velden en Lightroom `crs:`-ontwikkelinstellingen blijven
  behouden, evenals encoding/BOM. GridTag-metadata gebruikt de standaardvelden;
  eigen tracking mag in een aparte GridTag-namespace. Compatibiliteit moet met
  fixtures en later met beide echte applicaties worden gecontroleerd.
- [ ] Eerst standalone mapverwerking, `review.csv`, handmatige nummers en
  `--apply`; **`--stints` volgt samen met driverresolutie uit taken 12/13**.
  Dit is een voorgestelde fasering, geen volledige uitvoering van taak 11:
  die noemt ook `--stints`. Die optie mag niet stilzwijgend worden genegeerd.

## Huidige voortgang

- [x] Stap 0: AGENTS, README, alle 19 docs-bestanden (inclusief zes XMP's)
  en alle 10 samples-bestanden volledig gelezen.
- [x] `src/XiPHiAS.GridTag.Core/PhotoIO.cs` toegevoegd met `IPhotoSource`
  en `IResultSink`.
- [x] Overload `TaggingPipeline.Process(IPhotoSource, IResultSink)` toegevoegd.
  Deze leest de batch, gebruikt de bestaande manifestpipeline en stuurt de
  resultaten naar de sink. Bestaande manifestverwerking blijft beschikbaar.
- [x] `tests/XiPHiAS.GridTag.Core.Tests/PhotoIOTests.cs` toegevoegd:
  controleert behoud van de handmatige metadata via de nieuwe route.
- [x] Gerichte test geslaagd, 1 test:

  ```powershell
  dotnet test tests/XiPHiAS.GridTag.Core.Tests/XiPHiAS.GridTag.Core.Tests.csproj --no-restore --filter FullyQualifiedName~PhotoIOTests
  ```

Nog geen concrete standalone-adapters, XMP-writer of `tag`-commando toegevoegd.
Na deze voorbereidende codewijziging is nog geen volledige solution-build of
volledige testsuite gedraaid. De eerdere WPF-reviewapp bestaat al en blijft een
alleen-lezen viewer voor resultaten en manifesten.

## Implementatie na bevestiging

- [ ] `ManifestPhotoSource` en `JsonResultSink` maken en `run` daarop aansluiten.
  Bestaande CLI-tests en resultaatcontracten moeten ongewijzigd blijven werken.
- [ ] `FolderPhotoSource`: recursief RAW/JPEG vinden; stabiele volgorde op pad;
  EXIF-opnametijd lezen via de bestaande `ExifCaptureTimeReader` in Vision.
  Core blijft BCL-only; EXIF-toegang passend injecteren/isolereren.
- [ ] `--manual` CSV op pad koppelen. Ondersteun `path;numbers` en de extra
  `reasons`-kolom uit `review.csv`; behoud voorloopnullen en meerdere nummers.
- [ ] `ReviewCsvResultSink`: altijd output voor niet-`auto`/`manual` resultaten,
  met `path;numbers;reasons` en lege nummers om handmatig in te vullen.
  Voorkom dat een uitvoerpad de gebruikte invoer-CSV of andere bronbestanden
  onbedoeld overschrijft.
- [ ] `XmpSidecarWriter` en sink: uitsluitend bij `--apply`; alleen de zes
  GridTag-veldgroepen schrijven; read-merge-write en keywordtracking;
  PM-velden, overige keywords en `crs:`-ontwikkelinstellingen behouden.
  Encoding/BOM van bestaande sidecars behouden; veilig en idempotent schrijven.
- [ ] Handmatige overrides beschermen bij latere automatische runs; per-foto
  schrijfproblemen zichtbaar maken en andere foto's blijven verwerken.
- [ ] `gridtag tag --folder --entrylist [--session] [--manual] [--review-out]
  [--apply]` toevoegen. Zonder `--apply` geen sidecars schrijven; wel review-CSV
  en statusoverzicht. Gebruik dezelfde herkenningspipeline als `run`.
- [ ] `--stints` volgens de bevestigde scope implementeren of expliciet als
  nog niet ondersteund afwijzen. Niet stilzwijgend accepteren.
- [ ] Tests voor discovery, CSV-roundtrip, dry run, handmatige nummers,
  eigenaarschap, encoding, nieuwe/bestaande sidecars, idempotentie en fouten.
  End-to-end: twee `tag`-runs op dezelfde fixturemap.
- [ ] README en docs actualiseren; contractwijzigingen eerst bespreken.
- [ ] `dotnet build XiPHiAS.GridTag.slnx` en
  `dotnet test XiPHiAS.GridTag.slnx` uitvoeren; geen nieuwe warnings.

## Andere gemelde onduidelijkheden

- AGENTS vereist `schemaVersion` op alle JSON-bestanden; session-, detector-
  en nummerlezerconfiguraties missen die, net als het stintvoorbeeld in AGENTS.
  Niet zonder toestemming de bestaande contracten veranderen.
- README/architectuurdocumentatie beschrijven nog Lightroom-only verwerking.
- Bestaande rijdernaam-evidence beïnvloedt nummermatching; de nieuwe
  driverresolutie uit AGENTS moet daarvan onafhankelijk zijn. De verhouding
  tussen beide is nog niet expliciet bepaald.
- Driver-board confidencegrenzen en overlappende/aansluitende stintvensters
  moeten worden verduidelijkt als taken 12/13 worden meegenomen.
- AGENTS noemt 45 entrylistregels; de huidige fixture heeft 46.
- Sample-resultaat 1001 heeft `error`, maar ook herkenningsvelden zonder foutreden.
- Architectuur-/contractdocumentatie en het README-testaantal zijn deels verouderd.

## Git en thuis verdergaan

De werkmap bevat veel bestaande, niet-gecommitte wijzigingen, waaronder
visionwerk en de WPF-reviewapp. Bewaar die; reset of overschrijf ze niet.
De eigenaar heeft daarna opdracht gegeven de reviewapp, bijgewerkte AGENTS,
deze TODO en standalone-voorbereiding te committen en pushen. Die selectie is
apart geëxporteerd en gecontroleerd: solution-build met 0 warnings/errors,
71 tests geslaagd (61 Core, 10 CLI). Eerdere vision-, burst- en timingwijzigingen
vallen buiten deze overdrachtscommit en blijven lokaal; de documentatie hierboven
beschrijft deels die uitgebreidere werkmap. Neem die wijzigingen niet automatisch
aan als aanwezig na een pull. Voeg geen foto's, modelgewichten, buildoutput of
andere gegenereerde bestanden toe.

Voorstel voor de volgende prompt:

> Lees TODO.md en AGENTS.md volledig. Bespreek eerst de drie openstaande
> keuzes onder 'Eerst bevestigen met de eigenaar'. Voer daarna de goedgekeurde
> standalone-scope uit, met behoud van de bestaande wijzigingen en workflows.

## Controle bij hervatting

Op 2 oktober 2026 zijn AGENTS.md, TODO.md, README en de huidige tekstbestanden
onder docs/ en samples/ opnieuw gelezen, inclusief de zes XMP-referenties.
De bestaande standalone-voorbereiding is volledig gecontroleerd:

- `dotnet build XiPHiAS.GridTag.slnx --no-restore`: geslaagd, 0 warnings/errors.
- `dotnet test XiPHiAS.GridTag.slnx --no-build --no-restore`: 176 tests geslaagd
  (149 Core, 27 CLI).

Bij deze controle waren de drie keuzes onder 'Eerst bevestigen met de eigenaar' voorgelegd en
nog open. De actuele goedkeuring staat bij de betreffende keuze hierboven. Concrete adapters, XMP-writer en
het tag-commando zijn in deze hervatting nog niet geïmplementeerd.
