# AGENTS.md — XiPHiAS GridTag Lightroom Classic plugin

Instructions for coding agents working under `lightroom/`. Read the repository root `AGENTS.md` first. This file adds Lightroom-specific rules for `lightroom/XiPHiAS.GridTag.lrdevplugin/` and wins on conflict inside this subtree.

## 1. Scope

This folder contains the **thin Lightroom Classic adapter/UI layer** for XiPHiAS GridTag.

The plugin may:
- collect the final Lightroom Picks that the owner already selected and edited;
- build `manifest.json` for the C# CLI;
- launch `gridtag.exe` outside catalog write gates;
- read and validate `results.json`;
- apply only XiPHiAS GridTag-owned metadata to the Lightroom catalog;
- maintain XiPHiAS GridTag custom metadata and review collections;
- provide the manual-number correction flow and plugin settings.

The plugin must **not** contain recognition, matching, entry-list, session-resolution or field-generation business logic. Those belong in the C# projects. Do not reimplement Core logic in Lua as a fallback.

XiPHiAS GridTag is not a culling, editing or export plugin. The owner processes the images first; XiPHiAS GridTag only enriches the final Picks with car-specific metadata.

## 2. Runtime and language

- Lightroom Classic SDK, **Lua 5.1**.
- Keep code, identifiers and comments in English.
- Keep user-visible plugin strings in Dutch.
- Use `local` for variables and functions unless an SDK entry point requires otherwise.
- One focused module per file; avoid global mutable state.
- Log through `LrLogger('GridTag')`.
- Use plugin-local `json.lua` (rxi, MIT); retain its licence notice.
- Do not add JavaScript, Node.js, Electron or a browser runtime to the Lightroom plugin.

The owner mainly develops in JavaScript and some C#. Prefer simple procedural Lua over metaprogramming, custom OO layers or clever abstractions.

## 3. Fixed Lightroom workflow

The plugin operates in this order:

1. Lightroom Classic already contains the owner's selected and edited photos.
2. The owner marks final photos as **Pick**.
3. Menu action `XiPHiAS GridTag: tag Picks` collects only photos with `pickStatus == 1`.
4. The plugin writes a versioned `manifest.json` into a run-specific temporary/work folder.
5. The plugin starts `gridtag.exe run ...` with `LrTasks.execute` **outside** a write gate.
6. The plugin reads and validates versioned `results.json`.
7. The plugin applies successful results to the Lightroom catalog in short write-gate chunks.
8. Review/no-car/error collections and XiPHiAS GridTag custom metadata are updated.
9. The owner reviews remaining cases and exports from Lightroom normally.

Do not add automatic processing on import, selection, rating changes, develop changes, export or folder watching unless the owner explicitly asks for it.

## 4. Source of truth and contracts

The root contract definitions and `docs/contracts.md` are authoritative.

The plugin must treat JSON as an external, versioned contract:
- require `schemaVersion`;
- reject an unsupported schema with a clear Dutch error message;
- never guess missing or renamed contract properties;
- keep payloads simple and compatible with `json.lua`;
- never infer business rules from display strings.

`manifest.json` uses Lightroom photo `localIdentifier` as `id`; include `uuid`, `path`, `captureTime` and optional `manualNumber` exactly as defined in the root contract.

`results.json` is keyed back to catalog photos by `id`. Ignore no result silently only if the contract explicitly allows it; otherwise treat missing/duplicate IDs as a run-level validation error before catalog writes begin.

Breaking JSON changes require the root contract, C# records and Lua consumer/producer to change together.

## 5. Selection rules

Normal analysis mode:
- process **Picks only** (`pickStatus == 1`);
- never change Pick/Reject state;
- never change rating or color label;
- never include non-Picks merely because they are selected in the UI.

Manual mode:
- operates on the photos targeted by the documented manual workflow;
- reads XiPHiAS GridTag's custom `manualNumber` field;
- passes it to the same C# CLI;
- does not bypass C# validation against the entry list.

A photo with XiPHiAS GridTag status `manual` must not be replaced by a later automatic run unless the owner explicitly performs a future reset/clear action that is designed for that purpose.

## 6. Metadata ownership

XiPHiAS GridTag owns exactly these photo fields:
- `headline`
- `caption`
- `altTextAccessibility`
- `extDescrAccessibility`
- `personShown`
- keywords previously created by XiPHiAS GridTag and recorded in XiPHiAS GridTag plugin metadata

XiPHiAS GridTag custom plugin metadata may include:
- `status`
- `number`
- `manualNumber`
- `confidence`
- `reasons`
- `session`
- `keywords`
- `toolVersion`

