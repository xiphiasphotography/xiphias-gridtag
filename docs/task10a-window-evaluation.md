# Taak 10a — entrylist-classifier en raamnaam-evidence

Uitvoering: 10–11 oktober 2026. Code en inferentie zijn lokaal; foto's, labels, sidecars
en Lightroom-catalogus zijn niet gewijzigd. Er zijn geen nieuwe dependencies,
contractwijzigingen of gewijzigde herkenningsdrempels.

## Geïmplementeerd

- `OnnxCarModelClassifier` ondersteunt `entrylist-rgb` en `entrylist-imagenet`.
  Labels moeten de verschillende `car`-waarden van de gekozen event-entrylist
  precies eenmaal dekken. Output `[batch,label count]` wordt bij laden getoetst.
  Labelvolgorde wordt nooit herschikt: die hoort bij de model-export.
- `WindowCropDriverNameReader` begrenst de naamreader tot het raamgebied: 10–90%
  horizontaal, 10–55% verticaal binnen de tot het beeld begrensde autobox.
  Dit is een vaste geometrische schatting, geen raamsegmentatiemodel. Het kan
  namen missen bij andere standpunten. Ongeldige/ontbrekende bounds blijven
  zonder OCR-observatie; er is geen fallback naar de volledige auto.
- `run` en `eval` wrappen zowel de generieke naamreader als PaddleOCR. De
  nummerreader blijft de volledige autobox gebruiken. De verschillende crops
  krijgen verschillende OCR-uitvoer; modelsessies blijven gedeeld.
- De bestaande Core `CarModelEvidence`/`DriverNameEvidence`, conflictafhandeling
  en onzekerheids-gating blijven behouden. Geen evidence voor `Auto`, manual,
  een ontbrekend nummer of een onbekend nummer. Evidence verzint geen deelnemer.

De huidige fixture heeft acht racemodellen en een negende Volvo-testauto.
De acht racemodellen staan in `samples/car-model.entrylist-labels.txt`.
Een achtklassen-model vereist een event-entrylist zonder de Volvo-testregel.
De CLI filtert nooit op automerk, klasse of testdeelnemer: dit is een expliciet
gekozen evaluatiekopie. Met een negenmodellen-entrylist zijn negen uitvoerklassen
nodig. De sample-labelvolgorde geldt alleen als een export daarvoor is getraind.

Preprocessing: `entrylist-rgb` is nearest-neighbour RGB CHW 0–1;
`entrylist-imagenet` is bilineaire resize met RGB/ImageNet mean
`[0.485,0.456,0.406]`, std `[0.229,0.224,0.225]`. Kies uitsluitend de variant
waarvoor de toekomstige export is gemaakt. Inputs: float `[1|batch,3,H,W]`,
vaste positieve H/W; logits `[1|batch,classes]`. Inferentie gebruikt batch 1.

## Achtklassen-model: nog niet beschikbaar

De gebruiker wees op `./Models`. De aangetroffen classifier
`Models/car-model/resnet50_cars_enhanced.onnx` heeft **196 Stanford Cars-klassen**.
De overige ONNX-bestanden zijn YOLOX-S autodetectie en PaddleOCR text detection/
recognition, geen tweede automodel-classifier. De achtklassen-compatibiliteitscheck
met de aanwezige ResNet en acht labels geeft correct exitcode 3:
`Car classifier output must be [batch,label count]; the model and vocabulary do not match.`

De achtklassen-adapter is geïmplementeerd; **achtmodellen-herkenning en de
kwaliteitsmeting daarvan zijn nog niet voltooid**. Daarvoor is een passende
getrainde export met geverifieerde preprocessing en labelvolgorde nodig.
Er is niet op de evaluatie-ground-truth getraind en er zijn geen gewichten
verzonnen. De gecombineerde nameting hieronder gebruikt de bestaande
`stanford-imagenet` **merk-evidence**, geen achtklassen-GT3-classificatie.

