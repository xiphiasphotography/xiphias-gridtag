# Open questions

## Input and documentation discrepancies

- The session sample and AGENTS.md section 6 session example omit schemaVersion,
  while section 3 requires it on every JSON exchange file. Resolve before task 3
  introduces EventContext; the sample has not been changed.
- The sample CSV puts car/class after the driver columns, whereas section 6 lists
  them before drivers. Header-based loading has been proposed to the owner.
- The original README allows lowering the target framework for older SDKs.
  AGENTS.md and the owner's explicit instruction require net10.0; no downgrade
  is allowed. SDK 10.0.401 is now installed.

## Lightroom SDK: still unverified

No Lightroom Classic instance was available for task 7. Retain these checks
from AGENTS.md for manual Lightroom testing; the checklist is in
`docs/lightroom-test-plan.md`:

- Multiple drivers in personShown: separator behavior (default proposal: comma + space).
- LrTasks.pcall as a yield-safe wrapper for LrTasks.execute.
- Windows command quoting, including paths with spaces.
- createCollection(name, nil, true) and collection addPhotos/removePhotos behavior.
- Loading when LrSdkVersion exceeds the running Lightroom SDK version.
- Write-gate chunking and review collections with hundreds of photos.
- `photo:setRawMetadata` support for accessibility and custom metadata fields
  on the owner's installed Lightroom SDK.

## Metadata-schema laden — 11 oktober 2026

Eigenaar meldt dat Lightroom de plug-in uitschakelt bij het lezen van het schema.
Hersteld conform Adobe SDK Guide hoofdstuk 4: `schemaVersion`,
`metadataFieldsForPhotos`, enum-tabellen met `value`/`title`, geen ongeldig
`double`-type; tagset via `LrMetadataTagsetFactory`, `items` en gekwalificeerde veld-ID's.
Bron: https://archive.stecman.co.nz/files/docs/lightroom-sdk/Manual/Lightroom%20SDK%20Guide.pdf
Lua 5.1-syntax en schema-regressietest zijn lokaal gecontroleerd.
Nog te bevestigen in Lightroom Classic: bijgewerkte map laadt zonder schemafout
 en het paneel XiPHiAS GridTag toont de acht velden, inclusief handmatig nummer.
De bestaande algemene Lua-testharness heeft een syntaxfout in de foto-array;
de geïsoleerde schema-regressietest staat in `tests/GridTag.Lua.Tests/test_metadata.lua`.

## Bestandskiezer in instellingen — 11 oktober 2026

Drie Bladeren-knoppen gebruiken de gedocumenteerde LrDialogs.runOpenPanel met één bestand en EXE/CSV/JSON-filter. De view bindt expliciet aan prefs. Lua 5.1-syntax gecontroleerd; nog in Lightroom te bevestigen: de kiezer opent vanuit het instellingenvenster en de gekozen locatie verschijnt in het tekstveld. Annuleren moet het bestaande pad behouden.
SDK-bron: https://archive.stecman.co.nz/files/docs/lightroom-sdk/API-Reference/modules/LrDialogs.html

## Fotoselectie — 11 oktober 2026

Eigenaar meldde een nil-method-fout voor getMultipleSelectedPhotos. Run en Manifest gebruiken nu getTargetPhotos, voorafgegaan door getTargetPhoto om de filmstrip-fallback bij geen selectie te voorkomen. Manifest en resultaten gebruiken dezelfde selectiesnapshot. Lua-regressietest bevestigt één/meerdere/geen selectie en Picks-filtering. Nog in Lightroom te bevestigen: beide menuacties starten correct.
SDK-bron: https://archive.stecman.co.nz/files/docs/lightroom-sdk/API-Reference/modules/LrCatalog.html

## Eigen metadata — 11 oktober 2026

Eigenaar meldt Unknown key: manualNumber. Manifest leest manualNumber nu via getPropertyForPlugin. CatalogWriter leest status en schrijft status/number/confidence/reasons/session/toolVersion via de plug-in-API, binnen de bestaande write-gate. Standaard IPTC-velden blijven setRawMetadata gebruiken. Strikte Lua-regressietest bevestigt handmatige invoer, statusopslag en behoud van manual-status. Nog in Lightroom te bevestigen: verwerking en zichtbaarheid van eigen metadata.

## Picks-fallback en voorkeuren — 11 oktober 2026

Op verzoek van de eigenaar gebruikt automatische verwerking zonder selectie de huidige filmstrip en filtert op Picks. De automatische menuactie vereist geen selectie meer; handmatig blijft selectie vereisen. De eerdere notitie over geen verwerking zonder selectie is hiermee vervangen. Settings gebruikt een LrBinding-propertytable en kopieert expliciet naar preferences bij Opslaan; Annuleren bewaart eerdere waarden. Prefs-defaults worden nu daadwerkelijk geladen. Lua-tests voor selectie, opslag/annuleren, schema en eigen metadata slagen. Nog in Lightroom te testen: paden blijven bij heropenen en herstart behouden; automatische actie vindt Picks zonder selectie.
SDK-bron: https://archive.stecman.co.nz/files/docs/lightroom-sdk/API-Reference/modules/LrBinding.html

