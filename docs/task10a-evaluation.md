# Taak 10a — echte evidence-evaluatie

Datum: 2 oktober 2026. Foto's en modellen zijn uitsluitend lokaal verwerkt;
RAW/JPEG-bestanden en oorspronkelijke labels zijn niet gewijzigd.

## Implementatie

- `CarModelEvidence` en `DriverNameEvidence` blijven Core-implementaties van
  `IEvidence`; ONNX-inferentie en beeldverwerking blijven in Vision.
- De pipeline leest eerst nummers. Alleen `Review` met een bestaande
  entrylist-kandidaat roept de visuele evidencefactory aan. `Auto`, ontbrekende
  nummers, onbekende nummers en handmatige invoer slaan deze stap over.
- ResNet50 accepteert de dynamische batchdimensie. De opt-in modus
  `stanford-imagenet` levert merk-evidence uit de 196 straatwagenklassen.
- De lokale PaddleOCR-detector en -recognizer lezen tekstregels binnen elke
  autodetectie. De originele woordenlijst wordt op tekenvolgorde gecontroleerd.
  CTC-kansen worden rechtstreeks gedecodeerd, zonder tweede softmax.
- Naam-evidence ondersteunt volledige namen of unieke achternamen, eventueel
  met initiaal; losse fragmenten en niet-herkende sponsorregels blijven neutraal.
- Een sterk automerkconflict blijft `review`, ook als een andere bron steun geeft.
  Evidence voegt nooit een niet door OCR gelezen deelnemer toe.

De bestaande digit-adapter was niet geschikt voor het meegeleverde algemene
PaddleOCR-model. Om evidence daadwerkelijk op foto's te meten is daarom ook
een primaire `PaddleNumberReader` toegevoegd. Deze accepteert uitsluitend
volledige numerieke tekstregels, zonder kennis van labels of entrylist.
Dezelfde OCR-uitvoer wordt binnen één autobox hergebruikt voor naam-evidence.

Alle bestaande matchingthresholds blijven:
low confidence 0.90, margin 0.30, confusable 0.97, substring 0.98 en
evidence conflict 0.25. Evidencegewichten blijven steun `1 + confidence * 0.5`,
automodel-tegenspraak `1 - confidence` en neutraal 1. De YOLOX-configuratie
blijft confidence 0.25 en NMS 0.45.

## Dataset en reproduceerbaarheid

`.training/dataset_labels.csv` bevat 181 headerloze `imagename;numbers`-regels;
alle 181 bestanden waren aanwezig in `.training/images`. Alle labels bevatten
minstens één nummer. Meervoudige nummers en hun volgorde blijven bewaard;
voorloopnullen blijven behouden: `007` en `7` zijn verschillende startnummers.

SHA-256 van de bron-CSV:
`A83B41594BBBA54AA23609A24F408AD35045F8F023290C6AE94375AE40726DAB`.

Omzetten vanuit de repositoryroot:

```powershell
New-Item -ItemType Directory -Force work | Out-Null
$datasetRoot = Join-Path (Get-Location) '.training/images'
$labels = @('path;numbers') + @(Get-Content .training/dataset_labels.csv |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    ForEach-Object {
        $cells = $_.Split(';', 2)
        (Join-Path $datasetRoot $cells[0]) + ';' + $cells[1]
    })
$labels | Set-Content work/task10a-training-labels.csv
```

Beide inhoudelijke metingen gebruiken dezelfde detector, nummer-OCR, entrylist,
session en labels. Alleen de evidenceopties verschillen:

```powershell
dotnet build XiPHiAS.GridTag.slnx
$commonArgs = @(
    'eval',
    '--labels', 'work/task10a-training-labels.csv',
    '--entrylist', 'samples/entrylist.csv',
    '--session', 'samples/session.example.json',
    '--vision-config', 'samples/detector.yolox.json',
    '--number-ocr', 'Models/driver-name/PP-OCRv6_det_small.onnx',
    '--number-ocr-model', 'Models/driver-name/latin_PP-OCRv5_rec_mobile.onnx',
    '--number-ocr-dictionary', 'Models/driver-name/ppocrv5_latin_dict.txt'
)
dotnet run --project src/XiPHiAS.GridTag.Cli --no-build -- @commonArgs --out work/task10a-ocr-before.json |
    Tee-Object work/task10a-ocr-before.txt

$evidenceArgs = @(
    '--car-model', 'Models/car-model/resnet50_cars_enhanced.onnx',
    '--car-model-labels', 'Models/car-model/stanford_cars_labels.txt',
    '--car-model-format', 'stanford-imagenet',
    '--driver-name-detector', 'Models/driver-name/PP-OCRv6_det_small.onnx',
    '--driver-name-model', 'Models/driver-name/latin_PP-OCRv5_rec_mobile.onnx',
    '--driver-name-alphabet', 'Models/driver-name/ppocrv5_latin_dict.txt'
)
dotnet run --project src/XiPHiAS.GridTag.Cli --no-build -- @commonArgs @evidenceArgs --out work/task10a-ocr-after.json |
    Tee-Object work/task10a-ocr-after.txt
```

YOLOX-input/output zijn `[1,3,640,640]` en `[1,8400,85]`.
ResNet-input/output zijn `[batch,3,224,224]` en `[batch,196]`.
PaddleOCR-herkenning levert `[batch,time,504]`: 502 originele tekens, blank en
spatie. De beeldmaten van beide PaddleOCR-modellen zijn dynamisch.

## Resultaten

