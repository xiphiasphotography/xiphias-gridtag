# Taak 10b — burstvoorstellen voor review

Burstpropagatie is een optionele tweede stap na de onafhankelijke herkenning
van de volledige batch. Core bepaalt welke nummers mogen worden voorgesteld;
Vision verzorgt beeldgelijkenis en, voor `eval`, het lezen van EXIF-tijden.

## Gebruik

Voeg aan een bestaande `run`- of `eval`-opdracht toe:

```text
--burst-max-gap 2 --burst-similarity 0.95
```

Zonder beide opties blijft burstpropagatie uitgeschakeld. Zodra minstens één
optie is opgegeven, is de standaard voor de andere optie 2 seconden respectievelijk
0.95. Decimalen in CLI-opties gebruiken een punt. Deze grenzen zijn afzonderlijke
burstgrenzen; de bestaande OCR/evidence-matchingthresholds veranderen niet.
De Lightroom-instellingen hebben nog geen eigen burstschakelaar; deze koppeling
is beschikbaar via de CLI en de C#-batchpipeline.

Bij `run` is `manifest.photos[].captureTime` de camerakloktijd die de Lightroom-
adapter aanlevert. Bij `eval` met burstopties wordt `DateTimeOriginal` uit de foto
gelezen, inclusief `SubSecTimeOriginal`. Ontbrekende EXIF-tijden worden niet
afgeleid uit bestandsnamen, CSV-volgorde of bestandsdatums. De betreffende foto
doet dan niet mee aan burstpropagatie. Offsets worden genegeerd: tijdafstanden
gebruiken dezelfde cameraklokinterpretatie als sessieresolutie.

## Regels

- Alleen een onafhankelijk `auto`- of gevalideerd `manual`-resultaat met precies
  één primaire, in de entrylist aanwezige auto mag bron zijn. Multi-car-bronnen
  worden overgeslagen om geen primaire auto aan een ander frame toe te schrijven.
- Alleen bestaande `review`-foto's zonder handmatige nummerinvoer krijgen
  voorstellen. `auto`, `manual`, `noCar` en `error` blijven ongewijzigd.
- Bron en doel moeten in dezelfde map en dezelfde opgeloste sessie liggen.
  Zowel eerdere als latere frames komen in aanmerking. Beide grenswaarden
  zijn inclusief.
- Vision vergelijkt 32×32 RGB-previews: de score is één minus de genormaliseerde
  gemiddelde absolute kleurafstand. Dit vergelijkt ruimtelijk beeldgebruik en
  kleur, zonder nieuw model, upload of dependency. Alleen kleine descriptors
  worden gedurende de run gecachet.
- De score is **beeldgelijkenis, geen kans dat de auto dezelfde is**. Een vrijwel
  gelijke achtergrond kan ook een andere auto bevatten. Daarom blijven alle
  voorstellen review en ontbreken gegenereerde IPTC-velden.
- Meerdere bronnen met hetzelfde exacte nummer worden gededupliceerd. De bron
  met hoogste gelijkenis wint, gevolgd door kleinste tijdafstand en laagste ID.
  `007` en `7` blijven afzonderlijke kandidaten.
- Verschillende voorgestelde nummers blijven naast elkaar zichtbaar met reden
  `burst_ambiguous`; de processor kiest geen automatische winnaar.
- Alleen de oorspronkelijke herkende bronresultaten worden gebruikt. Een
  burstvoorstel kan geen nieuwe bron worden: geen ketenpropagatie.
- Onbruikbare previews leveren geen voorstel. Een onverwachte vergelijkingsfout
  houdt het oorspronkelijke review-resultaat intact en voegt een foutreden toe.
  Annulering wordt niet als zo'n vergelijkingsfout behandeld.

## Resultaten en review

Het bestaande JSON-schema blijft versie 1. Voorstellen gebruiken bestaande
`cars`-velden: `source: "burst"`, `primary: false` en `confidence` als gemeten
beeldgelijkenis. Ze zijn geen opgeloste deelnemers. Bestaande niet-burst-
autokandidaten blijven bewaard. Bij toepassing van een voorstel is `fields`
afwezig en blijft `status: "review"`.

