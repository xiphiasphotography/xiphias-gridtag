# Taak 10c — optionele timingcontrole

Datum: 2 oktober 2026. Timing is lokale, optionele `IEvidence` naast de
automodel- en naam-evidence. Er zijn geen matchingthresholds aangepast.

## Gebruik

Voeg aan `gridtag run` of `gridtag eval` toe:

```text
--timing-csv passing-times.csv --clock-offset 00:00:05
```

De bestaande CSV-vorm is `number;time`, UTF-8 met of zonder BOM. De header
wordt gecontroleerd; aangehaalde cellen, lege regels en herhaalde passings
zijn toegestaan. Elke passing vereist een niet-leeg nummer en een volledige
datum en tijd, bijvoorbeeld `007;2026-10-02T14:00:05.123`.
ISO-tijden met `T` en optionele offset, of `yyyy-MM-dd HH:mm:ss`, worden
geaccepteerd, met maximaal zeven decimalen. Alleen een tijd zonder datum
en cultuurafhankelijke datums worden afgewezen. `007` blijft verschillend van `7`.
Een voorbeeld staat in [passing-times.example.csv](../samples/passing-times.example.csv);
de voorbeeldgegevens zijn fictief.

De gecorrigeerde tijd is **cameratijd + klokoffset**. Cameratijd 14:00:00
met offset `00:00:05` wordt dus 14:00:05. Voor een camera die vijf seconden
voorloopt gebruik je `-00:00:05`. Standaard is de offset nul. Het formaat is
`[-][dagen.]hh:mm:ss[.fractie]`. Een ongeldige offset of een offset zonder
timing-CSV geeft invoerfout, exitcode 3.

Bij `run` komt de cameraklok uit `manifest.captureTime`; bij `eval` uit
EXIF `DateTimeOriginal` inclusief subseconden. Geen bestandsdatum of vaste
voorbeeldtijd wordt als timingbewijs gebruikt. De vergelijking gebruikt de
kloktijden zoals geschreven en negeert tijdzone-offsets, gelijk aan de
burstlogica. Lever foto- en timingklokken in dezelfde lokale tijd aan.
De klokoffset geldt uitsluitend voor timing-evidence; sessies en burstgaps
blijven gebaseerd op de oorspronkelijke cameraklok.

## Beslissingen

Nummerherkenning komt eerst. Alleen een onzekere bestaande entrylist-kandidaat
wordt gecontroleerd. Timing maakt nooit zelf een deelnemer aan. Reeds zekere
auto-matches en handmatige nummers worden niet opnieuw door timing beoordeeld.

Voor een kandidaat wordt de dichtstbijzijnde passing van datzelfde nummer
gebruikt. Binnen de bestaande tolerantie van twee seconden is het gewicht
`1 + 0.5 * (1 - afstand / 2)`: exact gelijk geeft 1.5, op de grens 1.
Een bekende passing buiten de tolerantie geeft gewicht 0.1 en dwingt bij de
onzekere match `evidence_conflict:timing` en `review` af. Een andere ondersteunende
bron kan dat sterke conflict niet verbergen. Een review schrijft geen metadata.

Een ontbrekende fototijd, ontbrekende kandidaatpassings, lege timinglijst,
onbekende deelnemer of een offset die de datum buiten het geldige bereik brengt
blijft neutraal (gewicht 1). Negatieve of nul-toleranties in de C#-API worden
afgewezen. De CLI behoudt de bestaande twee seconden.

Deze controle veronderstelt dat passing times bij de gefotografeerde passage
horen. Foto's elders op het circuit, incomplete timingdata, verschillende
sessies of een verkeerde klokoffset kunnen ten onrechte conflict geven.
Timing is daarom expliciet optioneel; een ontbrekende passing is geen bewijs
dat een auto afwezig was. Een CSV met meerdere evenementen moet vooraf op het
juiste evenement worden geselecteerd.

## Verificatie

Regressietests controleren steun/conflict, de tolerantiegrens, positieve en
negatieve offsets, de dichtstbijzijnde van meerdere passings, ontbrekende tijden,
datumoverflow, voorloopnullen, BOM/aangehaalde CSV-cellen en ongeldige invoer.
Pipeline-tests controleren onzekerheids-gating en behoud van handmatige en
reeds automatische matches. Een CLI-test controleert dat timing-eval zonder
burstopties de echte EXIF-tijd gebruikt via sessieresolutie.

Bij de aangehaalde CSV-test bleek de gedeelde parser een quote na een
scheidingsteken af te wijzen. Die fout is met de falende test hersteld; de
bestaande entrylist-tests blijven van toepassing.

`dotnet build XiPHiAS.GridTag.slnx --no-restore` slaagt zonder warnings of errors.
`dotnet test XiPHiAS.GridTag.slnx --no-build --no-restore` slaagt: **165 tests**
(140 Core, 25 CLI).

Er is geen echte passing-times-CSV gevonden bij de foto's. Daarom is uitsluitend
een neutrale rooktest uitgevoerd op `.training/images2`, met dezelfde labels,
entrylist, YOLOX en PaddleOCR als de [vorige voormeting](task10-images2-evaluation.md):

```powershell
dotnet run --project src/XiPHiAS.GridTag.Cli --no-build -- eval --labels work/task10-images2-labels.csv --entrylist work/task10-images2-entrylist.csv --session samples/session.example.json --vision-config samples/detector.yolox.json --number-ocr Models/driver-name/PP-OCRv6_det_small.onnx --number-ocr-model Models/driver-name/latin_PP-OCRv5_rec_mobile.onnx --number-ocr-dictionary Models/driver-name/ppocrv5_latin_dict.txt --timing-csv work/task10c/empty-passing-times.csv --clock-offset 00:00:05 --out work/task10c/empty-timing-eval.json
```

De lege CSV bevat alleen de header. Resultaat: 9 labelregels, 1 correcte auto,
0 foutieve auto, 7 review, 1 ontbrekende foto; precision 100%, recall 11.11%,
5.5366 seconden per labelregel, **NO-GO**. Fotobeslissingen zijn gelijk aan
de eerdere voormeting. Dit toont neutraal gedrag, geen kwaliteitswinst door
timing. Echte kwaliteit moet nog met onafhankelijke passing times worden gemeten;
er zijn geen passings uit de fotolabels verzonnen.

De result-/manifestcontracten blijven schema 1. Er is geen Lightroom-instelling
toegevoegd; de timingbron is beschikbaar via CLI en de C#-pipeline.