**Historische meting, vóór de correctie van voorloopnullen:** de onderstaande
resultaten gebruikten nog een normalizer die `007` naar `7` omzette. Dat gedrag
is inmiddels gecorrigeerd. Deze cijfers bewijzen geen correcte onderscheiding
tussen die twee startnummers en moeten voor de actuele verwerking opnieuw
worden gemeten met exact gespelde entrylistnummers.

De oorspronkelijke pipeline met alleen YOLOX en zonder passende nummer-OCR
gaf op deze 181 foto's: 0 auto, 180 review, 1 noCar; precision/recall 0%,
review rate 99.45%, 0.6384 s/foto. Evidence werd niet uitgevoerd. Deze nulmeting
is niet gebruikt om een evidenceverbetering te claimen.

De vergelijkbare voor/nameting met dezelfde nummer-OCR staat hieronder.
De evaluator vereist een exacte match van de volledige nummerreeks, dus een
ontbrekende secundaire auto telt als fout. Nul `error`-resultaten is apart
gecontroleerd in de opgeslagen fotoresultaten.

| Metriek | Zonder evidence | Met merk + naam |
| --- | ---: | ---: |
| Foto's | 181 | 181 |
| Auto-resultaten | 92 | 100 |
| Correcte auto-resultaten | 90 | 98 |
| Foute auto-resultaten | 2 | 2 |
| Auto rate | 50,83% | 55,25% |
| Auto precision | 97,83% | 98,00% |
| Recall | 49,72% | 54,14% |
| Review | 88 (48,62%) | 80 (44,20%) |
| noCar / error | 1 / 0 | 1 / 0 |
| Seconden/foto | 1,0778 | 1,0420 |
| Go/no-go | NO-GO | NO-GO |

De acht statuswijzigingen zijn `review → auto`, alle correct: één foto met
nummer 6 (CSV-resultaat-ID 9) en zeven met nummer 21 (ID's 27, 42, 44, 61,
99, 132 en 160). Geen bestaand auto-resultaat is gewijzigd. Het aantal foute
auto-resultaten is in beide runs 1,10% van alle foto's; de recall haalt het
criterium van 80% niet. De bestaande fouten blijven `89 → 88` en
`66,58 → 58` (ontbrekende secundaire auto). Omdat evidence alleen bij onzekerheid
wordt toegepast, kan het al als `auto` geaccepteerde fouten niet corrigeren.

In de nameting is de evidencefactory 71 keer aangeroepen op onzekere
autodetecties. De classifier leverde 71 observaties; de naamreader bood 1.113
tekstregels aan voor vergelijking met de entrylist. Dat zijn grotendeels
willekeurige autoteksten/sponsorregels, **geen 1.113 herkende rijdersnamen**.
De voormeting gebruikte geen classifier of naam-evidence. De gerapporteerde
71 factorycalls in die eerdere binary waren lege factorycalls zonder readers;
de definitieve implementatie telt die niet meer mee.

Deze gecombineerde meting is geen afzonderlijke ablation van merk versus naam.
De kleine tijdafname bewijst geen snelheidswinst. Beide metingen gebruiken
dezelfde OCR; de nameting hergebruikt OCR-uitvoer voor namen en doet daarnaast
classifier-inferentie voor onzekere kandidaten.

Lokale meetbestanden:
`work/task10a-ocr-before.txt`, `work/task10a-ocr-after.txt`, de bijbehorende
results-JSON's en `work/task10a-changes.json`. Ze zijn gegenereerde, genegeerde
werkbestanden en worden niet gecommit.

Verificatie: `dotnet build XiPHiAS.GridTag.slnx --no-restore` zonder warnings
of errors; `dotnet test XiPHiAS.GridTag.slnx --no-build --no-restore`: 96 tests
geslaagd. Tests dekken onder andere CTC-kansen, woordenlijstvolgordegebruik,
herhaalde tekens, nummerdeduplicatie, tekstregiogeometrie, fabrikantlabels,
naamfragmenten, gedeelde achternamen, onzekerheids-gating en evidenceconflicten.
De woordenlijst zelf is bij het laden van het echte model gecontroleerd;
de tests gebruiken de lokale foto's/modellen niet als verplichte fixtures.

## Beperkingen en vervolg

De [ZEDEDA-modelpagina](https://huggingface.co/zededa/resnet50-cars) documenteert
geen preprocessing. De gebruikte RGB/ImageNet-normalisatie en directe
bilineaire resize naar 224×224 zijn expliciete aannames, geen geverifieerde
exportvereisten. Stanford Cars bevat straatwagenklassen; GT3/EVO-identificatie
is daardoor niet aangetoond. Merk-evidence is een eerste lokale proef.

De PaddleOCR-adapter gebruikt asgerichte DB-componenten en begrensde beeldmaten.
Hij heeft geen perspectiefcorrectie of tekstoriëntatieclassifier en reproduceert
RapidOCR's polygonen/dilatie niet exact. Referenties en exacte preprocessing
staan in [evidence-burst-timing.md](evidence-burst-timing.md).

De datasetnaam `.training` maakt dit geen held-out testset: deze meting gebruikt
de volledige aangeleverde set. Er is hierop niet getraind of aan thresholds
gedraaid. De labels onderscheiden leesbare/onleesbare nummers niet. Recall
wordt daarom over alle 181 gelabelde autofoto's berekend. Tijdmetingen zijn
één run per configuratie en sluiten het laden van modelsessies uit; korte
build/testactiviteit tijdens de voormeting kan de timing beïnvloed hebben.

De verdere kwaliteitsstap is verificatie van de ResNet-preprocessing en een
held-out evaluatie, met inspectie van foutieve auto-matches en afzonderlijke
nummer-/merk-/naamkwaliteit. Thresholds zijn in deze taak niet gewijzigd.