## Tijdelijke werkmap — 11 oktober 2026

De ongeldige getTempDirectory-aanroep is vervangen door de gedocumenteerde LrPathUtils.getStandardFilePath('temp'). De Run-regressietest met strikte SDK-stubs doorloopt werkmap, manifest, CLI-start en resultaattoepassing. Lua 5.1-syntax gecontroleerd. De echte CLI-start blijft in Lightroom te bevestigen, inclusief de eerder genoteerde Windows-quoting.
SDK-bron: https://archive.stecman.co.nz/files/docs/lightroom-sdk/API-Reference/modules/LrPathUtils.html

## CLI-start Windows — 11 oktober 2026

Eigenaar meldt exitcode 1 vanuit Lightroom. De directe CLI verwerkt het gevonden drie-CR2-manifest succesvol met de sample-entrylist en sessie. Dit bewijst geen modelherkenning (geen vision-opties in deze proef). Run omsluit de Windows-opdracht nu met een extra paar aanhalingstekens en schrijft stdout/stderr naar cli.log in de tijdelijke werkmap. Bij fouten toont de melding maximaal 4096 bytes plus logpad. De Lua-test controleert Windows-omsluiting en redirect. Nog in Lightroom te bevestigen: echte CLI-start met de eigenaarinstellingen.

## Collecties beschikbaar na commit — 11 oktober 2026

Eigenaar meldt nil bij GridTag GeenAuto. De SDK vermeldt dat nieuwe collecties pas na afloop van de write-gate toegankelijk zijn. CatalogWriter bereidt nu beide collecties voor in een aparte gate en zoekt ze daarna opnieuw op via getChildCollections/getName. Bestaande collecties worden hergebruikt, membership blijft in de metadata-gates. Lua-regressietest simuleert uitgestelde beschikbaarheid en controleert statuswissels en hergebruik. Nog in Lightroom te bevestigen: drie testfoto's worden zonder fout in de juiste collectie geplaatst.
SDK-bron: https://archive.stecman.co.nz/files/docs/lightroom-sdk/API-Reference/modules/LrCatalog.html

## Lightroom-herkenning aangesloten — 11 oktober 2026

Run geeft bij automatische verwerking YOLOX-config, CPU-provider en de drie PaddleOCR-paden mee. Herkenningsmap is configureerbaar met mapkiezer; default is de repository boven de plug-in. Geen drempel/modelwijziging, geen extra evidence. Handmatig blijft zonder vision. Lua 5.1-syntax en command-/instellingen-tests slagen. Lokale echte CLI-run op het drie-CR2-manifest eindigt met drie review-resultaten (largest_car_unresolved), geen fouten of automatische metadata. Resultaat: work/lightroom-recognition-results.json. Nog te bevestigen in Lightroom: modelrun voltooit en de drie testfoto's komen in GridTag Review.

## Copy Name voor export — 11 oktober 2026

Op expliciet verzoek schrijft CatalogWriter copyName = fields.headline bij auto/manual. SDK ondersteunt setRawMetadata('copyName', value). Lua-tests controleren beide statussen, idempotentie, reviewbehoud en manualbescherming. Een mislukte Copy Name-write breekt de write-gate af en wordt gemeld. Nog in Lightroom te bevestigen: Copy Name wordt gevuld en het exportsjabloon Copy Name-Filename-Custom Text geeft het gewenste JPG-pad. Exportgrootte is een label in het exportpreset, geen herkenningsparameter.

## Title en Copy Name — 11 oktober 2026

Eigenaar bevestigt dat Copy Name wordt gevuld. Title wordt nu eveneens via setRawMetadata met fields.headline gevuld bij auto/manual; bij schrijffouten wordt de write-gate afgebroken. Lua-regressietest bevestigt Title = headline. Nog in Lightroom te bevestigen: opnieuw verwerken vult Title en Ctrl+S schrijft dc:title naar de RAW-sidecar.

## OCR-diagnostiek — 11 oktober 2026

Optioneel JSONL-log via run --debug-log, standaard ingeschakeld door Lightroom. Build zonder waarschuwingen; 186 .NET-tests slagen plus Lua-syntax en Run-stub. Echte GTWC-proef: drie 160x120 RAW-previews, autodetecties aanwezig, OCR-hypothesen leeg. RAW-previewkeuze moet worden onderzocht; geen drempels gewijzigd. Porsche-batch nog niet geanalyseerd. Lightroom-logpad wordt in de voltooiingsmelding getoond.

## Grote ingebedde RAW-preview — 11 oktober 2026

