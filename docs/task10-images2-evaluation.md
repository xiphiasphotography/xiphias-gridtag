# Taken 10a en 10b — evaluatie images2

Datum: 2 oktober 2026. Alle verwerking was lokaal. Foto's en bronlabels zijn niet gewijzigd.

## Dataset en vaste invoer

Gebruikt: `.training/images2` en de aangetroffen `.training/images2_labels.csv`.
Het opgegeven `training/images_label.csv` bestaat hier niet. De gevonden CSV bevat
negen regels, allemaal met nummer 968; acht foto's zijn aanwezig.
`23ZV6-6891.JPG` ontbreekt en blijft als `no_preview` in de meting staan.
De oorspronkelijke CSV heeft SHA256
`CF20BB666D1E5B1C248E2749E536D9E04B1AB9E9C7F52F1312863F81460D7548`.

De door de gebruiker bijgewerkte entrylist bevat 968, Michael Rosa, Volvo S60 T3
R-Design. Alle drie metingen gebruiken dezelfde ongewijzigde kopie:
`work/task10-images2-entrylist.csv`, SHA256
`7B92146577DF1B409EC746E9B0CA8EF3F3DFCCDDA1C995171B2733C4A0EFE3F7`.
De headerloze bronlabels zijn naar absolute paden met een `path;numbers`-header
omgezet in `work/task10-images2-labels.csv`. Er zijn geen labels weggelaten.

## Uitvoering en grenzen

Voormeting: YOLOX en PaddleOCR voor nummers, zonder evidence en burst.
Nameting 10a: dezelfde pipeline met automerk- en rijdernaam-evidence.
Nameting 10b: dezelfde evidence plus burstgrenzen 2 seconden en 0.95 gelijkenis.

Matching blijft low confidence 0.90, margin 0.30, confusable 0.97,
substring 0.98 en evidence conflict 0.25. YOLOX blijft confidence 0.25,
NMS 0.45. Er is niet getraind of aan thresholds gedraaid.
Voorloopnullen blijven behouden: 007 en 7 zijn verschillende startnummers.

De evidence-readers worden alleen aangeroepen bij een onzekere, bestaande
entrylist-kandidaat. Ontbrekende of onbekende nummers krijgen geen deelnemer
toegekend op basis van merk of naam alleen. Burstvoorstellen blijven altijd
`review`, bron `burst`, zonder automatisch te schrijven metadata.

## Resultaten

| Metriek | Voor | Met evidence (10a) | Evidence + burst (10b) |
| --- | ---: | ---: | ---: |
| Labelregels | 9 | 9 | 9 |
| Correcte auto-matches | 1 | 1 | 1 |
| Foutieve auto-matches | 0 | 0 | 0 |
| Auto-precision | 100% | 100% | 100% |
| Recall | 11.11% | 11.11% | 11.11% |
| Reviewfoto's | 7 | 7 | 7 |
| Ontbrekende foto / error | 1 | 1 | 1 |
| Evidence-aanroepen | 0 | 1 | 1 |
| Automodel-observaties | 0 | 1 | 1 |
| Gelezen naam-tekstregels | 0 | 2 | 2 |
| Burst-reviewfoto's | 0 | 0 | 0 |

De correcte auto-match betreft `23ZV6-6325.JPG`, nummer 968.
Evidence verandert op deze set geen fotobeslissing. Twee gelezen tekstregels
betekent niet dat twee rijdernamen zijn herkend. De overige foto's blijven
review door onopgeloste nummerlezingen; evidence identificeert ze niet zelfstandig.
De gezamenlijke reviewredenen zijn `largest_car_unresolved` (5),
`no_entry_match` (1), `low_confidence` (1) en `confusable:80` (1);
een foto kan meerdere redenen hebben. Daarnaast ontbreekt één preview.
Over de acht aanwezige foto's bedraagt recall 12.5%; de CLI rapporteert 11.11%
over alle negen gelabelde regels. De uitkomst blijft **NO-GO**.

Alle acht aanwezige foto's hebben echte EXIF-opnametijden met subseconden.
De kleinste tussenpoos is **5.35 seconden**, tussen `23ZV6-6363.JPG` en
`23ZV6-2719.JPG`. Geen paar valt binnen de vaste grens van 2 seconden;
beeldgelijkenis wordt voor deze paren daarom niet berekend.
Deze set valideert het uitsluiten op tijd, maar kan de kwaliteit van echte
burstvoorstellen niet aantonen. De synthetische controles en regressietests
staan in [taak10b-burst-propagation.md](task10b-burst-propagation.md).

