# Taak 10a — JPEG-evaluatie op echte data

Datum: 2 oktober 2026. Voor en na uitgevoerd met `gridtag eval`, dezelfde lokale
modellen en ongewijzigde thresholds. Foto's, sidecars, Lightroom-catalogus en
bronlabels zijn niet geschreven. Deze herhaling verandert geen herkenningscode.

## Dataset en vaste invoer

`samples/ground-truth.jpg.csv` bevat 113 headerloze regels voor 112 unieke JPEG's.
De CSV verwijst naar de niet-bestaande map `GT World Challangeowres`. Alle
bestandsnamen zijn exact gevonden in
`S:\Fotografie\2026-09-18 - GT World Challange\Lowres`. Alleen de aparte
`work/task10a-jpg-labels.csv` gebruikt die herstelde paden; de bron-CSV blijft intact.
De bestandsnamen bevatten al deelnemersinformatie. Die informatie wordt niet
als herkenningsinvoer gebruikt: inferentie gebruikt uitsluitend beeldpixels;
de evaluatielabels worden pas bij de beoordeling van resultaten gebruikt.

De laatste CSV-kolom wordt net als bij de CR2-meting als primair/secundair
geïnterpreteerd. Twee regels voor `XIP_2709` worden één label `66,58`, primair
66 eerst. Alle foto's en nummers blijven behouden, inclusief voorloopnullen.
Er zijn geen afzonderlijke leesbaarheidslabels of no-car-voorbeelden.

SHA256 `samples/ground-truth.jpg.csv`:
`FF30D0593B9B78B51117DF355FEFB2A4C90F30A34956F72FB630D7AA692DB087`.
SHA256 `samples/entrylist.csv`:
`7B92146577DF1B409EC746E9B0CA8EF3F3DFCCDDA1C995171B2733C4A0EFE3F7`.
De hashes zijn na afloop opnieuw gecontroleerd en gelijk gebleven.
De entrylist is expliciet gekozen invoer, geen hard-gecodeerde eventlijst.
`samples/session.example.json` is voorbeeldmetadata; deze meting valideert
startnummers en niet de juistheid van sessie/captiontekst.

## Configuratie

YOLOX-S met confidence 0.25 en NMS 0.45, expliciet `--detector-provider cpu`
vanwege de eerder vastgestelde DirectML-fout op deze machine. Nummer-OCR:
PP-OCRv6 text detection en Latin PP-OCRv5 recognition met originele woordenlijst.
Matching: low confidence 0.90, margin 0.30, confusable 0.97, substring 0.98,
evidence conflict 0.25. De nameting voegt alleen ResNet50 Stanford Cars
merk-evidence en PaddleOCR-naam-evidence toe. Geen burst of timing-evidence.
Evidence wordt uitsluitend bij onzekere, bestaande entrylist-kandidaten gelezen;
zekere automatische matches en ontbrekende/onbekende nummers slaan die stap over.

## Resultaten

| Metriek | Zonder evidence | Met merk + naam |
| --- | ---: | ---: |
| Foto's | 112 | 112 |
| Auto-resultaten | 54 | 60 |
| Correcte auto-resultaten | 50 | 56 |
| Foute auto-resultaten | 4 | 4 |
| Auto rate | 48.21% | 53.57% |
| Auto precision | 92.59% | 93.33% |
| Recall | 44.64% | 50.00% |
| Review | 58 (51.79%) | 52 (46.43%) |
| noCar / error | 0 / 0 | 0 / 0 |
| Evidence-aanroepen | 0 | 38 |
| Modelobservaties | 0 | 38 |
| Naamtekstregels | 0 | 536 |
| Seconden/foto | 1.5144 | 1.8429 |
| Go/no-go | NO-GO | NO-GO |

Er zijn zes correcte `review → auto`-wijzigingen:

| CSV-resultaat-ID | Foto | Nummer |
| --- | --- | --- |
| 16 | XIP_1460 | 21 |
| 17 | XIP_1843 | 21 |
| 18 | XIP_2648 | 21 |
| 19 | XIP_2654 | 21 |
| 70 | XIP_2144 | 6 |
| 109 | XIP_1755 | 96 |

Geen bestaand auto-resultaat is gewijzigd. In totaal veranderen 30 individuele
fotoresultaten: zes van status en 24 reviewresultaten in redenen en/of kandidaten.
De vier fouten blijven:

