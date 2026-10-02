# Evaluatie (`gridtag eval`)

Task 8 bouwt het evaluatieharnas voordat er vision-modellen worden toegevoegd. Het harnas leest een `labels.csv` met het formaat:

```text
path;numbers
D:\foto-001.ARW;69
D:\foto-002.ARW;69,3
D:\foto-003.ARW;
```

Een lege `numbers`-waarde betekent dat de foto geen auto heeft. Nummers worden met dezelfde `NumberNormalizer` als de entrylist genormaliseerd. De evaluator verandert geen matcher-thresholds.

Voorloopnullen blijven behouden: een voorspelling `7` voor label `007` is fout.
Gebruik in de entrylist exact dezelfde schrijfwijze als het startnummer op de auto.

## Gebruik

```text
dotnet run --project src/XiPHiAS.GridTag.Cli -- eval --labels samples/labels.example.csv --entrylist samples/entrylist.csv --session samples/session.example.json
```

Het commando schrijft geen foto's en verandert geen labels. De evaluator leest RAW-previews en volledige JPEG-input. Gebruik `--vision-config` en `--plate-config` voor detector- en nummermodellen; zonder deze opties worden de null-readers gebruikt. `--car-model`/`--car-model-labels` en `--driver-name-model`/`--driver-name-alphabet` sluiten optionele evidence aan, uitsluitend voor onzekere bekende nummerkandidaten. Zie [modelvereisten en metingen](evidence-burst-timing.md). De fake providers in de tests maken model-onafhankelijke evaluatie van de rekenregels mogelijk.

## Definities

- **Auto precision**: correcte automatische primaire matches gedeeld door alle foto’s met status `auto`. Bij nul auto-resultaten is precision `0%`.
- **Recall**: correcte automatische primaire matches gedeeld door alle gelabelde foto’s met minstens één nummer. No-car-labels tellen niet mee in de recall-noemer.
- **Correcte match**: de primaire voorspelde nummerreeks is gelijk aan de genormaliseerde gelabelde nummerreeks. De volgorde blijft betekenisvol: het eerste nummer is primair.
- **Review rate**: foto’s met status `review` gedeeld door alle evaluatiefoto’s. `noCar` en `error` zijn geen review-foto’s.
- **Wrong automatic tags**: auto-resultaten waarvan de primaire nummerreeks niet gelijk is aan het label. Het go/no-go-percentage gebruikt dit aantal gedeeld door alle evaluatiefoto’s.
- **Per-reason counts**: iedere reden uit `PhotoResult.reasons` wordt afzonderlijk geteld, inclusief redenen van niet-autoresultaten.
- **Top confusions**: voor iedere foute auto-match wordt `verwacht->voorspeld` geteld. Meervoudige nummers worden met komma’s weergegeven.
- **Seconds/photo**: verstreken wall-clock tijd van de evaluatie gedeeld door het aantal labels. Dit omvat de processor die aan de evaluator wordt meegegeven.
- **Go/no-go**: `GO` wanneer alle criteria tegelijk gelden; anders `NO-GO`.

## Criteria

Volgens AGENTS.md §9:

- recall van legible nummers: minimaal `80%`;
- verkeerde automatische tags: maximaal `2%` van alle evaluatiefoto’s;
- verwerkingstijd: maximaal `2 seconden/foto`.

De command-output bevat auto precision, recall, review rate, seconds/photo, per-reason counts, confusions en de gecombineerde beslissing. Thresholds worden uitsluitend via evaluatiegegevens gewijzigd; task 8 wijzigt ze niet.

Daarnaast toont de CLI aantallen correcte/foute auto-foto's, auto rate en
evidence-observaties. Met `--out work/eval-results.json` bewaar je de individuele
fotoresultaten in het bestaande results-contract (schemaVersion 1). ID's volgen
de CSV-volgorde vanaf 1. De volledige nummerreeks, inclusief secundaire auto's,
moet overeenkomen met het label voor een correct auto-resultaat.

Voor de headerloze `.training/dataset_labels.csv` met bestandsnamen is een
lokale omzetting naar `path;numbers` met bereikbare paden nodig. Zie
[taak 10a: voor/nameting](task10a-evaluation.md) voor die omzetting en de opdrachten
met de lokale YOLOX-, PaddleOCR- en ResNet-modellen. Matchingthresholds blijven
ongewijzigd; ground-truthlabels worden uitsluitend door de evaluator gebruikt.

Burstopties `--burst-max-gap`/`--burst-similarity` maken de optionele review-
postprocessing beschikbaar. Bij `eval` komen capturetijden uit EXIF; ontbrekende
tijden sluiten een foto uit van burstpropagatie. De CLI rapporteert het aantal
`burst review photos`. Kandidaten met source `burst` blijven review en tellen
niet als automatische matches. Batchtijd omvat EXIF-lezen en burstverwerking.
Zie [de burstverificatie](task10b-burst-propagation.md).

## Fake-provider tests

De tests gebruiken geen RAW-bestanden en geen netwerk of cloudservice. Ze leveren per label vooraf gemaakte `PhotoResult`-waarden aan de evaluator en controleren:

- correcte en foute auto-resultaten;
- review-resultaten en reason counts;
- confusions;
- no-car labels;
- precision, recall, review rate en seconds/photo;
- de gecombineerde go/no-go-beslissing.

## Huidige baseline en beperking

De meegeleverde voorbeeldconfiguraties verwijzen naar ontbrekende modelgewichten. Zonder modelconfiguraties gebruikt `gridtag eval` `NullCarDetector` en `NullPlateReader`; ontbrekende foto's geven `error` met `no_preview`. De sample-evaluatie toont dan geen herkenningskwaliteit aan. De JPEG-rooktest is herhaalbaar met `--labels samples/labels.task10a-smoke.csv` als de bijbehorende foto aanwezig is. Threshold tuning is niet uitgevoerd.