Do **not** write or modify:
- title unless the root contract is deliberately changed;
- creator, credit, copyright, contact fields;
- location/event/transmission reference owned by Photo Mechanic;
- rating, labels, Pick/Reject flags;
- develop settings, crops or image adjustments;
- unrelated keywords;
- filesystem XMP sidecars or RAW files.

Never call undocumented metadata-save/read helpers to force sidecar synchronisation. XiPHiAS GridTag writes to the Lightroom catalog only.

## 7. Idempotent metadata application

Re-running XiPHiAS GridTag must be safe.

Before adding new XiPHiAS GridTag keywords for a photo:
1. read the XiPHiAS GridTag-owned keyword list stored in plugin metadata;
2. remove only those old XiPHiAS GridTag keyword objects from that photo;
3. leave all other keywords untouched;
4. create/reuse required new keywords;
5. add the new keywords;
6. save the exact new XiPHiAS GridTag keyword names back into plugin metadata.

Do not identify owned keywords by naming heuristics such as `#`, team name or hierarchy. Ownership comes from the plugin's recorded metadata.

Repeated application of the same result must not duplicate keywords or change unrelated data.

## 8. Catalog write gates

All catalog mutations must occur inside `catalog:withWriteAccessDo`.

Rules:
- never execute the CLI inside a write gate;
- never wait on external processes inside a write gate;
- validate the complete result file before starting writes;
- keep write gates short;
- process photos in configurable chunks, default approximately 50;
- avoid expensive filesystem or JSON work inside a write gate;
- one bad photo must not corrupt the rest of the batch.

Use defensive `pcall` around `photo:setRawMetadata` calls because some fields depend on the installed Lightroom SDK/version. Log the photo ID, field and error when a write fails.

Do not silently convert a partial metadata write into `auto`. If required XiPHiAS GridTag-owned fields fail in a way that makes the result incomplete, store/report an error and put the photo into Review according to the agreed behaviour.

## 9. CLI execution

Use Lightroom asynchronous task APIs for menu commands; do not block the Lightroom UI thread.

For CLI execution:
- resolve the configured `gridtag.exe`, entry-list and session paths first;
- validate that required files exist before launching;
- build arguments from contract-defined file paths, not shell fragments supplied by users;
- quote Windows paths defensively;
- call `LrTasks.execute` outside catalog write access;
- capture/use the CLI exit code;
- distinguish usage/input/run errors according to the documented CLI exit codes;
- still read `results.json` after exit code 0 and validate it before applying anything.

Do not parse CLI console text as an application contract. JSON files are the contract.

Windows quoting around `LrTasks.execute` is currently an explicit SDK/open-question item. If implementation testing establishes the required form, document the result in `docs/open-questions.md`.

## 10. Lightroom SDK facts

Use only SDK behaviour already marked verified in the root `AGENTS.md`, or verify it during an owner test before depending on it.

Known intended APIs include:
- `photo:getRawMetadata(...)`
- `photo:setRawMetadata(...)`
- `photo:setPropertyForPlugin(...)`
- `photo:getPropertyForPlugin(...)`
- `photo:addKeyword(...)`
- `photo:removeKeyword(...)`
- `catalog:createKeyword(...)`
- `catalog:withWriteAccessDo(...)`
- `LrFunctionContext.postAsyncTaskWithContext(...)`
- `LrTasks.execute(...)`

Do **not** use `photo:saveMetadata()` or `photo:readMetadata()`.

Current items that must remain treated as unverified until tested and recorded:
- multiple names in `personShown` and the correct separator;
- `LrTasks.pcall` as yield-safe error handling around process execution;
- Windows quoting for `LrTasks.execute`;
- creation/update semantics for normal Lightroom collections;
- compatibility when `LrSdkVersion` declared by the plugin exceeds the running Lightroom version.

Do not turn an unverified assumption into permanent plugin architecture without updating `docs/open-questions.md` with the real test result.

## 11. Custom metadata

Declare XiPHiAS GridTag custom metadata centrally and keep its IDs stable after release.

Expected fields include:
- status: enum `auto | review | manual | noCar | error`
- number
- manualNumber
- confidence
- reasons
- session
- keywords
- toolVersion

Mark fields searchable where useful for Lightroom filtering/smart collections.

Treat display labels as UI only; code should use stable field IDs.

Do not store large JSON result blobs in plugin metadata. Store only the small state needed for review, idempotence and traceability.

## 12. Review collections

The plugin maintains these logical collections:
- `GridTag Review`
- `GridTag GeenAuto`