- `7 → 007`, tweemaal (resultaat-ID 82 en 83).
- `93 → 88`, eenmaal (ID 100).
- `66,58 → 58,66`, eenmaal (ID 73): beide nummers gevonden, maar primair/secundair
  verwisseld. De evaluator vereist de volledige correcte nummerreeks en volgorde.

De verkeerde automatische tags vormen in beide metingen 4/112 = **3.57%** van
alle foto's, boven de grens van 2%. Recall blijft onder 80%. De snelheid blijft
in deze runs onder 2 s/foto. Alle criteria moeten tegelijk slagen: **NO-GO**.

536 naamtekstregels zijn OCR-observaties, geen 536 herkende rijdersnamen.
Deze gecombineerde meting meet merk plus naam; zij bewijst niet hoeveel winst
iedere bron afzonderlijk geeft. Eén run per configuratie, zonder model-laadtijd,
is geen betrouwbare snelheidsbenchmark. De Stanford Cars-classifier gebruikt
straatwagenklassen en een bestaande ongeverifieerde normalisatie-aanname;
specifieke GT3-modelherkenning is niet aangetoond. PaddleOCR heeft geen
perspectief- of rotatiecorrectie. Deze set heeft geen aangetoonde held-out-status.
Er is in deze taak niet getraind of aan thresholds gedraaid.

## Reproduceerbare opdrachten

Vanuit de repositoryroot, met de bestaande CPU-optie. De JPEG-reader gebruikt
het volledige JPEG-beeld en geen EXIF-thumbnail. De uitvoer blijft onder `work/`.

```powershell
dotnet build XiPHiAS.GridTag.slnx --no-restore
New-Item -ItemType Directory -Force work | Out-Null
$rows = @(Get-Content samples/ground-truth.jpg.csv -Encoding utf8 |
    Where-Object { $_.Trim() } | ForEach-Object {
        $cells = $_.Split(';')
        [pscustomobject]@{ Path = (Join-Path 'S:\Fotografie\2026-09-18 - GT World Challange\Lowres' ([IO.Path]::GetFileName($cells[0]))); Number = $cells[1]; Primary = $cells[4] }
    })
$labels = @('path;numbers') + @($rows | Group-Object Path | ForEach-Object {
    $_.Name + ';' + (($_.Group | Sort-Object Primary -Descending |
        ForEach-Object Number) -join ',')
})
$labels | Set-Content work/task10a-jpg-labels.csv -Encoding utf8
$commonArgs = @(
    'eval', '--labels', 'work/task10a-jpg-labels.csv',
    '--entrylist', 'samples/entrylist.csv', '--session', 'samples/session.example.json',
    '--vision-config', 'samples/detector.yolox.json', '--detector-provider', 'cpu',
    '--number-ocr', 'Models/driver-name/PP-OCRv6_det_small.onnx',
    '--number-ocr-model', 'Models/driver-name/latin_PP-OCRv5_rec_mobile.onnx',
    '--number-ocr-dictionary', 'Models/driver-name/ppocrv5_latin_dict.txt'
)
dotnet src/XiPHiAS.GridTag.Cli/bin/Debug/net10.0/gridtag.dll @commonArgs `
    --out work/task10a-jpg-before.json | Tee-Object work/task10a-jpg-before.txt
$evidenceArgs = @(
    '--car-model', 'Models/car-model/resnet50_cars_enhanced.onnx',
    '--car-model-labels', 'Models/car-model/stanford_cars_labels.txt',
    '--car-model-format', 'stanford-imagenet',
    '--driver-name-detector', 'Models/driver-name/PP-OCRv6_det_small.onnx',
    '--driver-name-model', 'Models/driver-name/latin_PP-OCRv5_rec_mobile.onnx',
    '--driver-name-alphabet', 'Models/driver-name/ppocrv5_latin_dict.txt'
)
dotnet src/XiPHiAS.GridTag.Cli/bin/Debug/net10.0/gridtag.dll @commonArgs @evidenceArgs `
    --out work/task10a-jpg-after.json | Tee-Object work/task10a-jpg-after.txt
```

Lokale meetbestanden: `work/task10a-jpg-{before,after}.{json,txt}`,
`work/task10a-jpg-labels.csv`, `work/task10a-jpg-changes.json` en
`work/task10a-jpg-wrong-before.json`. De opgeslagen fotoresultaten zijn onderling
vergeleken en op foutstatus gecontroleerd. In deze herhaling zijn geen codewijzigingen
of nieuwe tests aangebracht. Zie ook [de CR2-meting](task10a-cr2-evaluation.md).