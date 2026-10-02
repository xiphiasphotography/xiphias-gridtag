# Evidence, bursts en timing

## Evidence

De model- en driverbronnen worden alleen toegepast wanneer de eerste nummer-match `review` is met een in de entrylist aanwezige nummerkandidaat. Bij `auto`, ontbrekende lezingen, onbekende nummers en handmatige invoer worden readers niet aangeroepen.

Sluit `VisionEvidenceFactory.Create` aan als `TaggingPipeline.visualEvidenceProvider`. De readers ontvangen de getypeerde preview en de specifieke autodetectie. Contextuele evidence wordt bij dezelfde onzekere match gecombineerd met visuele evidence. Lege naamobservaties en niet-eindige confidencewaarden blijven neutraal.

- `CarModelEvidence` vergelijkt een modelobservatie met `Entry.Car`.
- `DriverNameEvidence` vergelijkt gelezen namen met de drivers van een entry.
- `TimingCrossCheckEvidence` vergelijkt een capturetijd met passing times uit CSV.
- `CompositeEvidence` combineert onafhankelijke bronnen zonder entrylist-candidates te creëren.

Vision kent de entrylist niet. `ICarModelClassifier` en `IDriverNameReader` leveren alleen observaties; Core maakt daar weights van en `NumberMatcher` beslist.

Er zijn geen thresholds gewijzigd. `OnnxCarModelClassifier` en `OnnxDriverNameReader` voeren lokale ONNX-inferentie uit op de afzonderlijke autobox; ze kennen de entrylist niet. De CLI sluit ze aan op dezelfde pipeline voor `run` en `eval` en disposeert hun sessies na afloop. Een sterk conflict van een individuele bron blijft `review`, ook wanneer een andere bron steun geeft of een andere nummerkandidaat bovenaan komt. Gewogen nummerkansen worden volgens de bestaande matchingregels begrensd op 1.

### Modelvereisten en CLI

De generieke evidence-adapters verwachten één float RGB-input `[batch,3,height,width]` met vaste beeldmaten en één float-output; batch mag dynamisch zijn en wordt als 1 uitgevoerd. De autobox wordt begrensd tot het beeld, vervolgens met nearest-neighbour naar de modeldimensies geschaald; RGB-kanalen worden gedeeld door 255. Modellen moeten voor deze preprocessing en volledige autoboxen zijn getraind/geëxporteerd. De implementatie gebruikt de lokale ONNX Runtime CPU-provider. De hieronder beschreven lokale modeladapters kiezen expliciet andere preprocessing.

- Automodel: logits `[1,classes]`; `labels.txt` bevat één niet-leeg model-/merklabel per regel, in outputvolgorde. Softmax levert de confidence.
- Coureursnaam: CTC-logits `[1,time,classes]`; `alphabet.txt` bevat één symbool per regel, met een blank-placeholder op regel 1. Spaties en Unicode-symbolen zijn toegestaan. Greedy CTC-decoding voegt herhaalde symbolen samen, behalve wanneer een blank ertussen staat. Confidence is het geometrisch gemiddelde van de uitgegeven symboolkansen. Deze reader leest één tekstregel per autobox; een willekeurig tekst-detector/OCR-model is niet automatisch compatibel.
- Onjuiste outputvormen geven een fout per foto; ontbrekende observaties blijven neutraal. Modelpaden en vocabularia worden vooraf gecontroleerd.

```powershell
dotnet run --project src/XiPHiAS.GridTag.Cli -- eval --labels work/labels.csv --entrylist samples/entrylist.csv --session samples/session.example.json --vision-config samples/detector.example.json --plate-config samples/plate-reader.example.json --car-model work/car-model.onnx --car-model-labels work/car-model-labels.txt --driver-name-model work/driver-name.onnx --driver-name-alphabet work/alphabet.txt
```

De bestanden in dit voorbeeld zijn niet meegeleverd; de detector- en nummerconfiguraties verwijzen ook naar nog ontbrekende gewichten. Gebruik alleen lokale modellen waarvan de licentie commercieel gebruik toestaat. Er zijn geen modellen gedownload of foto's geüpload.

### Lokale modellen in Models (taak 10a)