Voorbeeld van een onopgelost buurframe van bronfoto 12:

```json
{
  "id": 13,
  "status": "review",
  "reasons": [
    "largest_car_unresolved",
    "burst_propagation_review",
    "burst_source:12:007"
  ],
  "cars": [
    { "number": "007", "confidence": 0.98, "source": "burst", "primary": false }
  ]
}
```

De bestaande Lightroom-adapter schrijft IPTC-velden alleen voor `auto` en
`manual`. Een burstresultaat blijft in Review. Bevestigen gebeurt met de bestaande
handmatige nummerinvoer en de normale C#-validatie tegen de entrylist.
`ProcessPhoto` blijft onafhankelijke herkenning; burstvoorstellen worden alleen
gemaakt via `TaggingPipeline.Process` op de volledige manifestbatch.

`gridtag eval` gebruikt nu batchverwerking en telt de tijd daarvan mee, inclusief
EXIF-lezen en burstvergelijking. De CLI meldt `burst review photos`. Voorstellen
worden niet meegerekend als correcte automatische matches: auto-rate, precision
en auto-recall nemen niet toe door de aanwezigheid van reviewvoorstellen.

## Verificatie op 2 oktober 2026

Alle 181 JPEG's in `.training/images` zijn gecontroleerd: **0 bevatten een
leesbare EXIF DateTimeOriginal**. Een echte burstkwaliteitstest op deze set is
dus niet mogelijk. De huidige CSV levert alleen bestandsnamen en nummers.
De tijden zijn niet verzonnen of uit de sorteervolgorde afgeleid.

Een lokale CLI-rooktest in `work/burst-smoke` gebruikt drie kopieën van één
foto met label `007`, met expliciete gecontroleerde manifesttijden. Dit test
de aansluiting en grenzen, niet bewegings- of burstkwaliteit. Eén bron heeft
gevalideerde handmatige invoer `007`; de nummerreader is uitgeschakeld zodat
de twee autodetecties review blijven:

| Frame | Tijdafstand | Zonder burst | Met burst |
| --- | ---: | --- | --- |
| Bron | 0 s | manual 007 | manual 007 |
| Nabij | 1 s | review, geen kandidaat | review, kandidaat 007, source burst |
| Veraf | 10 s | review, geen kandidaat | review, geen kandidaat |

De gelijke kopieën scoren 1.0. Alleen de bron heeft metadatafields; het nabije
burstframe heeft geen fields en wordt nooit auto. `eval` op deze EXIF-loze
kopieën levert 3 review, 0 auto en 0 burstvoorstellen.

Regressietests controleren tijd-/sessie-/mapgrenzen, ontbrekende timestamps en
previews, niet-eindige scores, ambigue bronnen, deduplicatie, behoud van `007`,
idempotentie, uitsluiting van multi-car-bronnen, geen ketenpropagatie, batch-
aansluiting, beschermde statussen en annulering. Een onafhankelijke evaluatietest
controleert dat burst-review de automatische recall niet verhoogt.

Afgeronde checks:

```powershell
dotnet build XiPHiAS.GridTag.slnx --no-restore
dotnet test XiPHiAS.GridTag.slnx --no-build --no-restore
```

Build: 0 warnings en 0 errors. Tests: **142 geslaagd** (120 Core, 22 CLI),
inclusief een echte EXIF-APP1-fixture met DateTimeOriginal en subseconden.
`git diff --check` is schoon. Gegenereerde rooktestbestanden blijven onder
de genegeerde `work/`-map; er zijn geen foto's, modellen of buildoutputs gecommit.

Voor een inhoudelijke kwaliteitsmeting zijn opeenvolgende RAW's/JPEG's met
originele capturetijden (of een Lightroom-manifest met die tijden) en labels
nodig. De eenvoudige gelijkenisscore en de standaard 0.95 zijn voorlopig niet
op een echte bursttestset gekalibreerd.