## Dataset en vaste grenzen

112 unieke JPEG's uit `samples/ground-truth.jpg.csv` (113 bronregels), dezelfde
Lowres-map als bij de eerdere JPEG-meting. De bronpaden hebben de bekende
mapfout; de evaluatiekopie gebruikt bereikbare paden met exact dezelfde
bestandsnamen. De twee regels voor XIP_2709 vormen één label `66,58`, primair
66 eerst volgens de laatste CSV-kolom. Geen foto of nummer is weggelaten.

De entrylist-kopie `work/task10a-window-entrylist.csv` bevat alle racedeelnemers
uit de fixture, alleen klasse PRIVATE is in die kopie uitgesloten. Het is
geen productie-filter. Beide vergelijkbare runs en de naam-only-run gebruiken
exact dezelfde kopie. De twee labels `7` blijven behouden, hoewel de entrylist
`007` vermeldt: ontbrekende nullen worden niet ingevuld.

SHA256 bronlabels: `FF30D0593B9B78B51117DF355FEFB2A4C90F30A34956F72FB630D7AA692DB087`.
SHA256 bron-entrylist: `7B92146577DF1B409EC746E9B0CA8EF3F3DFCCDDA1C995171B2733C4A0EFE3F7`.
SHA256 evaluatie-entrylist: `CAA8D1FA030D744EDD47DDDEC0B4346DCFE83268DB25BA867C46D25B16347792`.

Matching: low confidence 0.90, margin 0.30, confusable 0.97, substring 0.98,
evidence conflict 0.25. Steungewicht `1 + confidence * 0.5`, modelconflict
`1 - confidence`, neutraal 1; samengestelde weights begrensd door de matcher
op [0,1.5], kandidaatkansen op 1. YOLOX confidence 0.25, NMS 0.45.
CPU-detector, dezelfde nummer-PaddleOCR, geen burst of timingcontrole.
Sessiemetadata is de voorbeeldconfiguratie en wordt niet op juistheid beoordeeld.
## Meetresultaten

| Metriek | Zonder evidence | Alleen raamnaam | Raamnaam + bestaande merkclassifier |
| --- | ---: | ---: | ---: |
| Foto's | 112 | 112 | 112 |
| Auto-resultaten | 54 | 56 | 56 |
| Correct auto | 50 | 52 | 52 |
| Fout auto | 4 | 4 | 4 |
| Auto rate | 48.21% | 50.00% | 50.00% |
| Auto precision | 92.59% | 92.86% | 92.86% |
| Recall | 44.64% | 46.43% | 46.43% |
| Review | 58 (51.79%) | 56 (50.00%) | 56 (50.00%) |
| noCar / error | 0 / 0 | 0 / 0 | 0 / 0 |
| Evidence-aanroepen | 0 | 38 | 38 |
| Modelobservaties | 0 | 0 | 38 |
| Naamtekstregels | 0 | 175 | 175 |
| Seconden/foto | 1.6958 | 1.7264 | 1.7263 |
| Go/no-go | NO-GO | NO-GO | NO-GO |

Naam-evidence alleen verandert exact twee fotoresultaten, allebei correct van
review naar auto. De combinatie verandert 30 fotoresultaten, waarvan twee
statuswijzigingen; de overige verschillen zitten in reviewredenen/candidates.
Geen bestaand auto-resultaat verandert in beide nametingen.

| Resultaat-ID | Foto | Label | Voor | Naam | Naam + merk |
| --- | --- | --- | --- | --- | --- |
| 21 | XIP_1512 | 222 | review | auto 222 | auto 222 |
| 85 | XIP_1794 | 71 | review | auto 71 | review |
| 109 | XIP_1755 | 96 | review | review | auto 96 |