`--car-model-format stanford-imagenet` ondersteunt de toegevoegde ResNet50-export
met dynamische batch en 196 Stanford Cars-labels. De begrensde autobox wordt
bilineair naar 224×224 geschaald, RGB / 255 wordt genormaliseerd met mean
`[0.485,0.456,0.406]` en std `[0.229,0.224,0.225]`. **Dit is een expliciete
aanname:** de [modelpagina](https://huggingface.co/zededa/resnet50-cars) documenteert
de preprocessing niet. Er wordt geen GT3-model verzonnen uit een straatwagenlabel:
deze modus levert uitsluitend het merk van de hoogste klasse, met de confidence
van die klasse. Mercedes-Benz wordt als Mercedes vergeleken met Mercedes-AMG.
De generieke classifier blijft modeltekst leveren.

`--driver-name-detector` kiest de lokale PaddleOCR-adapter. Deze gebruikt
`PP-OCRv6_det_small.onnx`, `latin_PP-OCRv5_rec_mobile.onnx` en de originele
`ppocrv5_latin_dict.txt`. De woordenlijst wordt vergeleken met de tekenvolgorde
in de modelmetadata; blank wordt vóór en spatie achter de woordenlijst geplaatst.
Voor detectie en herkenning wordt BGR naar [-1,1] genormaliseerd. Tekstregels
worden aspectbehoudend naar hoogte 48 geschaald en rechts met genormaliseerde
nulwaarden gepad. De al aanwezige CTC-kansen krijgen geen tweede softmax;
confidence is het gemiddelde van uitgegeven tekenkansen.

De tekstregio-adapter gebruikt begrensde, asgerichte connected components uit
de DB-kaart, met vaste binarisatie 0.3, componentconfidence 0.5 en expansie 1.6.
De detectie-input is maximaal 960 pixels op de langste zijde, afgerond op
veelvouden van 32. Dit is geen exacte kopie van RapidOCR's polygonen,
dilatie, rotated-box extraction en perspectiefcorrectie. Schuine tekst kan
daardoor slechter gelezen worden. Referenties:
[preprocessing/CTC](https://github.com/RapidAI/RapidOCR/blob/main/python/rapidocr/ch_ppocr_rec/main.py),
[woordenlijst/decoding](https://github.com/RapidAI/RapidOCR/blob/main/python/rapidocr/ch_ppocr_rec/utils.py),
[DB-detector](https://github.com/RapidAI/RapidOCR/blob/main/python/rapidocr/ch_ppocr_det/utils.py).

Dezelfde lokale tekstreader kan met `--number-ocr`, `--number-ocr-model` en
`--number-ocr-dictionary` primaire nummerlezingen leveren. Alleen volledige
numerieke regels (optioneel voorafgegaan door #) worden kandidaten. Dubbele
lezingen van hetzelfde nummer behouden de hoogste confidence; ze worden niet
opgeteld. Deze reader kent geen entrylist en gebruikt geen ground truth.
Wanneer beide rollen dezelfde modellen gebruiken, wordt slechts de laatste
autobox-uitvoer gecachet. Het aanbieden van naam-evidence en de classifier-
inferentie blijven uitsluitend bij een onzekere bekende nummerkandidaat.

Driver-name evidence accepteert volledige namen, unieke achternamen of een
initiaal met unieke achternaam. Korte tekstfragmenten en willekeurige
sponsorregels geven geen steun. Een onbekende naam is neutraal. De bestaande
evidencegewichten en matchingthresholds zijn niet aangepast.

Zie [de echte voor/nameting op .training](task10a-evaluation.md) voor de
reproduceerbare opdrachten, resultaten en beperkingen.

## Timing CSV

Formaat:

```text
number;time
69;2026-09-18 13:52:05
```

Gebruik in de CLI:

```text
gridtag eval --labels work/labels.csv --entrylist samples/entrylist.csv --session samples/session.example.json --timing-csv work/passing-times.csv --clock-offset 00:00:05
```

De offset wordt bij de fototijd opgeteld. Een passing binnen de standaardtolerantie van twee seconden versterkt de kandidaat; een bekende kandidaat die buiten de tolerantie valt geeft conflictgewicht. Ontbrekende timingdata blijft neutraal.

Bij `eval --timing-csv` wordt de echte EXIF-opnametijd gebruikt, ook zonder
burstopties. Zie [taak 10c](task10c-timing-cross-check.md) voor CSV-validatie,
klokoffset, neutrale gevallen en de grenzen van deze controle.

## Burst-propagatie

`BurstPropagation.Propose` gebruikt EXIF-tijdverschil en een aangeleverde similarityscore. Het resultaat is altijd een `BurstNumberProposal` met reden `burst_propagation_review`. Een voorstel wordt nooit als `auto` teruggegeven en moet opnieuw door de gewone nummer/evidence-validatie of handmatige review.

Taak 10b sluit dit aan als optionele batchstap via `BurstReviewProcessor` en
`PreviewFrameSimilarity`. Activeer met `--burst-max-gap 2 --burst-similarity 0.95`
bij `run` of `eval`. De bron moet onafhankelijk herkend zijn; voorstellen blijven
review, zonder metadatafields of verdere propagatie. Zie
[burstregels, resultaatrepresentatie en verificatie](task10b-burst-propagation.md).

## Metingen

### JPEG-rooktest (vervolg taak 10a)

In `samples/images` staan 181 JPEG's, zonder meegeleverde ground-truthlabels of ONNX-modelbestanden. Eén beeld is visueel gecontroleerd: `794413804_1768687518228080_6224452282927640869_n.jpg` toont nummer 5. Dat label staat in `samples/labels.task10a-smoke.csv`; de overige foto's zijn niet als no-car gelabeld of uit hun bestandsnaam afgeleid.

Voor en na gebruikt: `gridtag eval --labels work/task10a-labels.csv --entrylist samples/entrylist.csv --session samples/session.example.json`, met dezelfde inhoud als de bewaarde smoke-labels.

| Metriek | Voor | Na |
| --- | --- | --- |
| Foto's | 1 | 1 |
| Auto precision / recall / review rate | 0% / 0% / 0% | 0% / 0% / 0% |
| Seconden/foto | 0,3860 | 0,0490 |
| Reden | `no_car_detected`: 1 | `no_car_detected`: 1 |
| Go/no-go | NO-GO | NO-GO |

Zonder detectorconfiguratie gebruikt de CLI `NullCarDetector`; de evidence-readers worden in deze rooktest dus niet uitgevoerd. De tijdverschillen omvatten JPEG-previewverwerking en opstart/cache-effecten, en bewijzen geen verbetering van evidence-inferentie. Een inhoudelijke voor/nameting met evidence blijft afhankelijk van getrainde compatibele modellen en een gecontroleerde gelabelde fotoset. Decoder-, onzekerheids- en conflictgedrag worden wel door unit tests gecontroleerd.

Taak 10a, 2 oktober 2026: voor en na uitgevoerd met dezelfde opdracht en ongewijzigde matchingdrempels:

```powershell
dotnet run --project src/XiPHiAS.GridTag.Cli -- eval --labels samples/labels.example.csv --entrylist samples/entrylist.csv --session samples/session.example.json
```

| Metriek | Voor | Na |
| --- | --- | --- |
| Foto's | 4 | 4 |
| Auto precision | 0% | 0% |
| Recall | 0% | 0% |
| Review rate | 0% | 0% |
| Seconden/foto | 0,0004 | 0,0004 |
| Reden | `no_preview`: 4 | `no_preview`: 4 |
| Go/no-go | NO-GO | NO-GO |

De samplepaden zijn hier niet aanwezig. Deze eerdere run bewijst geen herkenningskwaliteit of snelheid van evidence-inferentie. De pipeline-aansluiting is met fake readers getest; destijds ontbraken concrete classifier/OCR-implementaties, modelgewichten en een bereikbare gelabelde fotoset.

Historische meting van de eerdere uitbreiding:

De bestaande sample/eval-run vóór deze uitbreiding had geen automatische detecties: `auto precision 0%`, `recall 0%`, `review rate 0%`, en `no_car_detected` voor alle 241 labels. Zonder modelconfiguratie blijven de na-metingen gelijk; de nieuwe evidencebronnen worden pas meetbaar zodra echte modelobservaties of timingdata worden aangesloten. Thresholds zijn niet aangepast.
