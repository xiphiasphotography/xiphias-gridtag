# XiPHiAS GridTag

Visuele evidence voor automodel en coureursnaam kan per autodetectie worden aangesloten
via `VisionEvidenceFactory.Create` en `TaggingPipeline.visualEvidenceProvider`. De readers
worden uitsluitend bij een onzekere, in de entrylist aanwezige nummerkandidaat aangeroepen.
Concrete lokale ONNX-implementaties zijn beschikbaar; getrainde modelgewichten ontbreken nog. Zie
[evidence en metingen](docs/evidence-burst-timing.md).

`run` en `eval` ondersteunen `--car-model model.onnx --car-model-labels labels.txt`
en `--driver-name-model reader.onnx --driver-name-alphabet alphabet.txt`.
JPEG-input wordt op oorspronkelijke resolutie gelezen, zodat kleine namen en nummers
niet door thumbnailselectie of verkleining verloren gaan.

De productnaam is XiPHiAS GridTag; C#-projecten en namespaces gebruiken `XiPHiAS.GridTag.*`.
Het CLI-command blijft `gridtag` (`gridtag.exe`). De Lightroom-plugin staat in
`lightroom/XiPHiAS.GridTag.lrdevplugin`; wijs Plug-inbeheer na de hernoeming naar die map.
De toolkit-ID `net.xiphias.gridtag`, metadata-ID's en bestaande collectienamen
`GridTag Review` en `GridTag GeenAuto` blijven behouden, evenals de loggernaam `GridTag`
en tijdelijke padprefix `GridTag-`. Deze brandingwijziging verandert geen functionaliteit
of JSON-contracten.

XiPHiAS GridTag herkent startnummers op raceauto's in RAW-foto's, controleert de match tegen de entrylist en aanvullende visuele aanwijzingen zoals automerk/-model en logo's, en voegt daarna automatisch de juiste auto-specifieke IPTC-metadata toe in **Lightroom Classic**: headline, beschrijving, alt-tekst, keywords en rijdersnamen. XiPHiAS GridTag komt pas in actie nadat de foto's handmatig zijn geselecteerd en bewerkt.

> Status: **0.0.1 (beta)**. De CLI, domeinlogica en Lightroom-adapter werken en zijn getest; de vision-modellen (autodetectie en nummer-OCR) moeten nog worden gekoppeld. Zonder model-configuratie meldt elke foto `error` met reden `no_preview`. Zie `docs/release-notes-0.0.1.md`.

## Implementation status

The .NET 10 scaffold is available. Core provides entry-list loading and lookup,
field generation, number matching, JSON contracts, and the fake-backed tagging
pipeline. The CLI provides `run`, `check-entrylist`, `fields`, `version`, and
`eval`, with documented exit codes 0-3. The evaluation harness reports
precision, recall, review rate, reasons, confusions and timing without tuning
thresholds. The Lightroom adapter now contains the task 7 plugin workflow, but
its SDK-dependent behavior still requires the manual checks in
`docs/lightroom-test-plan.md`. Vision contains the local preview and optional
ONNX/DirectML detector/plate-reader adapters; model weights remain external.

## Evaluatie-data

Uit de sample-data valt geen meetbare testset te maken: er zijn geen foto's en
de paden in `samples/labels.example.csv` bestaan niet. Daarom zijn er twee
praktische routes:

- `work/labels.smoke.csv`: smoke-input voor de vier voorbeeldrijen. Dit test
  alleen of `gridtag eval` het formaat leest; verwacht bij de meting alleen
  `no_preview` als er geen echte foto's of providers beschikbaar zijn.
- `tools/make_labels.py`: maakt een echte `work/labels.csv` uit een eigen
  archief met RAW-bestanden en XMP-sidecars. Het script leest alleen en past
  geen foto's of sidecars aan. Het vereist Python 3.8+ en geen extra packages.

Gebruik:

```text
python tools/make_labels.py "D:\\Archief\\<map met RAW + sidecars van een eerder evenement>" ^
    --entrylist samples/entrylist.csv --max-per-number 12 --sample 400 --out work/labels.csv
```

Het script herkent nummer-keywords zoals `#69` in `dc:subject` en schrijft:

- `work/labels.csv`: foto's met precies één nummer-keyword;
- `work/labels.review.csv`: meerdere nummers of nummers die niet in de
  entrylist staan. Zet bij meerdere nummers de hoofdauto vooraan en verplaats
  de gecorrigeerde rij daarna naar `labels.csv`, omdat eval het eerste nummer
  als primair gebruikt;
