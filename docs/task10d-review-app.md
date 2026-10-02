# Taak 10d — WPF-reviewapp

De bestaande `XiPHiAS.GridTag.Review`-app biedt een lokale, alleen-lezen weergave
van foto's met status `review` en `noCar`. Lightroom blijft verantwoordelijk voor
handmatige correcties en het schrijven van catalogusmetadata.

Start vanuit de repository:

```powershell
dotnet run --project src/XiPHiAS.GridTag.Review
```

1. Kies **Open results.json**. Een `manifest.json` in dezelfde map wordt automatisch geladen.
2. Gebruik **Kies manifest** als het bijbehorende manifest ergens anders staat.
3. Filter op review, geen auto of beide. De volgorde uit de resultaten blijft behouden.
4. Selecteer een foto of gebruik **Vorige**, **Volgende** of de pijltjestoetsen in de lijst.
5. Bekijk status, Lightroom-foto-ID, sessie, redenen, kandidaten en het bronpad.

JPEG en PNG worden lokaal weergegeven. RAW-previews gebruiken de bestaande
previewprovider: eerst een ingebedde JPEG, daarna een halve-resolutiedecode via
de beschikbare Windows-codec. De preview toont het bronbestand, niet de
Lightroom-ontwikkelinstellingen. Een ontbrekende of onleesbare foto krijgt een
melding; de rest van de lijst blijft beschikbaar.

Bestanden en previews worden buiten de UI-thread geladen. Bij een nieuwe
selectie wordt de vorige previewaanvraag geannuleerd en een verouderd resultaat
niet getoond. De bestaande synchrone RAW-decoder kan een lopende decode niet
tussentijds onderbreken. Sluiten annuleert de actieve laadverzoeken.

SchemaVersion 1 wordt gecontroleerd voor resultaten en manifest. Dubbele
foto-ID's worden afgewezen. Relatieve fotopaden worden geïnterpreteerd ten
opzichte van de manifestmap. Zonder manifest kan de resultatenlijst wel worden
bekeken, maar zijn previews niet beschikbaar. Gebruik altijd het manifest van
dezelfde GridTag-run: foto-ID's alleen bewijzen niet dat twee bestanden bij
elkaar horen.

De app schrijft geen foto's, XMP, resultaten of catalogusmetadata en verandert
geen JSON-contracten. Kandidaten blijven voorstellen; correcties verlopen via
**Startnummer (handmatig)** en **verwerk handmatige nummers** in Lightroom.

Handmatige Windows-controle:

- Startvenster verschijnt; openen, filters en navigatie werken.
- Open twee resultaatbestanden achter elkaar; de lijst wordt vervangen.
- Kies snel andere foto's; er verschijnt geen preview van een vorige selectie.
- Controleer JPEG, PNG en RAW; probeer ook een ontbrekende en corrupte foto.
- Open een leeg resultaatbestand; oude details en preview verdwijnen.
- Open ongeldig JSON; de vorige sessie blijft beschikbaar.
- Sluit tijdens het laden; er verschijnt geen foutdialoog na het sluiten.

Uitgevoerde verificatie: solution-build zonder waarschuwingen of fouten;
173 tests geslaagd, waaronder acht reviewtests voor filtering/volgorde,
manifestkoppeling, ontbrekend manifest, ongeldige schema's, dubbele resultaat-ID's,
ongeldig JSON en annulering. Een lokale WPF-smokecheck bouwde het venster op,
schakelde de drie filters en renderde de inhoud voor visuele controle.
De bovenstaande interactieve controle met echte RAW-bestanden blijft nodig
om de beschikbare codecs en de volledige gebruikersworkflow te bevestigen.