De sessie/eventmetadata is de bestaande voorbeeldconfiguratie
`samples/session.example.json`, geen geverifieerde beschrijving van dit evenement.
De meting beoordeelt startnummerherkenning, niet de juistheid van die captions.

## Reproduceerbare opdrachten


before:

```powershell
dotnet run --project src/XiPHiAS.GridTag.Cli --no-build -- eval --labels work/task10-images2-labels.csv --entrylist work/task10-images2-entrylist.csv --session samples/session.example.json --vision-config samples/detector.yolox.json --number-ocr Models/driver-name/PP-OCRv6_det_small.onnx --number-ocr-model Models/driver-name/latin_PP-OCRv5_rec_mobile.onnx --number-ocr-dictionary Models/driver-name/ppocrv5_latin_dict.txt --out work/task10-images2-before.json
```

after:

```powershell
dotnet run --project src/XiPHiAS.GridTag.Cli --no-build -- eval --labels work/task10-images2-labels.csv --entrylist work/task10-images2-entrylist.csv --session samples/session.example.json --vision-config samples/detector.yolox.json --number-ocr Models/driver-name/PP-OCRv6_det_small.onnx --number-ocr-model Models/driver-name/latin_PP-OCRv5_rec_mobile.onnx --number-ocr-dictionary Models/driver-name/ppocrv5_latin_dict.txt --car-model Models/car-model/resnet50_cars_enhanced.onnx --car-model-labels Models/car-model/stanford_cars_labels.txt --car-model-format stanford-imagenet --driver-name-detector Models/driver-name/PP-OCRv6_det_small.onnx --driver-name-model Models/driver-name/latin_PP-OCRv5_rec_mobile.onnx --driver-name-alphabet Models/driver-name/ppocrv5_latin_dict.txt --out work/task10-images2-after.json
```

burst:

```powershell
dotnet run --project src/XiPHiAS.GridTag.Cli --no-build -- eval --labels work/task10-images2-labels.csv --entrylist work/task10-images2-entrylist.csv --session samples/session.example.json --vision-config samples/detector.yolox.json --number-ocr Models/driver-name/PP-OCRv6_det_small.onnx --number-ocr-model Models/driver-name/latin_PP-OCRv5_rec_mobile.onnx --number-ocr-dictionary Models/driver-name/ppocrv5_latin_dict.txt --car-model Models/car-model/resnet50_cars_enhanced.onnx --car-model-labels Models/car-model/stanford_cars_labels.txt --car-model-format stanford-imagenet --driver-name-detector Models/driver-name/PP-OCRv6_det_small.onnx --driver-name-model Models/driver-name/latin_PP-OCRv5_rec_mobile.onnx --driver-name-alphabet Models/driver-name/ppocrv5_latin_dict.txt --burst-max-gap 2 --burst-similarity 0.95 --out work/task10-images2-burst.json
```

De JSON-uitvoer en consolelogs staan in `work/task10-images2-{before,after,burst}.{json,txt}`. Deze lokale gegenereerde bestanden worden niet gecommit.

Tijd per labelregel: voor **5.3808 s**, evidence **5.4430 s**, evidence + burst **5.3441 s**. Dit zijn afzonderlijke runs, exclusief het laden van modelsessies; verschillen zijn geen betrouwbaar bewijs van snelheidswinst. De ontbrekende foto telt mee in de noemer.

## Beperkingen en verificatie

De ImageNet-normalisatie van de Stanford Cars-classifier is nog een expliciete, niet door de model-export geverifieerde aanname. De PaddleOCR-adapter heeft geen perspectief- of rotatiecorrectie. Zie [de implementatie en modelbeperkingen van 10a](task10a-evaluation.md). De huidige kleine set toont geen kwaliteitswinst van evidence en bevat geen geschikte burstparen. Thresholds zijn behouden.

`dotnet build XiPHiAS.GridTag.slnx --no-restore` slaagt zonder warnings of errors.
`dotnet test XiPHiAS.GridTag.slnx --no-build --no-restore` slaagt: 142 tests
(120 Core, 22 CLI). De solution-opdrachten vereisen hier toegang tot de
geïnstalleerde Windows SDK buiten de sandbox. De fotobeslissingen in alle
drie JSON-uitvoeren zijn gelijk; alleen de generatie-tijdstempel verschilt.
