# AGENTS.md — XiPHiAS GridTag

Instructions for coding agents (Codex) working in this repository. Read this file fully before changing anything. Nested `AGENTS.md` files (e.g. `lightroom/AGENTS.md`) add rules for their folder and win on conflict.

## 1. What this project is

**XiPHiAS GridTag** recognises start numbers on motorsport cars in RAW photos, validates the identification against the event entry list and optional visual evidence (car make/model and logos), and adds the matching IPTC metadata (headline, description, keywords, drivers) to those photos in **Lightroom Classic (LrC)**. XiPHiAS GridTag is run only after the owner has selected and edited the photos; it is not a culling, editing or export application.

Two cooperating parts:

1. **`gridtag` CLI + libraries (.NET, C#)** — all application and recognition logic: RAW preview → car detection → number reading → optional visual evidence → validation against an entry list → generated metadata fields.
2. **LrC plugin (Lua)** — deliberately thin glue: collects the Picks, calls the CLI, applies the results to the LrC catalog, and provides a manual-correction flow. Do not move recognition or business logic into Lua.

The owner primarily develops in JavaScript and has some C# experience. Prefer straightforward, readable C# that is easy to follow from a JavaScript background; avoid clever framework abstractions. JavaScript/Node/Electron are **not** runtime dependencies of XiPHiAS GridTag unless the owner explicitly asks for them. Communicate with the owner in Dutch if asked; keep code, identifiers and comments in English (user-facing plugin strings in Dutch).

## 2. Fixed workflow (do not redesign)

```
Photo Mechanic: base IPTC (event/session constants)   ->  folder "yyyy-mm-dd - event\raw"
FastStone Viewer: move selection                       ->  folder "yyyy-mm-dd - event"
Lightroom Classic: owner edits photos, then sets final Pick flag
XiPHiAS GridTag: only those Picks are analysed and get car-specific metadata
Lightroom Classic: review (incl. XiPHiAS GridTag review collection), export
```

Consequences:
- Event/session constants (creator, credit, copyright, location, event name, `TransmissionReference`) are **owned by Photo Mechanic**. XiPHiAS GridTag never writes them.
- XiPHiAS GridTag processes **Picks only** (`pickStatus == 1`) after the owner has finished selection/editing. It does not rate, cull, edit, render or export photos.
- XiPHiAS GridTag writes into the **LrC catalog via the plugin SDK**, not into XMP sidecars.
- The Lightroom plugin is only an adapter/UI layer. The C# CLI owns domain logic, matching, field generation and all vision integration.

## 3. Non-negotiables

1. **Precision over recall.** A wrong automatic tag is worse than no tag. When unsure → status `review`. Never lower thresholds to make numbers look better; change thresholds only with evaluation data (see §9).
2. **Local only.** No cloud OCR, no uploads, no telemetry. Photos never leave the machine.
3. **Never modify RAW files.** The .NET side never writes XMP sidecars or any image file. Only the Lua plugin writes metadata, and only into the LrC catalog.
4. **Field ownership.** XiPHiAS GridTag owns exactly: `headline`, `caption`, `altTextAccessibility`, `extDescrAccessibility`, `personShown`, and the keywords it created itself (tracked in plugin metadata `keywords`). It must not touch anything else (rating, label, develop settings, other keywords, PM fields).
5. **Idempotent.** Re-running on the same photo must not duplicate keywords or leave stale XiPHiAS GridTag keywords behind.
6. **Contracts are versioned.** Every JSON file has `schemaVersion`. Breaking changes bump it and update `docs/contracts.md`, the C# records and the Lua code in the same change.
7. **Manual overrides win.** A `manual` status is never overwritten by automatic runs.
8. **Never invent SDK behaviour.** LrC SDK facts in §10 are marked verified / unverified. Do not rely on unverified behaviour without adding a test note in `docs/open-questions.md`.
9. **Number first, evidence second.** Start-number recognition is the primary identification signal. Car make/model and logo recognition are validation/disambiguation evidence; they may strengthen a listed candidate or force `review` on conflict, but must not silently invent a participant.
10. **Keep the runtime simple.** C#/.NET + the thin Lightroom Lua plugin are the application stack. Do not introduce Node.js, Electron, a web UI, a service or a database server without asking.

## 4. Architecture

```
LrC plugin (Lua)                         gridtag CLI (.NET)
  collect Picks                            read manifest.json
  write manifest.json  ───────────────►    read entrylist.csv + session.json
  LrTasks.execute(gridtag run ...)         per photo: session → preview → detect → read number → evidence → match → build fields
  read results.json    ◄───────────────    write results.json (UTF-8 without BOM)
  apply to catalog (write gates, chunks)
  create/update review collections
```

Exchange is via files in a temp folder. The CLI has no dependency on Lightroom; the plugin has no dependency on the vision models. Keep it that way.

### Photo status model

| status   | meaning                                                           | plugin action                                  |
|----------|-------------------------------------------------------------------|------------------------------------------------|
| `auto`   | confident match, fields generated                                 | write fields + keywords                        |
| `review` | car found but number unresolved/ambiguous, or error in reasoning  | add to collection `GridTag Review`             |
| `noCar`  | no car detected (pit, portrait, crowd)                            | add to collection `GridTag GeenAuto`           |
| `manual` | number entered by a human and validated against the entry list    | write fields + keywords, remove from Review    |
| `error`  | exception while processing this photo                             | add to Review; message in `error`              |

## 5. Target repository layout

Create this structure in Phase 0 (see §11). Do not add other top-level projects without asking.

```
AGENTS.md  README.md  XiPHiAS.GridTag.slnx  Directory.Build.props  .editorconfig  .gitignore
docs/          contracts.md  architecture.md  open-questions.md  reference/*.xmp (golden examples)
samples/       entrylist.csv  session.example.json  manifest.example.json  results.example.json
src/
  XiPHiAS.GridTag.Core/      domain, entry list, matching, field building, pipeline, JSON contracts  (no I/O to images, no ONNX)
  XiPHiAS.GridTag.Vision/    RAW preview + vision implementations behind interfaces: ICarDetector / IPlateReader first; later car-model and logo evidence classifiers; stubs first
  XiPHiAS.GridTag.Cli/       gridtag.exe: run | check-entrylist | fields | eval | version
tests/XiPHiAS.GridTag.Core.Tests/   xUnit
tools/                      Python training/export scripts (later; not part of the .NET build)
lightroom/XiPHiAS.GridTag.lrdevplugin/   Info.lua, Runner.lua, Prefs.lua, Settings.lua, Metadata + Tagset, json.lua (rxi, MIT)
```

Dependency direction: `Cli → Vision → Core` and `Cli → Core`. `Core` references nothing but the BCL.

## 6. Data and contracts

### Entry list (`samples/entrylist.csv`)
UTF-8 **with BOM**, delimiter `;`, header:
`number;team;car;class;driver_1;driver_1_nat;driver_2;driver_2_nat` (loader must also accept `driver_3`, `driver_3_nat`, …).
Numbers are unique strings (`"3"`, `"69"`, `"991"`). Normalize input numbers: trim, strip leading `#`, uppercase, strip leading zeros (`"003"` → `"3"`, `"0"` stays).
Names contain non-ASCII characters (e.g. Söderström). Never assume ASCII.

### `session.json` (event-level context, provided per event)
```json
{
  "seriesName": "GT World Challenge Europe",
  "eventFullName": "GT World Challenge Europe powered by AWS",
  "location": "Circuit Zandvoort",
  "defaultSession": "FP2",
  "sessions": [
    { "code": "FP2", "name": "Free Practice 2", "start": "2026-09-18T13:30:00", "end": "2026-09-18T15:00:00" }
  ]
}
```
Session is resolved from the photo's capture time (clock time as written, ignore offset); fallback `defaultSession`; else no session (use the `*NoSession` templates, omit the session keyword). Times in the sample are placeholders.

### `manifest.json` (plugin → CLI)
```json
{
  "schemaVersion": 1,
  "photos": [
    { "id": 12345, "uuid": "…", "path": "D:\\2026-09-18 - GTWC\\_MWR1234.ARW",
      "captureTime": "2026-09-18T13:52:10", "manualNumber": null }
  ]
}
```
`id` = `LrPhoto.localIdentifier`. `manualNumber` is set only in manual mode; it may hold several numbers separated by comma, semicolon or space (first = primary car).

### `results.json` (CLI → plugin)
```json
{
  "schemaVersion": 1, "toolVersion": "0.1.0", "generatedAt": "2026-09-18T14:10:00Z",
  "photos": [
    { "id": 12345, "status": "auto", "reasons": [], "session": "FP2",
      "cars": [ { "number": "69", "confidence": 0.98, "source": "ocr", "primary": true } ],
      "fields": {
        "headline": "#69 Emil Frey Racing Ferrari 296 GT3 EVO",
        "caption": "…", "altText": "…", "extDescription": "…",
        "keywords": ["Emil Frey Racing", "…", "#69", "Free Practice 2", "PRO"],
        "persons": ["Thierry Vermeulen", "Ben Green"] } },
    { "id": 12346, "status": "review", "reasons": ["confusable:59,66,89,99"], "cars": [], "session": "FP2" }
  ]
}
```
JSON rules: camelCase, enums as camelCase strings (`auto|review|manual|noCar|error`), null properties omitted, **UTF-8 without BOM**, readable (relaxed escaping) output. The Lua side uses `json.lua` (rxi) — keep payloads simple (no dates as objects, no nested polymorphism).

CLI exit codes: `0` ok · `1` unexpected error · `2` usage error · `3` invalid/missing input file. Per-photo failures do **not** fail the run; they become `status: "error"`.

## 7. Field generation (golden behaviour)

Templates are data (`EventContext.Templates`), defaults are Dutch. Placeholders: `{nr} {team} {car} {class} {drivers} {verb} {session} {series} {event} {location}`. Unknown placeholder → exception (no silent empty output).

| Field | Default template |
|---|---|
| headline | `#{nr} {team} {car}` |
| caption | `{session} - {drivers} {verb} de {car} met startnummer #{nr} voor {team} tijdens de {event} op {location}.` |
| caption (no session) | `{drivers} {verb} de {car} met startnummer #{nr} voor {team} tijdens de {event} op {location}.` |
| altText | `Raceauto nummer {nr} van {team} in actie op {location} tijdens de {series}.` |
| extDescription | `De {car} met startnummer {nr} van {team} rijdt op {location} tijdens {session} van de {series}.` |
| extDescription (no session) | `De {car} met startnummer {nr} van {team} rijdt op {location} tijdens de {series}.` |

- `{drivers}`: `Name (NAT)` per driver; 2 → `A en B`; 3+ → `A, B en C`; no nationality → name only.
- `{verb}`: `rijdt` for one driver, `rijden` for more.
- **Keywords order**: per car `team, car, drivers…, #nr`; then session name; then distinct classes. Case-insensitive de-duplication, first wins.
- **Persons**: all drivers of all cars, distinct.
- **Several cars in one photo**: headline/caption/alt/ext use the **primary** car (largest detection, must be resolved); keywords and persons are the union.

**Golden tests are mandatory.** `docs/reference/003-*.xmp` (#3) and `docs/reference/069-*.xmp` (#69) are real expected outputs. Tests parse these XMP files (XDocument) and assert: `photoshop:Headline`, `dc:description`, `Iptc4xmpCore:AltTextAccessibility`, `Iptc4xmpCore:ExtDescrAccessibility`, `Iptc4xmpExt:PersonInImage`, and that the **last N items of `dc:subject`** equal the generated keywords. (The leading items of `dc:subject` are Photo Mechanic constants and are out of scope.)

## 8. Matching (number validation)

Vision returns an **n-best list** `NumberHypothesis(text, probability)`. Core decodes against the entry list (lexicon decoding); it never trusts the raw top string.

`NumberMatcher.Match`:
1. Normalize each hypothesis; sum probability per entry-list number; mass of non-listed hypotheses = `outOfList`.
2. Optional `IEvidence` (first car make/model, then logo; later driver-name text, focus point and timing) multiplies candidate probability by a weight in `[0, 1.5]`, capped at 1. Evidence is always evaluated against entry-list candidates; it never creates a participant that is absent from the entry list.
3. Best candidate + runner-up + margin. Decision `Auto` only if **no** reason applies; else `Review` with reasons. No listed hypothesis → `NoMatch` (`no_reading` / `no_entry_match`).

Reasons and defaults (all in `MatchOptions`, all tunable via evaluation only):

| reason | rule |
|---|---|
| `low_confidence` | best < 0.90 |
| `small_margin` | best − runnerUp < 0.30 |
| `out_of_list_mass` | outOfList > best (raw) |
| `confusable:<n,…>` | best has a listed neighbour differing in one digit by a confusable pair and best < 0.97 |
| `substring_risk:<n,…>` | best is contained in other listed numbers (`5` ⊂ `55` ⊂ `555`, `99` ⊂ `991`) and best < 0.98 |
| `evidence_conflict:<name>` | an evidence weight for best < 0.25 |

Default confusable digit pairs: `0-8 1-7 2-7 3-8 5-6 6-8 6-9 8-9`. Example cluster: 59, 66, 69, 96, 99 — car make/model is the preferred first disambiguator (Ferrari / McLaren / Audi / Lamborghini); a recognised manufacturer/team logo may provide weaker supporting evidence. A strong model/logo conflict with the number candidate must result in `review`, not an automatic correction.

Pipeline rule: the **largest** detection must resolve as `Auto`, otherwise the photo is `review`. Smaller unresolved cars are dropped with note `secondary_unresolved` and do not block the photo.

## 9. Evaluation (go/no-go for the recognition work)

Build `gridtag eval --labels labels.csv --entrylist … --session …` before investing in models. `labels.csv`: `path;numbers` (comma separated; empty = no car). Report: auto-rate, **precision of auto tags**, recall, review-rate, per-reason counts, confusion pairs, seconds/photo.

Proof-of-concept criteria on a held-out set: ≥ 80 % of photos with a legible number correct · ≤ 2 % wrong automatic tags · ≤ 2 s/photo on the owner's hardware. Manual corrections from the plugin become new labelled data.

## 10. Lightroom Classic SDK notes (Lua)

Environment: Lua 5.1, plugin runs inside LrC, `import`/`require` of plugin-local modules, globals `_PLUGIN`, `WIN_ENV`, `MAC_ENV`. No built-in JSON (use `json.lua`). Menu scripts must run in an async task (`LrFunctionContext.postAsyncTaskWithContext`).

**Verified in the official LrPhoto reference:** `photo:setRawMetadata` keys `headline`, `caption`, `title`, `personShown` (single string), `altTextAccessibility` and `extDescrAccessibility` (SDK 13.2+); raw metadata `pickStatus` (1 = Pick, −1 = rejected), `path`, `uuid`, `dateTimeOriginalISO8601`; `photo:setPropertyForPlugin/getPropertyForPlugin`; `photo:addKeyword/removeKeyword`; `catalog:createKeyword(name, synonyms, includeOnExport, parent, returnExisting)`; `catalog:withWriteAccessDo`.

**Do not use:** `photo:saveMetadata()` / `photo:readMetadata()` — undocumented, timing and dialog issues reported. The design deliberately avoids reading/writing XMP sidecars.

**Unverified — test in LrC and record results in `docs/open-questions.md`:**
- `personShown` with multiple drivers (separator; configurable pref `personSeparator`, default `", "`).
- `LrTasks.pcall` as the yield-safe pcall around `LrTasks.execute`.
- Windows quoting for `LrTasks.execute` (wrap the whole command in one extra pair of quotes).
- `catalog:createCollection(name, nil, true)`, `collection:addPhotos/removePhotos`.
- Whether `LrSdkVersion` above the running LrC's SDK still loads (min version 6.0 is set).

Plugin rules: keep write gates short (chunks of ~50 photos, default `chunkSize`); run the CLI **outside** any write gate; declare custom metadata (`status`, `number`, `manualNumber`, `confidence`, `reasons`, `session`, `keywords`, `toolVersion`) with `searchable = true` where useful; `status` is an enum (`auto, review, manual, noCar, error`). Toolkit identifier: `net.xiphias.gridtag` (persistent identity; preserve across branding changes).

Manual flow: user types a number in the custom field `manualNumber` for one or many photos (multi-select edit) → menu "XiPHiAS GridTag: verwerk handmatige nummers" → same CLI (`manualNumber` set) → status `manual` or `review` with `unknown_number:<n>`.

## 11. Commands

```
dotnet build XiPHiAS.GridTag.slnx
dotnet test  XiPHiAS.GridTag.slnx
dotnet run --project src/XiPHiAS.GridTag.Cli -- run --manifest samples/manifest.example.json --entrylist samples/entrylist.csv --session samples/session.example.json --out work/results.json
dotnet run --project src/XiPHiAS.GridTag.Cli -- check-entrylist --entrylist samples/entrylist.csv
dotnet run --project src/XiPHiAS.GridTag.Cli -- fields --entrylist samples/entrylist.csv --session samples/session.example.json --number 69
```
Lua syntax check (if Lua 5.1 is available): `luac5.1 -p lightroom/XiPHiAS.GridTag.lrdevplugin/*.lua`.

Target framework is `net10.0` (single place: `Directory.Build.props`). Do not pin NuGet versions from memory: use `dotnet add package <name>` and commit what resolves. Test stack: xUnit.

## 12. Conventions

C#: nullable enabled, file-scoped namespaces, records for data, `sealed` by default, no static mutable state, constructor injection, no `async` where nothing is awaited, XML docs on public API of `Core`. Build XML/JSON with real APIs, never by string concatenation. Zero compiler warnings for new code.
Errors: throw `InvalidDataException` for bad input files; catch per photo in the pipeline only.
Lua: `local` everything, one module per file, log via `LrLogger('GridTag')`, no global state, user-visible strings in Dutch, defensive `pcall` around every `setRawMetadata` (older LrC lacks some keys).
Git: small commits, imperative subject lines, one concern per change.

## 13. Testing rules

- Every Core behaviour in §6–§8 has a unit test. Golden XMP tests are never skipped or loosened to make a build pass.
- Pipeline tests use fakes (`IRawPreviewProvider`, `ICarDetector`, `IPlateReader`); no image files needed.
- Matching tests cover: strong single hypothesis (Auto), confusable cluster (Review), substring risk (`5` vs `55`), out-of-list dominance, supportive car-model evidence, conflicting car-model/logo evidence, and no reading.
- A bug fix starts with a failing test.

## 14. Definition of done

Build and tests green · no new warnings · docs/contracts updated if any contract or default changed · `docs/open-questions.md` updated if an unverified SDK assumption was touched · owner-facing behaviour changes noted in `README.md`.

## 15. First tasks (Phase 0 → 1), in order

1. **Scaffold** the layout of §5 with the projects, `Directory.Build.props`, `XiPHiAS.GridTag.slnx`, `.editorconfig`, `.gitignore` (`*.xmp -text` in `.gitattributes`). Copy the owner-provided `_entrylist.csv` to `samples/entrylist.csv` and the two reference XMP files to `docs/reference/`. Build green.
2. **Core domain**: `EntryList` + `EntryListLoader` (BOM, `;`, quoted cells, driver_N columns), `NumberNormalizer`, `ConfusionMap`, `EntryListAnalysis` (confusables, substring hosts). Tests incl. 45 entries, unique numbers, Söderström present.
3. **Fields**: `EventContext`, `FieldTemplates`, `TemplateRenderer`, `SessionResolver`, `FieldBuilder`. Golden tests for #3 and #69 pass.
4. **Matching**: `NumberHypothesis`, `MatchOptions`, `IEvidence`, `NumberMatcher` per §8 with tests.
5. **Pipeline + contracts**: `Manifest`, `ResultFile`, `PhotoResult`, `GridTagJson` (rules of §6), `TaggingPipeline` (manual path first, then automatic path against fakes), stub implementations in `XiPHiAS.GridTag.Vision` (`StubRawPreviewProvider` → `no_preview`, `NullCarDetector`, `NullPlateReader`).
6. **CLI**: `run`, `check-entrylist`, `fields`, `version`, exit codes of §6. Example files in `samples/`.
7. **Lightroom plugin**: `Info.lua`, metadata + tagset, `Prefs`, `Settings`, `Runner` (analyze + manual), review collections, `json.lua`. Lua syntax check clean. Then owner tests inside LrC and reports the open questions.
8. **Evaluation harness** (`gridtag eval`) — **before** any model work.
9. **Vision**: RAW preview extraction (embedded JPEG first, half-size decode as fallback), then car detection and number reading behind the existing interfaces. Keep the inference backend replaceable (for example ONNX Runtime/DirectML or WinML); do not leak backend-specific types into Core. Prefer permissively licensed models (check licences; AGPL is not acceptable for commercial use). Once number recognition has a measured baseline, add **car make/model classification as the first `IEvidence` control**, followed by optional logo recognition as supporting evidence. Re-run `gridtag eval` for every evidence/model change.
10. Later: driver-name reader; focus-point evidence; burst propagation; EXIF-time ↔ timing-data cross-check; WPF review UI only if Lightroom review proves insufficient; Python training/export tools in `tools/`.

## 16. Working agreements

- If a requirement is ambiguous, propose the smallest reasonable interpretation and note it in your summary instead of stalling. Ask before: changing a contract, adding a top-level project, adding a dependency with a non-permissive licence, or touching anything outside the field ownership of §3.4.
- Finish each task with: what changed, how it was verified (commands run), what remains, and any new open question.
