# Evaluatie (`gridtag eval`)

Task 8 bouwt het evaluatieharnas voordat er vision-modellen worden toegevoegd. Het harnas leest een `labels.csv` met het formaat:

```text
path;numbers
D:\foto-001.ARW;69
D:\foto-002.ARW;69,3
D:\foto-003.ARW;
```

Een lege `numbers`-waarde betekent dat de foto geen auto heeft. Nummers worden met dezelfde `NumberNormalizer` als de entrylist genormaliseerd. De evaluator verandert geen matcher-thresholds.

## Gebruik

```text
dotnet run --project src/XiPHiAS.GridTag.Cli -- eval --labels samples/labels.example.csv --entrylist samples/entrylist.csv --session samples/session.example.json
```

Het commando schrijft geen foto’s en verandert geen labels. De huidige command-line evaluator gebruikt de lokale stub providers. Daardoor is de huidige meting een contract- en pipeline-baseline: zonder vision-preview komen foto’s op `review` met `no_preview`. De fake providers in de tests maken model-onafhankelijke evaluatie van de rekenregels mogelijk.

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

## Fake-provider tests

De tests gebruiken geen RAW-bestanden en geen netwerk of cloudservice. Ze leveren per label vooraf gemaakte `PhotoResult`-waarden aan de evaluator en controleren:

- correcte en foute auto-resultaten;
- review-resultaten en reason counts;
- confusions;
- no-car labels;
- precision, recall, review rate en seconds/photo;
- de gecombineerde go/no-go-beslissing.

## Huidige baseline en beperking

`gridtag eval` gebruikt in productie de bestaande `StubRawPreviewProvider`, `NullCarDetector` en `NullPlateReader`. De sample-evaluatie kan daarom nog geen herkenningskwaliteit aantonen: zonder echte preview wordt geen nummer gelezen. De gemeten recall/precision en go/no-go-uitkomst zijn dus een technische baseline totdat task 9 echte vision providers toevoegt. Threshold tuning is niet uitgevoerd.