Het toevoegen van de bestaande merkclassifier levert dus één andere extra
correcte match op (#96), maar houdt de door namen geholpen #71 opnieuw in
review. Het netto aantal correcte automatische matches neemt hierdoor niet
verder toe. Gelijke totaalmetingen betekenen niet dat alle fotobeslissingen gelijk zijn.

De vier foutieve auto-resultaten blijven `7 → 007` (ID 82 en 83), `93 → 88`
(ID 100) en `66,58 → 58,66` (ID 73: primaire/secundaire volgorde verwisseld).
De evaluator vereist de hele correcte nummerreeks in volgorde. Er ontstaan
geen nieuwe foutieve auto-resultaten. Vier fouten op 112 foto's is **3.57%**,
boven de 2%-grens. Recall blijft onder 80%; daarom NO-GO, ook nu de tijd in
alle drie runs onder 2 s/foto ligt.

175 aangeboden OCR-tekstregels zijn geen 175 herkende rijdersnamen. De labels
valideren nummers, niet gelezen naamtekst of actuele rijderidentiteit. De
naam-only-run meet de afzonderlijke invloed op nummermatching. De gecombineerde
run bewijst geen achtmodellen-herkenning. Eén run per configuratie, exclusief
model-laadtijd, is geen betrouwbare snelheidsbenchmark. De dataset is niet
bewezen held-out en bevat geen aparte leesbaarheidslabels/no-car-voorbeelden.
De bestaande Stanford-preprocessing blijft een ongeverifieerde aanname.

Volledige reviewredenen en confusion pairs staan in de consolelogs; dezelfde
vier confusion pairs blijven in alle runs staan. Aanvankelijk is ook een
voormeting met de ongewijzigde negenmodellen-fixture uitgevoerd (50 correct,
4 fout, 1.6084 s/foto). Die is opgeslagen als `work/task10a-window-before.*`
en is niet gebruikt als vergelijkingsbaseline voor de acht-racemodellenlijst.

## Verificatie en resterend werk

`dotnet build XiPHiAS.GridTag.slnx --no-restore`: 0 warnings/errors.
`dotnet test XiPHiAS.GridTag.slnx --no-build --no-restore`: **185 tests geslaagd**
(158 Core, 27 CLI). Negen nieuwe tests controleren de raamcrop, behouden originele
bounds/preview, begrenzing en invalid/nonfinite/missing bounds zonder OCR, en
volledige/dubbele/ontbrekende/onjuiste labels met behoud van exportvolgorde.
De bestaande tests voor evidence-gating, weights, conflicten, manual overrides
en golden XMP-fields blijven slagen. De echte 196-versus-8 compatibilitycheck
is afzonderlijk met het aanwezige model uitgevoerd; tests vereisen die gewichten niet.

De bronhashes en de evaluatie-entrylist-hash zijn na afloop gecontroleerd.
Alle drie resultaatbestanden bevatten 112 foto's, geen error/noCar.
`git diff --check` is schoon. Geen gegenereerde foto's, modellen, logs of
buildoutput worden aan versiebeheer toegevoegd.

Resterend: een achtklassen-GT3-model met passende preprocessing en labels
trainen/aanleveren, controleren op een onafhankelijke set en dezelfde meting
opnieuw uitvoeren. Met de huidige weights kan dat deel van taak 10a niet als
voltooid of gemeten worden aangemerkt.

## Reproduceren

Eerst bouwen; daarna vanuit de repositoryroot onderstaande opdrachten. De
metadata op de foto's wordt niet toegepast. De PRIVATE-filter hoort uitsluitend
bij deze fixturevoorbereiding, niet bij de productie-CLI.

```powershell
dotnet build XiPHiAS.GridTag.slnx --no-restore
# Run from the GridTag repository root. All generated files stay under work/.
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force work | Out-Null
$rows = @(Get-Content samples/ground-truth.jpg.csv -Encoding utf8 | Where-Object { $_.Trim() } |
    ForEach-Object {
        $cells = $_.Split(';')
        $photoPath = Join-Path 'S:\Fotografie\2026-09-18 - GT World Challange\Lowres' ([IO.Path]::GetFileName($cells[0]))
        if (-not (Test-Path -LiteralPath $photoPath -PathType Leaf)) { throw "Missing photo: $photoPath" }
        [pscustomobject]@{ Path = $photoPath; Number = $cells[1]; Primary = $cells[4] }
    })
$labels = @('path;numbers') + @($rows | Group-Object Path | ForEach-Object {
    $_.Name + ';' + (($_.Group | Sort-Object Primary -Descending | ForEach-Object Number) -join ',')
})
$labels | Set-Content work/task10a-jpg-labels.csv -Encoding utf8
Import-Csv samples/entrylist.csv -Delimiter ';' | Where-Object class -ne 'PRIVATE' |
    Export-Csv work/task10a-window-entrylist.csv -Delimiter ';' -NoTypeInformation -Encoding utf8
$commonArgs = @(
    'eval', '--labels', 'work/task10a-jpg-labels.csv',
    '--entrylist', 'work/task10a-window-entrylist.csv', '--session', 'samples/session.example.json',
    '--vision-config', 'samples/detector.yolox.json', '--detector-provider', 'cpu',
    '--number-ocr', 'Models/driver-name/PP-OCRv6_det_small.onnx',
    '--number-ocr-model', 'Models/driver-name/latin_PP-OCRv5_rec_mobile.onnx',
    '--number-ocr-dictionary', 'Models/driver-name/ppocrv5_latin_dict.txt'
)
dotnet src/XiPHiAS.GridTag.Cli/bin/Debug/net10.0/gridtag.dll @commonArgs `
    --out work/task10a-window-event-before.json | Tee-Object work/task10a-window-event-before.txt
if ($LASTEXITCODE -ne 0) { throw 'Baseline failed' }
$nameArgs = @(
    '--driver-name-detector', 'Models/driver-name/PP-OCRv6_det_small.onnx',
    '--driver-name-model', 'Models/driver-name/latin_PP-OCRv5_rec_mobile.onnx',
    '--driver-name-alphabet', 'Models/driver-name/ppocrv5_latin_dict.txt'
)
dotnet src/XiPHiAS.GridTag.Cli/bin/Debug/net10.0/gridtag.dll @commonArgs @nameArgs `
    --out work/task10a-window-event-names.json | Tee-Object work/task10a-window-event-names.txt
if ($LASTEXITCODE -ne 0) { throw 'Name-only run failed' }
$modelArgs = @(
    '--car-model', 'Models/car-model/resnet50_cars_enhanced.onnx',
    '--car-model-labels', 'Models/car-model/stanford_cars_labels.txt',
    '--car-model-format', 'stanford-imagenet'
)
dotnet src/XiPHiAS.GridTag.Cli/bin/Debug/net10.0/gridtag.dll @commonArgs @nameArgs @modelArgs `
    --out work/task10a-window-event-after.json | Tee-Object work/task10a-window-event-after.txt
if ($LASTEXITCODE -ne 0) { throw 'Combined run failed' }
# A future eight-class export uses --car-model-format entrylist-rgb or entrylist-imagenet
# and its actual exporter-ordered labels. The Stanford export above is not such a model.
```

Voor de compatibilitycheck: dezelfde commonArgs, de aanwezige ResNet,
`--car-model-labels samples/car-model.entrylist-labels.txt` en
`--car-model-format entrylist-imagenet`: exitcode 3 verwacht.

Lokale artifacts: `work/task10a-window-event-{before,names,after}.{json,txt}`,
`work/task10a-window-{names,combined}-changes.json`,
`work/task10a-window-wrong.json`, `work/task10a-window-eight-class-validation.txt`,
`work/task10a-window-entrylist.csv` en `work/task10a-window-commands.ps1`.