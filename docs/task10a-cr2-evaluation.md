# Taak 10a — CR2-evaluatie op echte data

Datum: 2 oktober 2026. Alle inferentie was lokaal. De opdrachten gebruiken
`eval`, lezen de CR2-bestanden en schrijven alleen uitvoer onder `work/`.
Er worden geen RAW-bestanden, sidecars of Lightroom-catalogusgegevens geschreven.

## Implementatie en vaste configuratie

De bestaande `CarModelEvidence` en `DriverNameEvidence` implementeren `IEvidence`.
Vision bevat de lokale ONNX-classifier en PaddleOCR-naamreader. De pipeline
roept deze alleen aan als het nummer zonder evidence `Review` oplevert met een
bestaande entrylist-kandidaat. Geen nummer, een onbekend nummer, `Auto` en
handmatige invoer roepen de evidence-readers niet aan. Naam-evidence toetst
alleen reeds vermelde deelnemers; deze taak bepaalt niet de huidige rijder.

Nieuw voor deze machine: `--detector-provider cpu` bij `run`/`eval` met
`--vision-config`. DirectML blijft standaard. DirectML gaf hier bij het laden
van de detector fout `887A0004` (niet ondersteunde device-interface/feature level).
Daarom gebruiken beide vergelijkbare metingen dezelfde CPU-detector. De
classifier en PaddleOCR werken eveneens lokaal op de CPU.

Ongewijzigde matchingdrempels: low confidence 0.90, margin 0.30, confusable 0.97,
substring 0.98, evidence conflict 0.25. YOLOX: confidence 0.25, NMS 0.45.
Modellen: YOLOX-S, PP-OCRv6 text detection, Latin PP-OCRv5 recognition en
ResNet50 Stanford Cars. Alleen de evidenceopties verschillen tussen voor en na.

## Labels

`samples/ground-truth.csv` bevat 113 headerloze regels, 112 unieke CR2-paden
onder `S:\Fotografie\2026-09-18 - GT World Challange\`. Alle bestanden bestaan.
De extra kolommen worden niet als OCR-invoer gebruikt. De laatste kolom wordt
geïnterpreteerd als primair/secundair: `XIP_2709.CR2` heeft `66;;;1` en `58;;;0`.
De evaluatiekopie groepeert daarom per pad, primair eerst: `66,58`.
Geen foto of nummer wordt weggelaten. Voorloopnullen blijven behouden.
De CSV heeft geen aparte leesbaarheidslabels of no-car-voorbeelden.

SHA256 bronlabels:
`4B3113F90DA7DA830F5DBAB01E8B32C4BE74E59FC065A474D8B2AF468EB973E8`.
SHA256 `samples/entrylist.csv`:
`7B92146577DF1B409EC746E9B0CA8EF3F3DFCCDDA1C995171B2733C4A0EFE3F7`.
Deze bronbestanden zijn niet gewijzigd. De gekozen entrylist blijft per-event
invoer; de meting gebruikt de aangetroffen sample als expliciete parameter.
`samples/session.example.json` is voorbeeldmetadata, geen geverifieerde sessie.

## Reproduceren vanuit de repositoryroot

```powershell
dotnet build XiPHiAS.GridTag.slnx --no-restore
New-Item -ItemType Directory -Force work | Out-Null
$rows = @(Get-Content samples/ground-truth.csv -Encoding utf8 |
    Where-Object { $_.Trim() } | ForEach-Object {
        $cells = $_.Split(';')
        [pscustomobject]@{ Path = $cells[0]; Number = $cells[1]; Primary = $cells[4] }
    })
