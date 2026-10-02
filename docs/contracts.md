# Contracts

AGENTS.md sections 3 and 6 define the authoritative exchange contracts.
No JSON runtime contracts are implemented in tasks 1-2.

The entry list is semicolon-delimited UTF-8 with BOM. Required columns are
number, team, car, class, driver_1, driver_1_nat, driver_2, driver_2_nat.
Additional driver_N / driver_N_nat pairs represent additional drivers.
Names retain Unicode. Numbers are trimmed, have a leading # removed, are
uppercased invariantly. Leading zeros are significant and preserved: `007`
and `7` identify different entries. OCR, manual input, timing and evaluation
must use the same exact string identity; missing zeros are not inferred.
Duplicate normalized numbers are invalid.

Burst review candidates reuse the schemaVersion 1 result fields. They have
status `review`, no generated `fields`, and car source `burst` with `primary`
false. For source `burst`, `confidence` denotes frame appearance similarity,
not identification probability. Such cars are proposals, not resolved matches.
Reasons include `burst_propagation_review`, `burst_source:<source ID>:<number>`
and, for different proposed numbers, `burst_ambiguous`. No new JSON properties
or automatic acceptance path are introduced. See [burst workflow](task10b-burst-propagation.md).

Manifest and results samples declare schemaVersion 1 and use UTF-8 without BOM.
The session sample currently has no schemaVersion; this conflicts with the
versioning requirement and is recorded in open-questions.md. Samples are retained
unchanged pending clarification, rather than silently changing the contract.