Status behaviour:
- `auto`: remove from Review/GeenAuto when appropriate; apply fields.
- `manual`: apply fields and remove from Review/GeenAuto when appropriate.
- `review`: add to `GridTag Review`.
- `noCar`: add to `GridTag GeenAuto`.
- `error`: add to `GridTag Review` and preserve an understandable error/reason.

Collection operations must be idempotent. Re-running must not create duplicate same-name collections or duplicate membership side effects.

If collection SDK behaviour is still unverified on the owner's Lightroom version, isolate it behind a small module and document the result of the manual test.

## 13. Manual correction flow

The supported flow is:

1. Owner selects one or more photos in Lightroom.
2. Owner enters `manualNumber` in XiPHiAS GridTag custom metadata. Multiple numbers may be separated by comma, semicolon or spaces; first number is primary as defined by the root contract.
3. Owner runs `XiPHiAS GridTag: verwerk handmatige nummers`.
4. Plugin sends those values to the normal C# CLI through `manifest.json`.
5. C# validates the numbers against the event entry list and generates fields.
6. Valid result becomes status `manual`; invalid input becomes `review` with e.g. `unknown_number:<n>`.

Never generate metadata directly from the typed number in Lua. Manual means human identification, **not** bypassing the C# entry-list and field-generation rules.

## 14. Settings and preferences

Keep settings minimal and directly related to the adapter role.

Expected preferences include:
- path to `gridtag.exe`;
- path to `entrylist.csv`;
- path to `session.json`;
- `chunkSize` (sensible default around 50);
- `personSeparator` while multi-person `personShown` remains under test.

Validate paths and numeric ranges at use time as well as in the settings UI.

Do not add model thresholds, OCR settings or matching weights to the Lightroom UI. Those are C#/evaluation concerns unless the root design is explicitly changed.

## 15. Error handling and user feedback

Failures must be actionable for the owner.

Differentiate at least:
- missing/misconfigured executable;
- missing entry list/session file;
- CLI non-zero exit;
- missing or invalid result JSON;
- unsupported `schemaVersion`;
- per-photo `error` status;
- Lightroom metadata write failure.

Use concise Dutch dialogs/messages for errors that require user action. Put technical details in the XiPHiAS GridTag log.

A per-photo processing failure must not abort metadata application for unrelated valid photos unless the overall result contract itself is corrupt.

Never claim metadata was applied successfully when a Lightroom write failed.

## 16. File/module responsibilities

Prefer small modules with clear responsibilities. The initial layout may use:

- `Info.lua` — plugin identity, SDK declaration, menu registration references.
- `Metadata.lua` — custom metadata schema.
- `Tagset.lua` — metadata panel/tagset definition if used.
- `Prefs.lua` — preference storage and defaults.
- `Settings.lua` — Dutch settings UI.
- `Runner.lua` — orchestration only: collect → manifest → execute → result → apply.
- `Manifest.lua` — manifest construction/validation if Runner grows too large.
- `Results.lua` — result contract validation if Runner grows too large.
- `CatalogWriter.lua` — write-gate/chunked metadata application if needed.
- `Collections.lua` — Review/GeenAuto collection handling if needed.
- `json.lua` — vendored rxi JSON implementation, minimally modified or unmodified.

Do not split files merely to satisfy this list. Start small, extract modules when responsibilities become distinct.

## 17. Testing and verification

There is no substitute for testing the plugin inside Lightroom Classic.

For every plugin task:
1. run Lua syntax checking when Lua 5.1 tooling is available:
   `luac5.1 -p lightroom/XiPHiAS.GridTag.lrdevplugin/*.lua`
2. keep JSON contract examples compatible with the C# tests;
3. manually test changed Lightroom SDK behaviour in the owner's LrC when required;
4. record newly verified/unverified SDK behaviour in `docs/open-questions.md`;
5. confirm that a rerun is idempotent and does not remove Photo Mechanic or user keywords when metadata-writing code changes.

When practical, isolate pure Lua helpers so they can be tested outside Lightroom, but do not build a large Lua test framework before the plugin warrants it.

## 18. Definition of done for plugin changes

A Lightroom-plugin change is done when:
- Lua syntax check is clean where tooling is available;
- root JSON contracts still match the C# side;
- CLI execution occurs outside write gates;
- catalog writes are bounded/chunked and touch only XiPHiAS GridTag-owned fields;
- reruns remain idempotent;
- manual status is protected from automatic overwrite;
- affected Review/GeenAuto behaviour has been checked;
- any newly tested SDK assumption is recorded in `docs/open-questions.md`;
- owner-facing behaviour changes are reflected in `README.md` where relevant.

Finish work with a short summary: files changed, verification performed, remaining open questions and anything that still requires testing inside Lightroom Classic.