$labels = @('path;numbers') + @($rows | Group-Object Path | ForEach-Object {
    $_.Name + ';' + (($_.Group | Sort-Object Primary -Descending |
        ForEach-Object Number) -join ',')
})
$labels | Set-Content work/task10a-cr2-labels.csv -Encoding utf8
$commonArgs = @(
    'eval', '--labels', 'work/task10a-cr2-labels.csv',
    '--entrylist', 'samples/entrylist.csv', '--session', 'samples/session.example.json',
    '--vision-config', 'samples/detector.yolox.json', '--detector-provider', 'cpu',
    '--number-ocr', 'Models/driver-name/PP-OCRv6_det_small.onnx',
    '--number-ocr-model', 'Models/driver-name/latin_PP-OCRv5_rec_mobile.onnx',
    '--number-ocr-dictionary', 'Models/driver-name/ppocrv5_latin_dict.txt'
)
dotnet src/XiPHiAS.GridTag.Cli/bin/Debug/net10.0/gridtag.dll @commonArgs `
    --out work/task10a-cr2-before.json | Tee-Object work/task10a-cr2-before.txt
$evidenceArgs = @(
    '--car-model', 'Models/car-model/resnet50_cars_enhanced.onnx',
    '--car-model-labels', 'Models/car-model/stanford_cars_labels.txt',
    '--car-model-format', 'stanford-imagenet',
    '--driver-name-detector', 'Models/driver-name/PP-OCRv6_det_small.onnx',
    '--driver-name-model', 'Models/driver-name/latin_PP-OCRv5_rec_mobile.onnx',
    '--driver-name-alphabet', 'Models/driver-name/ppocrv5_latin_dict.txt'
)
dotnet src/XiPHiAS.GridTag.Cli/bin/Debug/net10.0/gridtag.dll @commonArgs @evidenceArgs `
    --out work/task10a-cr2-after.json | Tee-Object work/task10a-cr2-after.txt
```

## Resultaten

| Metriek | Zonder evidence | Met merk + naam |
| --- | ---: | ---: |
| Unieke foto's | 112 | 112 |
| Auto-resultaten (correct / fout) | 0 (0 / 0) | 0 (0 / 0) |
| Auto rate | 0% | 0% |
| Auto precision volgens CLI | 0% | 0% |
| Recall | 0% | 0% |
| Review | 104 (92.86%) | 104 (92.86%) |
| noCar / error | 8 / 0 | 8 / 0 |
| Evidence-aanroepen / modelobservaties / naamtekstregels | 0 / 0 / 0 | 0 / 0 / 0 |
| Seconden/foto | 0.1546 | 0.1528 |
| Go/no-go | NO-GO | NO-GO |

Alle 112 individuele fotoresultaten zijn identiek, inclusief redenen en cars.
De redenen zijn `largest_car_unresolved` (103), `no_entry_match` (1) en
`no_car_detected` (8). Er zijn geen automatische voorspellingen en dus ook
geen confusion pairs. Precision is bij nul auto-resultaten niet inhoudelijk
meetbaar; de CLI rapporteert volgens zijn conventie 0%. Evidence wordt niet
uitgevoerd omdat er geen onzekere *vermelde* kandidaat is. De tijdverschillen
van één run per configuratie bewijzen geen snelheidswinst; modelladen valt
buiten de gemeten verwerkingstijd. De kwaliteit is onvoldoende voor gebruik.

## Grenzen van de conclusie

De huidige RAW-reader kiest de EXIF-thumbnail voordat hij een decode probeert.
Voor de afzonderlijk gecontroleerde `XIP_1311.CR2` en `XIP_2801.CR2` is die
thumbnail **160×120 pixels**, bron `EmbeddedJpeg`. Dat is ongeschikt voor fijne
startnummertekst en een belangrijke beperking van deze RAW-pipeline. De
nulscore toont geen bewijs dat de evidence-modellen goed of slecht werken:
ze krijgen geen onzekere entrylist-kandidaat om te beoordelen.

De eerstvolgende kwaliteitsstap is een grotere ingebedde CR2-preview gebruiken,
daarna dezelfde voor/nameting herhalen zonder drempels te wijzigen. De
Stanford Cars-classifier levert merk-evidence uit straatwagenklassen; specifieke
GT3-modelherkenning is niet aangetoond. De normalisatie is een bestaande,
ongeverifieerde aanname. De naamreader mist perspectief- en rotatiecorrectie.

Build: `dotnet build XiPHiAS.GridTag.slnx --no-restore`, nul warnings/errors.
Tests: `dotnet test XiPHiAS.GridTag.slnx --no-build --no-restore`, 176 geslaagd
(149 Core, 27 CLI), inclusief evidence-gating/conflicten en CLI-providerfouten.
De gegenereerde JSON-, label- en logbestanden staan lokaal onder `work/`.