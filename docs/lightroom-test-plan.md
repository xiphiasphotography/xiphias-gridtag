# Lightroom testplan

Dit plan beschrijft handmatige tests in Lightroom Classic. De tests zijn nodig omdat de Lightroom SDK niet beschikbaar is in de gewone .NET-testomgeving.

## Voorbereiding

- Gebruik een kopie van een catalogus en enkele testfoto's.
- Installeer de plugin uit `lightroom/XiPHiAS.GridTag.lrdevplugin`.
- Configureer `gridtag.exe`, `entrylist.csv` en `session.json`.
- Controleer dat de testcatalogus eigen Photo Mechanic keywords en metadata bevat.
- Maak testsets met 5 foto's en met 300 foto's.

## Basisworkflow

- [ ] Selecteer Picks en start `XiPHiAS GridTag: tag Picks`.
- [ ] Controleer dat alleen foto's met `pickStatus == 1` in `manifest.json` staan.
- [ ] Controleer dat niet-Picks niet veranderen en niet worden verwerkt.
- [ ] Controleer dat het CLI-proces buiten een Lightroom write gate draait.
- [ ] Controleer een succesvolle run met `results.json` schemaVersion 1.
- [ ] Controleer dat een ongeldige of ontbrekende schemaVersion vóór cataloguswrites wordt geweigerd.
- [ ] Controleer exitcodes 0, 1, 2 en 3 met passende Nederlandstalige foutmelding.
- [ ] Controleer ontbrekende executable, entrylist en session.json.
- [ ] Controleer ontbrekende, dubbele en onbekende photo-id's in results.json.

## Metadata en eigenaarschap

- [ ] Controleer dat `headline`, `caption`, `altTextAccessibility`, `extDescrAccessibility` en `personShown` correct worden geschreven.
- [ ] Controleer dat status, nummer, confidence, reasons, session, keywords en toolVersion in de XiPHiAS GridTag-velden terechtkomen.
- [ ] Controleer dat creator, credit, copyright, locatie, event, TransmissionReference, rating, label, Pick/Reject en develop-instellingen ongewijzigd blijven.
- [ ] Controleer dat bestaande niet-XiPHiAS GridTag keywords behouden blijven.
- [ ] Controleer dat RAW-bestanden en XMP-sidecars niet worden gewijzigd.

## Idempotentie en collections

- [ ] Voer dezelfde run tweemaal uit.
- [ ] Controleer dat XiPHiAS GridTag-keywords niet worden gedupliceerd.
- [ ] Controleer dat oude XiPHiAS GridTag-keywords worden verwijderd wanneer de tweede run andere keywords oplevert.
- [ ] Controleer dat niet-XiPHiAS GridTag keywords bij beide runs blijven bestaan.
- [ ] Controleer dat `auto` en `manual` uit `GridTag Review` en `GridTag GeenAuto` worden verwijderd.
- [ ] Controleer dat `review` in `GridTag Review` komt.
- [ ] Controleer dat `noCar` in `GridTag GeenAuto` komt.
- [ ] Controleer dat `error` in `GridTag Review` komt en de reden zichtbaar blijft.
- [ ] Voer de run opnieuw uit en controleer dat collections niet dubbel worden aangemaakt en memberships stabiel blijven.

## Manual flow

- [ ] Selecteer een foto, vul `manualNumber` met `69` in en start `XiPHiAS GridTag: verwerk handmatige nummers`.
- [ ] Controleer status `manual`, velden, keywords en personShown.
- [ ] Vul twee nummers in als `69, 3`.
- [ ] Controleer dat 69 de primaire auto is en dat keywords/persons van beide auto's worden toegevoegd.
- [ ] Controleer dat een onbekend nummer status `review` en `unknown_number:<n>` oplevert.
- [ ] Controleer dat een latere automatische run een `manual` resultaat niet overschrijft.

## Person Shown

- [ ] Test één rijder.
- [ ] Test twee rijders en controleer het scheidingsteken.
- [ ] Test drie of meer rijders.
- [ ] Test de voorkeur `personSeparator` met `", "` en een alternatief teken.
- [ ] Noteer of Lightroom meerdere namen in `personShown` afzonderlijk, als één tekst of anderszins behandelt.

## Ongeverifieerde Lightroom SDK-items

- [ ] `LrTasks.pcall` rond `LrTasks.execute` is yield-safe en geeft de juiste exitcode terug.
- [ ] Windows quoting werkt met spaties in executable-, manifest-, entrylist- en session-paden.
- [ ] `catalog:createCollection(name, nil, true)` hergebruikt de bestaande collectie.
- [ ] `collection:addPhotos` en `collection:removePhotos` werken idempotent.
- [ ] Een plugin met de gedeclareerde `LrSdkVersion` laadt op de gebruikte Lightroom-versie.
- [ ] `photo:setRawMetadata` ondersteunt alle gebruikte accessibility- en custom metadata-velden op de gebruikte SDK.

## Schaal en write gates

- [ ] Voer een run uit met 5 foto's en controleer de resultaten.
- [ ] Voer een run uit met 300 foto's.
- [ ] Controleer dat de default chunk size ongeveer 50 is.
- [ ] Controleer dat Lightroom tijdens de CLI-run responsief blijft.
- [ ] Controleer dat één foutfoto de overige foto's niet blokkeert.
- [ ] Controleer dat een gedeeltelijke metadata-write als fout/review zichtbaar wordt.

## Wat nog ongetest blijft

Zonder een echte Lightroom Classic-installatie, catalogus en de juiste SDK-versie zijn de SDK-items hierboven niet geverifieerd. De .NET-tests bewijzen alleen de CLI-, JSON- en Core-contracten; zij bewijzen niet dat Lightroom metadata, collections, write gates, menu's of Windows command quoting correct uitvoert. Noteer iedere echte uitkomst in `docs/open-questions.md`.
