# AGENTS.md — XiPHiAS GridTag

Instructions for coding agents (Codex) working in this repository. Read this file fully before changing anything. Nested `AGENTS.md` files (e.g. `lightroom/AGENTS.md`) add rules for their folder and win on conflict.

## 1. What this project is

**XiPHiAS GridTag** recognises start numbers on motorsport cars in RAW photos, validates the identification against the event entry list and optional visual evidence (car make/model, logos, and which of a car's drivers is currently racing), and adds the matching IPTC metadata (headline, description, keywords, drivers) to those photos. XiPHiAS GridTag is run only after the owner has selected and edited the photos; it is not a culling, editing or export application.

Three cooperating parts, two of them adapters around one shared core:

1. **`gridtag` CLI + libraries (.NET, C#)** — all application and recognition logic: RAW preview → car detection → number reading → optional visual/temporal evidence → validation against an entry list → generated metadata fields. This is the only place domain logic lives.
2. **LrC plugin (Lua)** — deliberately thin glue for the normal workflow: collects the Picks, calls the CLI, applies the results to the LrC catalog, and provides a manual-correction flow. Do not move recognition or business logic into Lua.
3. **Standalone mode (same CLI, different command)** — for use without Lightroom open: point `gridtag tag` at a folder of RAW/JPG files and an entry list; it writes XMP sidecars directly. See §4 and §6a.

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
- In the normal (Lightroom) workflow, XiPHiAS GridTag writes into the **LrC catalog via the plugin SDK**, not into XMP sidecars.
- The Lightroom plugin is only an adapter/UI layer. The C# CLI owns domain logic, matching, field generation and all vision integration.

**Alternate workflow — standalone mode.** When Lightroom is not part of the workflow (or for a quick check before import), the owner points the CLI at a folder directly: `gridtag tag --folder <path> --entrylist <csv>`. This skips Photo Mechanic/FastStone/LrC entirely and writes GridTag's fields as XMP sidecars next to the originals. It is a separate command (`tag`, not `run`) with its own adapter — see §4 and §6a. The two modes share 100% of the recognition/domain code and differ only in how photos are discovered and how results are written.

## 3. Non-negotiables

1. **Precision over recall.** A wrong automatic tag is worse than no tag. When unsure → status `review`. Never lower thresholds to make numbers look better; change thresholds only with evaluation data (see §9).
2. **Local only.** No cloud OCR, no uploads, no telemetry. Photos never leave the machine.
3. **Image pixels are never touched; metadata is written only through the two defined adapters.** No part of GridTag ever modifies a RAW or JPEG file's pixel data. Metadata is written in exactly two ways and nowhere else: (a) the **Lightroom plugin** writes into the **LrC catalog** via the SDK (`run` command, the normal workflow of §2); (b) in **standalone mode**, the **CLI itself** writes **XMP sidecar files** next to the originals (`tag` command, §6a), using a read-merge-write writer that never touches non-GridTag fields (§3.4) or an existing sidecar's develop/`crs:` settings. The `run` command never writes sidecars; the `tag` command never touches a Lightroom catalog.
4. **Field ownership.** XiPHiAS GridTag owns exactly the same set of fields in both adapters — LrC catalog keys and their XMP sidecar equivalents: `headline`/`photoshop:Headline`, `caption`/`dc:description`, `altTextAccessibility`/`Iptc4xmpCore:AltTextAccessibility`, `extDescrAccessibility`/`Iptc4xmpCore:ExtDescrAccessibility`, `personShown`/`Iptc4xmpExt:PersonInImage`, and the keywords it created itself. It must not touch anything else (rating, label, develop settings, other keywords, PM fields). Both adapters track which keywords/elements they themselves wrote (plugin metadata in the catalog; a dedicated tracking element in the sidecar) so re-runs stay idempotent (§3.5).
5. **Idempotent.** Re-running on the same photo, through either adapter, must not duplicate keywords/elements or leave stale XiPHiAS GridTag content behind.
6. **Contracts are versioned.** Every JSON file has `schemaVersion`. Breaking changes bump it and update `docs/contracts.md`, the C# records and the Lua code in the same change.
7. **Manual overrides win.** A `manual` status is never overwritten by automatic runs.
8. **Never invent SDK behaviour.** LrC SDK facts in §10 are marked verified / unverified. Do not rely on unverified behaviour without adding a test note in `docs/open-questions.md`.
9. **Identification evidence never invents a participant.** Start-number recognition is the primary identification signal. Car make/model and logo recognition are validation/disambiguation evidence for the *number*; they may strengthen a listed candidate or force `review` on conflict, but must not silently invent a participant. The same rule applies to driver identification (§6a's driver board / stint schedule, §8a): it may only select among the drivers already listed in the entry list for the matched car, never invent a name, and a conflict between evidence sources is surfaced as a non-blocking reason rather than silently resolved or allowed to downgrade an otherwise-confident number match.
10. **Keep the runtime simple.** C#/.NET + the thin Lightroom Lua plugin are the application stack. Do not introduce Node.js, Electron, a web UI, a service or a database server without asking. The standalone `tag` command is a CLI command, not a new application.

## 4. Architecture

```
                         gridtag CLI (.NET) — shared pipeline
                         ─────────────────────────────────────
IPhotoSource  ──►  read entrylist.csv + session.json [+ stints.json]
                    per photo: session/stint → preview → detect → read number
                    → driver board (optional) → evidence → match → build fields
                                              ──►  IResultSink

Adapter A — Lightroom plugin (normal workflow, "run")
  LrC plugin (Lua): collect Picks, write manifest.json ───► IPhotoSource = ManifestPhotoSource
  IResultSink = JsonResultSink ───► results.json ───► LrC plugin applies to catalog (write gates, chunks),
                                                        creates/updates review collections

Adapter B — Standalone (no Lightroom, "tag")
  IPhotoSource = FolderPhotoSource: scan folder for RAW/JPG, read capture time from EXIF
  IResultSink  = XmpSidecarResultSink (--apply): read-merge-write sidecar per photo
               + ReviewCsvResultSink (always): review.csv for anything not auto/manual
  Re-apply manual numbers later: gridtag tag --folder … --manual review.csv --apply
```

Exchange between the Lightroom plugin and the CLI is via files in a temp folder. The CLI has no dependency on Lightroom; the plugin has no dependency on the vision models. `IPhotoSource` and `IResultSink` are the only two seams between "how photos are found/results applied" and the shared pipeline — keep it that way; do not let either adapter leak into `Core` or `Vision`.

### Photo status model

| status   | meaning                                                           | plugin action                                  | folder (`tag`) action                          |
|----------|--------------------------------------------------------------------|------------------------------------------------|---------------------------------------------------|
| `auto`   | confident match, fields generated                                 | write fields + keywords                        | write sidecar (with `--apply`)                    |
| `review` | car found but number unresolved/ambiguous, or error in reasoning  | add to collection `GridTag Review`             | row in `review.csv`, no sidecar written            |
| `noCar`  | no car detected (pit, portrait, crowd)                            | add to collection `GridTag GeenAuto`           | row in `review.csv`, no sidecar written            |
| `manual` | number entered by a human and validated against the entry list    | write fields + keywords, remove from Review    | write sidecar (with `--apply`)                    |
| `error`  | exception while processing this photo                             | add to Review; message in `error`              | row in `review.csv` with the error message         |

A driver conflict or unresolved driver (§8a) never changes `status` by itself — it only adds a reason and falls back to listing every driver of the matched car, which is today's default behaviour either way.

## 5. Target repository layout

Create this structure in Phase 0 (see §11 and §15). Do not add other top-level projects without asking.

```
AGENTS.md  README.md  XiPHiAS.GridTag.slnx  Directory.Build.props  .editorconfig  .gitignore
docs/          contracts.md  architecture.md  open-questions.md  reference/*.xmp (golden examples)
samples/       entrylist.csv (fixture only, see §6)  session.example.json  stints.example.json
               manifest.example.json  results.example.json
src/
  XiPHiAS.GridTag.Core/
    Domain, Fields, Matching, Pipeline, Contracts   (as before; no I/O to images, no ONNX)
    IO/            IPhotoSource, IResultSink, ManifestPhotoSource, FolderPhotoSource,
                    JsonResultSink, XmpSidecarResultSink, ReviewCsvResultSink, XmpSidecarWriter
  XiPHiAS.GridTag.Vision/   RAW preview + vision implementations behind interfaces:
                            ICarDetector / IPlateReader; IDriverBoardReader (§8a);
                            later car-model and logo evidence classifiers; stubs first
  XiPHiAS.GridTag.Cli/      gridtag.exe: run | tag | check-entrylist | fields | eval | version
tests/XiPHiAS.GridTag.Core.Tests/   xUnit
tools/                      Python training/export scripts (later; not part of the .NET build)
lightroom/XiPHiAS.GridTag.lrdevplugin/   Info.lua, Runner.lua, Prefs.lua, Settings.lua, Metadata + Tagset, json.lua (rxi, MIT)
```

Dependency direction: `Cli → Vision → Core` and `Cli → Core`. `Core` references nothing but the BCL.

## 6. Data and contracts

### Entry list (owner-selected file, not a fixed path)
`samples/entrylist.csv` is a **fixture for tests and docs only** — the real file is chosen per event by the owner (`--entrylist` on the CLI; a per-event setting in the Lightroom plugin, not a single global preference — the plugin should make switching the active entry list between events easy, e.g. remember the last few used). Never hard-code a path to one event's list anywhere in production code.

UTF-8 **with BOM**, delimiter `;`, header:
`number;team;car;class;driver_1;driver_1_nat;driver_2;driver_2_nat` (loader must also accept `driver_3`, `driver_3_nat`, …, and optional `driver_1_code`, `driver_2_code`, `driver_3_code`, …).
Numbers are unique strings (`"3"`, `"69"`, `"991"`, `"007"`). Normalize input numbers: trim, strip leading `#`, uppercase. Preserve leading zeros: `"007"` and `"7"` are different valid start numbers, as are `"003"` and `"3"`.
Names contain non-ASCII characters (e.g. Söderström). Never assume ASCII.

**Driver codes** (`driver_N_code`): a short on-car identifier (e.g. a 3-letter code shown on an in-car LED board). If the column is present and non-empty for a driver, use it verbatim (normalized: trim, uppercase). If absent, derive a default: first 3 letters of the driver's surname (last whitespace-separated token of the name), uppercase, non-ASCII folded to ASCII for matching purposes only (display name is never altered). `EntryListLoader` must throw `InvalidDataException` fail-fast if two drivers of the **same car** end up with the same code (explicit or derived) — that is a data error to fix in the entry list, not something to guess around at runtime.

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

### `stints.json` (optional, per-event, who was driving when)
```json
{
  "stints": [
    { "number": "69", "driver": "Thierry Vermeulen", "start": "2026-09-18T13:30:00", "end": "2026-09-18T13:55:00" },
    { "number": "69", "driver": "Ben Green",          "start": "2026-09-18T13:55:00", "end": "2026-09-18T14:40:00" }
  ]
}
```
Entirely optional input (`--stints`). `driver` must match a driver name already listed for that `number` in the entry list — unknown driver names are a load-time error (`InvalidDataException`), same reasoning as §3.9. Used only as one of two independent sources `DriverResolver` consults (§8a); if the file is absent, stint evidence is simply never available and driver resolution falls back to board OCR / "car has a single listed driver".

### `manifest.json` (Lightroom plugin → CLI, `run` only)
```json
{
  "schemaVersion": 1,
  "photos": [
    { "id": 12345, "uuid": "…", "path": "D:\\2026-09-18 - GTWC\\_MWR1234.ARW",
      "captureTime": "2026-09-18T13:52:10", "manualNumber": null }
  ]
}
```
`id` = `LrPhoto.localIdentifier`. `manualNumber` is set only in manual mode; it may hold several numbers separated by comma, semicolon or space (first = primary car). Standalone mode (`tag`) never reads or writes this file — `FolderPhotoSource` builds the equivalent in memory from the scanned folder (see §6a).

### `results.json` (CLI → Lightroom plugin, `run` only)
```json
{
  "schemaVersion": 1, "toolVersion": "0.1.0", "generatedAt": "2026-09-18T14:10:00Z",
  "photos": [
    { "id": 12345, "status": "auto", "reasons": [], "session": "FP2",
      "cars": [ { "number": "69", "confidence": 0.98, "source": "ocr", "primary": true,
                  "currentDriver": "Thierry Vermeulen", "currentDriverSource": "boardOcr" } ],
      "fields": {
        "headline": "#69 Emil Frey Racing Ferrari 296 GT3 EVO",
        "caption": "…", "altText": "…", "extDescription": "…",
        "keywords": ["Emil Frey Racing", "…", "#69", "Free Practice 2", "PRO"],
        "persons": ["Thierry Vermeulen"] } },
    { "id": 12346, "status": "review", "reasons": ["confusable:59,66,89,99"], "cars": [], "session": "FP2" }
  ]
}
```
`cars[].currentDriver` is `null`/omitted when unresolved or conflicting; `currentDriverSource` is one of `singleDriver | boardOcr | stintSchedule | boardOcr+stintSchedule | null`. When `currentDriver` is set, `fields.persons` and the drivers used in headline/caption/alt/ext contain **only** that driver; when it is not set, they fall back to **all** drivers of the primary car (today's default, unchanged — this is why the existing #3/#69 golden tests, which have no board evidence, keep working unmodified).

JSON rules: camelCase, enums as camelCase strings (`auto|review|manual|noCar|error`; sources as `ocr|manual|singleDriver|boardOcr|stintSchedule`), null properties omitted, **UTF-8 without BOM**, readable (relaxed escaping) output. The Lua side uses `json.lua` (rxi) — keep payloads simple (no dates as objects, no nested polymorphism). This UTF-8-without-BOM rule is specific to `manifest.json`/`results.json`; it does **not** apply to XMP sidecars (§6a), which follow the convention of the files they merge into.

CLI exit codes: `0` ok · `1` unexpected error · `2` usage error · `3` invalid/missing input file. Per-photo failures do **not** fail the run; they become `status: "error"`.

## 6a. Standalone mode details

**Discovery (`FolderPhotoSource`).** Recursively scan `--folder` for RAW and JPEG files (reuse the extension list from the preview/eval tooling). Build one in-memory "manifest" entry per file: `id` = a stable sequence number (order by path), `path`, `captureTime` read from EXIF (same reader used for RAW preview work, §15 task 9a — a cheap metadata-only read, not a full decode). No `uuid`/`pickStatus` concept exists outside Lightroom: standalone mode processes **every photo it finds** in the folder (that is the point of pointing it at a folder directly); filtering by picking happens upstream, in Lightroom, before the owner ever reaches for standalone mode, or the owner simply points `--folder` at an already-curated subfolder.

**Manual numbers in.** `--manual <csv>` accepts the same shape the review output produces (`path;numbers`, §6a below) so a filled-in `review.csv` can be fed straight back in; matched by path.

**Applying results.**
- Without `--apply`: dry run. Print a summary (counts per status) and always write `review.csv` (or `--review-out <path>`) for every photo that is not `auto`/`manual`: columns `path;numbers;reasons` with `numbers` left **empty** for the owner to fill in (same convention as `tools/make_labels.py`'s review output), so the file can be edited by hand and passed back via `--manual`.
- With `--apply`: for every `auto`/`manual` photo, `XmpSidecarResultSink` writes the sidecar (`<basename>.xmp` next to the original). If a sidecar already exists, **read it, merge, write it back** — never overwrite blindly. Field ownership is exactly §3.4. Idempotency is achieved via a private tracking element (e.g. an `xiphias:GridTagKeywords` `rdf:Bag` inside a dedicated namespace) listing exactly which `dc:subject` entries GridTag itself added last time, so a re-run removes and replaces only those, never anything Photo Mechanic or the owner added by hand.
- Sidecar encoding follows the convention of the file being merged into (UTF-8 with BOM, matching the owner's existing Photo Mechanic-written sidecars and `docs/reference/*.xmp`) — this is independent of, and not governed by, the `results.json`/`manifest.json` UTF-8-without-BOM rule above.

**Relationship to Lightroom.** Standalone mode is for folders not (yet) managed in Lightroom, or for a quick pre-import check. If the owner later imports tagged photos into Lightroom, Lightroom only picks up sidecar contents at import time or via an explicit "Read Metadata from File" — GridTag's standalone mode does not and cannot push into an already-open catalog (that remains the Lightroom plugin's job, via `run`).

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

- `{drivers}`: the **resolved driver set** for the primary car — a single name when `currentDriver` is resolved (§6, §8a), otherwise all of the car's listed drivers (today's default). Rendered as `Name (NAT)` per driver; 2 → `A en B`; 3+ → `A, B en C`; no nationality → name only.
- `{verb}`: `rijdt` when the resolved driver set has one driver, `rijden` for more.
- **Keywords order**: per car `team, car, resolved driver(s)…, #nr`; then session name; then distinct classes. Case-insensitive de-duplication, first wins.
- **Persons**: the resolved driver set of every car in the photo, distinct.
- **Several cars in one photo**: headline/caption/alt/ext use the **primary** car (largest detection, must be resolved); keywords and persons are the union across cars.

**Golden tests are mandatory.** `docs/reference/003-*.xmp` (#3) and `docs/reference/069-*.xmp` (#69) are real expected outputs — both have two drivers and no driver-board evidence, so they exercise the "fall back to all drivers" path and must keep passing unmodified. Tests parse these XMP files (XDocument) and assert: `photoshop:Headline`, `dc:description`, `Iptc4xmpCore:AltTextAccessibility`, `Iptc4xmpCore:ExtDescrAccessibility`, `Iptc4xmpExt:PersonInImage`, and that the **last N items of `dc:subject`** equal the generated keywords. (The leading items of `dc:subject` are Photo Mechanic constants and are out of scope.) When task 12 (§15) adds driver-board resolution, add a new golden/unit test with a *resolved* `currentDriver` so the single-driver path is covered too — it cannot reuse #3/#69 as-is since neither has board evidence.

## 8. Matching (number validation)

Vision returns an **n-best list** `NumberHypothesis(text, probability)`. Core decodes against the entry list (lexicon decoding); it never trusts the raw top string.

`NumberMatcher.Match`:
1. Normalize each hypothesis; sum probability per entry-list number; mass of non-listed hypotheses = `outOfList`.
2. Optional `IEvidence` (first car make/model, then logo; later focus point and timing) multiplies candidate probability by a weight in `[0, 1.5]`, capped at 1. Evidence is always evaluated against entry-list candidates; it never creates a participant that is absent from the entry list. Driver identification (§8a) is a **separate, independent step** that runs after a number is matched — it does not feed back into the number's confidence.
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

## 8a. Driver identification (who, among the car's listed drivers, is racing now)

This resolves **which** of a matched car's already-listed drivers is shown — it is not a new way to find the *car's number*, and it never changes `status` by itself (§4 status table). Two independent, optional evidence sources, combined by `DriverResolver`:

1. **On-car driver board (`IDriverBoardReader`, Vision).** Many endurance cars carry an in-car LED board that sometimes shows a track position ("P3") and sometimes the current driver's short code. Returns an n-best list of `DriverCodeHypothesis(text, probability)` from a crop near the car-number region (exact crop geometry is a Vision concern, defined when task 12/9d implement it). Core normalizes each hypothesis (trim, uppercase) and matches it against the matched car's driver codes (`driver_N_code`, explicit or derived — §6). A position-style reading ("P3") simply will not match any code and is harmlessly discarded; no special-casing is needed for that case.
2. **Stint schedule (`stints.json`, optional, §6).** `StintResolver.Resolve(number, captureTime)` returns the driver whose stint window contains the photo's capture time, if `stints.json` was supplied and a window matches.

`DriverResolver.Resolve(car, boardHypotheses, stintResolver, captureTime)`:
- Car has exactly one listed driver → that driver, source `singleDriver`, no evidence needed.
- Only board resolves → that driver, source `boardOcr`.
- Only stint resolves → that driver, source `stintSchedule`.
- Both resolve to the **same** driver → that driver, source `boardOcr+stintSchedule`.
- Both resolve but **disagree** → unresolved; add reason `driver_conflict:<code>`; fields fall back to listing all of the car's drivers (§7).
- Neither resolves → unresolved; fields fall back to listing all of the car's drivers (§7) — this is the default today and must stay the default when no board/stint data exists, so existing behaviour and the #3/#69 golden tests are unaffected.

## 9. Evaluation (go/no-go for the recognition work)

Build `gridtag eval --labels labels.csv --entrylist … --session …` before investing in models. `labels.csv`: `path;numbers` (comma separated; empty = no car). Report: auto-rate, **precision of auto tags**, recall, review-rate, per-reason counts, confusion pairs, seconds/photo.

Proof-of-concept criteria on a held-out set: ≥ 80 % of photos with a legible number correct · ≤ 2 % wrong automatic tags · ≤ 2 s/photo on the owner's hardware. Manual corrections from the plugin (and from `--manual` in standalone mode) become new labelled data.

Driver-identification accuracy is a secondary, later metric (§15 task 12/13) — it must never be optimised at the expense of number-match precision.

## 10. Lightroom Classic SDK notes (Lua)

Environment: Lua 5.1, plugin runs inside LrC, `import`/`require` of plugin-local modules, globals `_PLUGIN`, `WIN_ENV`, `MAC_ENV`. No built-in JSON (use `json.lua`). Menu scripts must run in an async task (`LrFunctionContext.postAsyncTaskWithContext`).

**Verified in the official LrPhoto reference:** `photo:setRawMetadata` keys `headline`, `caption`, `title`, `personShown` (single string), `altTextAccessibility` and `extDescrAccessibility` (SDK 13.2+); raw metadata `pickStatus` (1 = Pick, −1 = rejected), `path`, `uuid`, `dateTimeOriginalISO8601`; `photo:setPropertyForPlugin/getPropertyForPlugin`; `photo:addKeyword/removeKeyword`; `catalog:createKeyword(name, synonyms, includeOnExport, parent, returnExisting)`; `catalog:withWriteAccessDo`.

**Do not use:** `photo:saveMetadata()` / `photo:readMetadata()` — undocumented, timing and dialog issues reported. The design deliberately avoids reading/writing XMP sidecars from the plugin (standalone mode's `XmpSidecarWriter` is unrelated — it runs entirely outside Lightroom).

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
dotnet run --project src/XiPHiAS.GridTag.Cli -- tag --folder work/raw-samples --entrylist samples/entrylist.csv --session samples/session.example.json --review-out work/review.csv
dotnet run --project src/XiPHiAS.GridTag.Cli -- tag --folder work/raw-samples --entrylist samples/entrylist.csv --manual work/review.csv --apply
dotnet run --project src/XiPHiAS.GridTag.Cli -- check-entrylist --entrylist samples/entrylist.csv
dotnet run --project src/XiPHiAS.GridTag.Cli -- fields --entrylist samples/entrylist.csv --session samples/session.example.json --number 69
```
Lua syntax check (if Lua 5.1 is available): `luac5.1 -p lightroom/XiPHiAS.GridTag.lrdevplugin/*.lua`.

Target framework is `net10.0` (single place: `Directory.Build.props`). Do not pin NuGet versions from memory: use `dotnet add package <name>` and commit what resolves. Test stack: xUnit.

## 12. Conventions

C#: nullable enabled, file-scoped namespaces, records for data, `sealed` by default, no static mutable state, constructor injection, no `async` where nothing is awaited, XML docs on public API of `Core`. Build XML/JSON with real APIs, never by string concatenation. Zero compiler warnings for new code.
XMP I/O specifically: always read-merge-write via `XDocument`/`XElement` with proper namespaces, never string templating; preserve unrelated namespaces and elements byte-for-byte where untouched; write encoding matches the file merged into (§6a), independent of the JSON no-BOM rule.
Errors: throw `InvalidDataException` for bad input files (including entry-list/stint data errors per §6/§6a); catch per photo in the pipeline only.
Lua: `local` everything, one module per file, log via `LrLogger('GridTag')`, no global state, user-visible strings in Dutch, defensive `pcall` around every `setRawMetadata` (older LrC lacks some keys).
Git: small commits, imperative subject lines, one concern per change.

## 13. Testing rules

- Every Core behaviour in §6–§8a has a unit test. Golden XMP tests are never skipped or loosened to make a build pass.
- Pipeline tests use fakes (`IRawPreviewProvider`, `ICarDetector`, `IPlateReader`, `IDriverBoardReader`); no image files needed.
- Matching tests cover: strong single hypothesis (Auto), confusable cluster (Review), substring risk (`5` vs `55`), out-of-list dominance, supportive car-model evidence, conflicting car-model/logo evidence, and no reading.
- Driver-resolution tests cover: single listed driver, board-only match, stint-only match, agreeing board+stint, conflicting board vs stint (falls back to all drivers, reason present, status unaffected), neither resolves (unchanged default behaviour, matches #3/#69 golden tests).
- `XmpSidecarResultSink`/`XmpSidecarWriter` tests cover: new sidecar created from scratch; existing sidecar with unrelated PM fields and `crs:` develop settings preserved byte-for-byte outside GridTag's own fields; idempotent re-run (second write replaces only the previously-GridTag-written keywords, not anything else); a non-UTF-8-BOM existing sidecar is not forced into a different encoding without reason.
- A bug fix starts with a failing test.

## 14. Definition of done

Build and tests green · no new warnings · docs/contracts updated if any contract or default changed · `docs/open-questions.md` updated if an unverified SDK assumption was touched · owner-facing behaviour changes noted in `README.md`.

## 15. First tasks (Phase 0 → 1), in order

1. **Scaffold** the layout of §5 with the projects, `Directory.Build.props`, `XiPHiAS.GridTag.slnx`, `.editorconfig`, `.gitignore` (`*.xmp -text` in `.gitattributes`). Copy the owner-provided `_entrylist.csv` to `samples/entrylist.csv` and the two reference XMP files to `docs/reference/`. Build green.
2. **Core domain**: `EntryList` + `EntryListLoader` (BOM, `;`, quoted cells, driver_N columns, optional driver_N_code with fallback derivation and the same-car collision check), `NumberNormalizer`, `ConfusionMap`, `EntryListAnalysis` (confusables, substring hosts). Tests incl. 45 entries, unique numbers, Söderström present, derived vs explicit driver codes, collision detection.
3. **Fields**: `EventContext`, `FieldTemplates`, `TemplateRenderer`, `SessionResolver`, `FieldBuilder`. Golden tests for #3 and #69 pass.
4. **Matching**: `NumberHypothesis`, `MatchOptions`, `IEvidence`, `NumberMatcher` per §8 with tests.
5. **Pipeline + contracts**: `Manifest`, `ResultFile`, `PhotoResult`, `GridTagJson` (rules of §6), `TaggingPipeline` (manual path first, then automatic path against fakes), stub implementations in `XiPHiAS.GridTag.Vision` (`StubRawPreviewProvider` → `no_preview`, `NullCarDetector`, `NullPlateReader`). Shape the pipeline around `IPhotoSource`/`IResultSink` from the start (§4) even though only the manifest/JSON pair exists yet, so task 11 is a pure addition, not a refactor.
6. **CLI**: `run`, `check-entrylist`, `fields`, `version`, exit codes of §6. Example files in `samples/`.
7. **Lightroom plugin**: `Info.lua`, metadata + tagset, `Prefs`, `Settings`, `Runner` (analyze + manual), review collections, `json.lua`. Lua syntax check clean. Then owner tests inside LrC and reports the open questions.
8. **Evaluation harness** (`gridtag eval`) — **before** any model work.
9. **Vision**: RAW preview extraction (embedded JPEG first, half-size decode as fallback), then car detection and number reading behind the existing interfaces. Keep the inference backend replaceable (for example ONNX Runtime/DirectML or WinML); do not leak backend-specific types into Core. Prefer permissively licensed models (check licences; AGPL is not acceptable for commercial use). Once number recognition has a measured baseline, add **car make/model classification as the first `IEvidence` control**, followed by optional logo recognition as supporting evidence. Re-run `gridtag eval` for every evidence/model change.
   - **9d. Driver-board OCR model**, same pattern as 9a–9c: implement `IDriverBoardReader` for real once a baseline for task 12's interfaces/stub exists. Do this only after the number-recognition baseline is measured — driver identification is explicitly secondary (§3.9).
10. Later: focus-point evidence; burst propagation; EXIF-time ↔ car-passing-time cross-check (distinct from the driver stint schedule of §6/§8a); WPF review UI only if Lightroom/`review.csv` review proves insufficient; Python training/export tools in `tools/`.
11. **Standalone mode.** Introduce `IPhotoSource`/`IResultSink` if task 5 did not already (should be a no-op by then) and refactor `run` onto `ManifestPhotoSource` + `JsonResultSink` with no behaviour change (existing tests must still pass unmodified). Add `FolderPhotoSource` (folder scan + EXIF capture time), `XmpSidecarWriter` + `XmpSidecarResultSink` (read-merge-write, idempotent per §3.4/§3.5, encoding per §6a), `ReviewCsvResultSink`, and the `gridtag tag --folder --entrylist [--session] [--stints] [--manual] [--review-out] [--apply]` command. Tests per §13's `XmpSidecarResultSink` bullet, plus an end-to-end test: run `tag` twice on the same fixture folder and assert the second run's sidecar differs only in whatever legitimately changed.
12. **Driver identification.** `IDriverBoardReader` interface + stub in Vision; `DriverCodeHypothesis`; `DriverResolver` in Core per §8a; wire into `TaggingPipeline` and the `results.json`/field-building contracts (§6, §7). Tests per §13's driver-resolution bullet. Does not yet require a real OCR model (task 9d does).
13. **Stint schedule.** `stints.json` loader + validation (§6), `StintResolver`, wired into `DriverResolver` (likely delivered together with task 12, but keep the loader/validation and the resolver as separately testable units).

## 16. Working agreements

- If a requirement is ambiguous, propose the smallest reasonable interpretation and note it in your summary instead of stalling. Ask before: changing a contract, adding a top-level project, adding a dependency with a non-permissive licence, or touching anything outside the field ownership of §3.4.
- Finish each task with: what changed, how it was verified (commands run), what remains, and any new open question.