- geen rij voor foto's zonder nummer-keyword. Gebruik alleen
  `--include-untagged` voor mappen waarvan zeker is dat er geen auto op staat;
  zulke rijen krijgen een leeg nummer.

Tips voor een bruikbare testset:

- gebruik een eerder evenement met de bijbehorende entrylist;
- gebruik `--max-per-number` zodat één auto de meting niet domineert;
- meng sessies, lichtomstandigheden en camerahoeken;
- gebruik foto's die niet in een trainingsset zitten;
- laat `work/` in `.gitignore` staan, omdat de bestanden lokale padinformatie
  bevatten.

Als sidecars een andere notatie gebruiken dan `#69`, pas dan de regex
`NUMBER_KEYWORD` bovenin `tools/make_labels.py` aan. Het script is getest op de
twee XMP-voorbeelden en afgeleide gevallen: één nummer, meerdere nummers,
geen nummer, onbekend nummer en een sidecar zonder RAW.

Verify with `dotnet restore XiPHiAS.GridTag.slnx`, `dotnet build XiPHiAS.GridTag.slnx`, and
`dotnet test XiPHiAS.GridTag.slnx`. No Lightroom or image files are needed for these checks.

## Workflow

1. **Photo Mechanic**: basis-IPTC per evenement/sessie (map `yyyy-mm-dd - event\raw`).
2. **FastStone Viewer**: selectie verplaatsen naar `yyyy-mm-dd - event`.
3. **Lightroom Classic**: zelf bewerken; zet daarna de definitieve beelden op **Pick**.
4. **XiPHiAS GridTag** (menu in Lightroom): alleen die Picks worden geanalyseerd en krijgen auto-specifieke metadata.
5. **Review in Lightroom**: foto's die niet automatisch lukken staan in de collectie `GridTag Review` (of `GridTag GeenAuto`). Typ daar zelf het nummer in het veld *Startnummer (handmatig)* en start "verwerk handmatige nummers".

Voor een aparte lokale weergave van `review`- en `noCar`-foto's kun je de bestaande
WPF-app starten met `dotnet run --project src/XiPHiAS.GridTag.Review`.
Open `results.json`; het naastgelegen `manifest.json` wordt automatisch gebruikt,
of kies het via **Kies manifest**. De app toont statusfilters, redenen, kandidaten
en JPEG/PNG/RAW-previews van de bronfoto's. Handmatige correcties blijven in
Lightroom; de app schrijft geen foto's of metadata. Zie de
[handleiding en controlelijst voor taak 10d](docs/task10d-review-app.md).
6. **Export** vanuit Lightroom.

XiPHiAS GridTag schrijft rechtstreeks in de Lightroom-catalogus. Er is dus geen "metadata opslaan" of "metadata lezen" nodig tussen de stappen, en er worden geen XMP-sidecars door de tool aangepast. XiPHiAS GridTag doet nadrukkelijk geen selectie, rating, beeldbewerking of export; dat blijft handwerk in de bestaande workflow.

## Hoe het werkt

```
Lightroom-plugin (Lua)                     gridtag.exe (.NET)
  verzamelt Picks                            leest manifest.json
  schrijft manifest.json  ───────────►       leest entrylist.csv + session.json
  start gridtag.exe                          per foto: sessie → preview → auto → nummer → evidence → validatie
  leest results.json      ◄───────────       schrijft results.json
  past metadata toe in de catalogus
```

- **Validatie tegen de entrylist:** de tool kiest uit de nummers die echt bestaan, in plaats van vrije OCR te vertrouwen. Verwarbare nummers (bijv. 59/66/69/96/99) en deelnummers (5 in 55) gaan naar review.
- **Nummer is primair, merk/model is controle:** een herkend automerk/-model kan een twijfelachtig nummer versterken of juist een conflict signaleren. Logoherkenning kan later als extra, zwakker bewijs worden gebruikt. Bij conflict gaat de foto naar review; XiPHiAS GridTag verzint nooit zelf een deelnemer.
- **Precisie boven recall:** liever een foto niet taggen dan verkeerd taggen.
- **Alles lokaal:** geen cloud, geen uploads.

Zie `AGENTS.md` voor de volledige regels, contracten en het matching-algoritme.


## Technische keuze

XiPHiAS GridTag bestaat bewust uit twee kleine, gescheiden delen:

- **C#/.NET (`gridtag.exe`)** bevat alle echte logica: RAW-preview, vision, entrylist, matching, confidence/evidence en veldgeneratie.
- **Lightroom Classic plug-in (Lua)** blijft een dunne adapter: Picks ophalen, `gridtag.exe` starten, resultaten lezen en metadata in de Lightroom-catalogus zetten.

De eigenaar programmeert voornamelijk in JavaScript en deels in C#. Daarom blijft de C#-code eenvoudig en expliciet opgebouwd. JavaScript/Node/Electron zijn geen runtime-onderdeel van XiPHiAS GridTag; er komt geen aparte webinterface of service bij zolang Lightroom zelf voldoende UI biedt.

## Wat XiPHiAS GridTag wel en niet schrijft

| XiPHiAS GridTag schrijft | Photo Mechanic / jij |
|---|---|
| Headline, beschrijving, alt-tekst, uitgebreide alt-beschrijving | Creator, credit, copyright, contactinfo |
| Keywords: team, auto, rijders, `#nummer`, sessie, klasse | Locatie, evenement, organisatie, algemene keywords |
| Rijders (Person Shown) | Rating, kleurlabel, ontwikkelinstellingen |

## Projectstructuur (doel)

```
AGENTS.md  README.md  XiPHiAS.GridTag.slnx  Directory.Build.props
docs/        contracts, architectuur, open vragen, reference/*.xmp (goede voorbeelden)
samples/     entrylist.csv, session.example.json, manifest/results voorbeelden
src/
  XiPHiAS.GridTag.Core     domein, entrylist, matching, veldgeneratie, pipeline
  XiPHiAS.GridTag.Vision   RAW-preview, autodetectie, nummerlezer; later merk/model- en logo-evidence
  XiPHiAS.GridTag.Cli      gridtag.exe
tests/XiPHiAS.GridTag.Core.Tests   xUnit, incl. golden tests op de twee XMP-voorbeelden
lightroom/XiPHiAS.GridTag.lrdevplugin   de Lightroom-plugin (Lua)
tools/       Python-scripts voor training (later)
```

## Vereisten

- Windows, **.NET SDK 10**. `TargetFramework` blijft `net10.0` in `Directory.Build.props`; installeer bij een oudere SDK de .NET 10 SDK.
- **Visual Studio Code** met de C# Dev Kit en een Lua-extensie (bijv. *Lua* van sumneko). Codex-extensie of Codex CLI.
- **Lightroom Classic**. Voor de alt-tekstvelden is SDK-versie 13.2 of nieuwer nodig, oudere versies slaan die velden over.
- Later voor de herkenning: bij voorkeur een GPU. De inference-backend blijft verwisselbaar (bijv. ONNX Runtime/DirectML of WinML).

## Snelstart (gebruiken)

### 1. Bouwen en testen
```text
dotnet restore XiPHiAS.GridTag.slnx
dotnet build   XiPHiAS.GridTag.slnx
dotnet test    XiPHiAS.GridTag.slnx
```

Verwacht: build met 0 warnings, 62 tests groen. Er zijn geen foto's of Lightroom nodig voor deze stap.

### 2. CLI gebruiken
```text
dotnet run --project src/XiPHiAS.GridTag.Cli -- version
dotnet run --project src/XiPHiAS.GridTag.Cli -- check-entrylist --entrylist samples/entrylist.csv
dotnet run --project src/XiPHiAS.GridTag.Cli -- fields --entrylist samples/entrylist.csv --session samples/session.example.json --number 69
dotnet run --project src/XiPHiAS.GridTag.Cli -- run --manifest samples/manifest.example.json --entrylist samples/entrylist.csv --session samples/session.example.json --out work/results.json
dotnet run --project src/XiPHiAS.GridTag.Cli -- preview --file "D:\foto.ARW" --out work/preview.jpg
dotnet run --project src/XiPHiAS.GridTag.Cli -- eval --labels work/labels.csv --entrylist samples/entrylist.csv --session samples/session.example.json
```

`fields` is de snelste manier om de gegenereerde metadata te controleren zonder foto's. `preview` schrijft de uit een RAW geëxtraheerde JPEG weg, zodat je kunt zien wat de vision-stappen krijgen.

Exit codes: `0` ok · `1` onverwachte fout · `2` usage-fout · `3` ongeldig of ontbrekend invoerbestand. Een fout op één foto laat de hele run niet mislukken; die foto krijgt `status: "error"`.

### 3. Vision-modellen koppelen (optioneel, voor `run`)

Zonder `--vision-config` en `--plate-config` gebruikt de CLI null-implementaties en levert `run` per foto `error`/`no_preview` op. Met modellen:

```text
dotnet run --project src/XiPHiAS.GridTag.Cli -- run --manifest work/manifest.json --entrylist samples/entrylist.csv --session work/session.json --out work/results.json --vision-config work/detector.json --plate-config work/plate.json
```

Modelgewichten blijven buiten de repository. Extra opties: `--timing-csv` en `--clock-offset` voor timing-controle.

Voor het lokale `Models/car-detection/yolox_s.onnx` gebruik je
`--vision-config samples/detector.yolox.json`. Deze configuratie kiest expliciet
`modelFormat: "yolox"`: BGR CHW met waarden 0–255, lineair verkleinen en padding
met 114 rechts/onder. De ruwe uitvoer `[1,8400,85]` krijgt eerst de YOLOX
grid/stride-decodering (strides 8, 16, 32). Daarna volgt GridTags bestaande
autoklassefilter en NMS. De standaard `generic`-modus blijft RGB 0–1 met
gecentreerde padding en verwacht al gedecodeerde boxcoördinaten.

De YOLOX-modus ondersteunt deze ruwe COCO P5-export; exports met ingebouwde
boxdecodering of een andere layout vereisen een andere adapter. De officiële
[YOLOX preprocessing](https://github.com/Megvii-BaseDetection/YOLOX/blob/main/yolox/data/data_augment.py)
en [ONNX demo](https://github.com/Megvii-BaseDetection/YOLOX/blob/main/demo/ONNXRuntime/onnx_inference.py)
zijn de referentie. GridTag gebruikt eigen bilineaire interpolatie; bit-identieke
OpenCV-resize-uitvoer is nog niet vastgesteld. GridTag past NMS alleen op de
autoklasse toe, terwijl de demo standaard klasse-onafhankelijke NMS gebruikt.
Een werkend detectiemodel vervangt geen startnummer-OCR: voor automatische
nummermatches is ook een passende nummerlezer nodig (`--plate-config` of
de hieronder beschreven PaddleOCR-adapter).

De lokale PaddleOCR-modellen kunnen nu ook startnummers lezen via
`--number-ocr`/`--number-ocr-model`/`--number-ocr-dictionary`. Automerk-evidence
(`--car-model-format stanford-imagenet`) en coureursnaam-evidence
(`--driver-name-detector`) worden alleen bij onzekere bekende nummerkandidaten
toegepast. De ResNet-normalisatie is voorlopig een expliciete aanname; de
classifier levert in deze modus merk-evidence uit straatwagenklassen.
Zie [taak 10a: modellen, opdrachten en echte voor/nameting](docs/task10a-evaluation.md).

De meting op `.training/images2` met de bijgewerkte entrylist staat in
[taken 10a/10b: images2-evaluatie](docs/task10-images2-evaluation.md).

Optionele timingcontrole bij `run` en `eval`: `--timing-csv passing-times.csv
--clock-offset 00:00:05`. De CSV heeft `number;time` met volledige datum/tijd;
de offset wordt bij de cameraklok opgeteld. Alleen onzekere bekende nummers
worden gecontroleerd; ontbrekende gegevens blijven neutraal. Zie
[taak 10c: timingcontrole](docs/task10c-timing-cross-check.md).

Optionele burstvoorstellen zijn beschikbaar bij `run` en `eval` met
`--burst-max-gap 2 --burst-similarity 0.95`. Een nabij, sterk gelijkend frame kan
een nummer als **review-kandidaat** krijgen, nooit als auto-tag of IPTC-fields.
Voorstellen worden niet verder doorgegeven. `007` blijft verschillend van `7`.
Ontbrekende EXIF-tijden leveren bij `eval` geen burstvoorstellen op. Zie
[taak 10b: burstpropagatie en verificatie](docs/task10b-burst-propagation.md).

### 4. Plugin in Lightroom Classic
1. **Bestand → Plug-inbeheer → Toevoegen** → kies `lightroom/XiPHiAS.GridTag.lrdevplugin`.
2. **Bibliotheek → Plug-in-extra's → XiPHiAS GridTag: instellingen…**: vul het pad naar de CLI (`gridtag.exe` of `dotnet <pad>\gridtag.dll`), `entrylist.csv` en `session.json`, plus de chunkgrootte.
3. Bewerken en selecteren zoals altijd, zet de definitieve beelden op **Pick**.
4. Selecteer de foto's en gebruik **XiPHiAS GridTag: tag Picks**.
5. Foto's die niet automatisch lukken staan in `GridTag Review` (of `GridTag GeenAuto`). Typ daar het nummer in *Startnummer (handmatig)* en gebruik **XiPHiAS GridTag: verwerk handmatige nummers**.

XiPHiAS GridTag schrijft alleen in de Lightroom-catalogus; er worden geen XMP-sidecars of RAW-bestanden aangepast.

### 5. Lua-tests (optioneel)

```text
lua run_lua_tests.lua
```

Draait `CatalogWriter` tegen gestubde Lightroom-API's: status/manual-bescherming, keyword-opruiming, chunking en het overleven van een falende `setRawMetadata`. Vereist alleen een Lua-interpreter; Lightroom is niet nodig.

## Aan de slag met Codex

Startnummers zijn tekst en behouden voorloopnullen: `007` en `7` zijn
verschillende deelnemers. Vermeld het exacte startnummer in de entrylist,
handmatige invoer en evaluatielabels; ontbrekende nullen worden niet ingevuld.

1. Maak een lege map, `git init`, en zet er `AGENTS.md` en deze `README.md` in.
2. Zet je bronbestanden klaar:
   - `samples/entrylist.csv` (je `_entrylist.csv`, UTF-8 met BOM, `;`-gescheiden)
   - `docs/reference/003-Mercedes_-_AMG_Team_Verstappen_Racing.xmp`
   - `docs/reference/069-Emil_Frey_Racing.xmp`
3. Open de map in VS Code en start Codex. Codex leest `AGENTS.md` automatisch.
4. Werk de taken één voor één af. Goede eerste prompts:

```
Voer taak 1 uit van AGENTS.md §15 (scaffold). Bouw en toon dat `dotnet build` slaagt.
```
```
Voer taak 2 en 3 uit (domein + FieldBuilder). De golden tests op de twee XMP-referentiebestanden moeten slagen.
```
```
Voer taak 4 uit (NumberMatcher) met tests voor: sterke enkele hypothese, verwarbaar cluster 59/66/69/96/99, substring 5 vs 55, out-of-list.
```
5. Laat Codex na elke taak vertellen wat er is gewijzigd, hoe het is gecontroleerd (commando's) en wat openstaat.

## Lightroom-plugin installeren (na taak 7)

1. Lightroom Classic → **Bestand → Plug-in Manager → Toevoegen** → kies `lightroom/XiPHiAS.GridTag.lrdevplugin`.
2. Menu **Bibliotheek → Plug-in-extra's → XiPHiAS GridTag: instellingen…**: pad naar `gridtag.exe`, `entrylist.csv` en `session.json`.
3. Selecteer de foto's van de map, gebruik **XiPHiAS GridTag: tag Picks**.

## Belangrijke open punten (te testen in Lightroom)

Vastgelegd in `docs/open-questions.md`, onder andere:

- Werkt **Person Shown** met twee rijders (en met welk scheidingsteken)?
- Windows-quoting bij het starten van `gridtag.exe` vanuit de plugin.
- Gedrag van collecties (`GridTag Review`) en de write-gates bij honderden foto's.


## Herkenningsstrategie

De eerste bruikbare versie wordt bewust in lagen opgebouwd:

1. auto detecteren;
2. startnummer lezen en alleen tegen geldige nummers uit de entrylist matchen;
3. automerk/-model herkennen als eerste extra controle op de nummermatch;
4. optioneel logo's gebruiken als aanvullende evidence;
5. alleen automatisch taggen wanneer de gecombineerde evidence voldoende betrouwbaar en onderling consistent is; anders `GridTag Review`.

Merk/model of een logo vervangt dus nooit de entrylist. Het doel is vooral fouten zoals een overtuigend gelezen `69` op een auto die visueel duidelijk bij nummer `96` uit de entrylist hoort, tegen te houden.

## Meetlat voor de herkenning

Voor je in modellen investeert, bouw `gridtag eval` (taak 8) en meet op een eigen testset:

- ≥ 80 % van de foto's met leesbaar nummer correct,
- ≤ 2 % foute automatische tags,
- ≤ 2 s per foto.

Halen twee of meer criteria niet, dan is een lichtere variant (handmatig invullen met entrylist-autocompletion) waarschijnlijk zinvoller.

## Licenties en privacy

- Controleer de licentie van elk model en pakket (Ultralytics YOLO is AGPL-3.0; kies voor commercieel gebruik een ruimhartig gelicentieerd alternatief).
- `json.lua` (rxi) is MIT; bewaar de licentietekst in het bestand.
- Foto's en entrylists verlaten je machine niet.