Previewextractor zoekt alle JPEG-kandidaten, parseert marker/segmentgrenzen inclusief entropy stuffing en selecteert de grootste GDI-decodeerbare op pixeloppervlak. Ongeschikte lossless RAW-JPEGs/corrupte kandidaten tellen niet mee. XIP_3033: 5472x3648 ipv160x120. Dezelfde 12 Porsche-CR2's/entrylist/sessie/modelparameters: voor 12 review met lege OCR; na 10 auto en 2 review. Dit is beslisgedrag, geen precisionclaim: geen ground-truth voor deze batch. Outputs work/porsche-large-results.json en work/porsche-large-debug.jsonl. Bronnen/catalogus ongemoeid. 187 tests slagen; build nul waarschuwingen. Groot RAW-bestand wordt voor JPEG-scan tijdelijk in geheugen gelezen; volledige previews kosten meer OCR-tijd. Lightroom-herhaling met bijgewerkte CLI nog te bevestigen.

## Voortgang — 11 oktober 2026

Run toont showModalProgressDialog met functionContext en cannotCancel, monitort progress.json vanuit een tweede async task. CLI run --progress-file schrijft atomisch completed/total na elke foto, inclusief errors/manual; geen wijziging van results-contract. Na herkenning volgt metadatafase; context sluit voortgang ook bij fouten. 187 .NET-tests en Lua-syntax/Run-stub slagen. Nog in Lightroom te bevestigen: live venster en teller tijdens echte externe CLI-run.
SDK-bron: https://archive.stecman.co.nz/files/docs/lightroom-sdk/API-Reference/modules/LrProgressScope.html

## Assertion na voortgangswijziging — 11 oktober 2026

Eigenaar meldt assertion failed zonder locatie. Exacte oorzaak niet vastgesteld. De modale voortgangsdialoog en geneste callWithContext zijn vervangen door standaard LrProgressScope met de oorspronkelijke postAsyncTaskWithContext-context uit beide menu-entrypoints. Teller en CLI-progress-file blijven werken. Lua-syntax/Run-stub slagen. Nog in Lightroom te bevestigen: geen assertion en teller linksboven zichtbaar. Dit vervangt de eerdere modale UI-notitie.

## Foto-index in voortgang — 11 oktober 2026

Voortgangsbestand bevat current en fileName voor de volgende/in uitvoering zijnde foto. Lightroom-caption toont Foto x/x — bestandsnaam; bij afgeronde herkenning x/x verwerkt. 187 .NET-tests en Lua-controles slagen. Nog te bevestigen in Lightroom: de caption is zichtbaar bij de voortgangsbalk.

## Assertion na CLI — 11 oktober 2026

Logs GridTag-1791714954 tonen geslaagde CLI: 12 resultaten, 10 auto/2 review. Exacte Lightroom-assertion niet gelokaliseerd. Nieuwe trefwoorden worden nu in een afzonderlijke write-gate voorbereid voordat ze aan foto's worden gekoppeld, conform de SDK-beschikbaarheidsregel. plugin-debug.log legt resultaatvalidatie en catalogustoepassing/fouten vast. Lua-syntax en gerichte stubs slagen; echte Lightroom-herhaling nodig om de oorzaak te bevestigen.

## Stoppen — 11 oktober 2026

Lightroom-progress is annuleerbaar tijdens de CLI-run. Monitor schrijft cancel.request; CLI controleert tussen foto's en slaat geen resultaatbestand op na annuleren. Lightroom stopt voordat resultaten worden toegepast, ook bij late annulering. Tijdens toepassen is Stop uit. 187 .NET-tests slagen in geïsoleerde work/stop-check-bouwmap omdat huidige gridtag.exe/dll door actieve Lightroom-run vergrendeld zijn. Lua-syntax/Run-stub slagen. Nieuw uitvoerbaar bestand: work/stop-check/bin/XiPHiAS.GridTag.Cli/debug/gridtag.exe. Normale build bijwerken zodra actieve run afgelopen is; daadwerkelijke stop in Lightroom nog te bevestigen.

## Stop met behoud afgeronde foto's — 11 oktober 2026

Op verzoek schrijft CLI na Stop gedeeltelijke resultaten van de afgeronde prefix, inclusief de huidige foto. Lightroom valideert die exacte prefix en past auto/manual-metadata en reviewcollecties daarvan toe; rest blijft ongemoeid. Volledige manifestvalidatie blijft verplicht zonder stop. Geen burstpropagatie op onvoltooide batch. 187 .NET-tests plus Lua-prefixcontrole/syntax/Run-stub slagen. Dit vervangt de eerdere geen-metadata-bij-stop-afspraak. Nieuwste CLI staat in work/stop-check/bin/XiPHiAS.GridTag.Cli/debug/gridtag.exe; echte Lightroom-stop nog te bevestigen.
