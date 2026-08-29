# Phase 4 closure review

Review date: 2026-08-30.

## Outcome

Phase 4's planned language, VM, declaration, event/value, and content-integration
slices are implemented. The durable `ContentSnapshot` owns the Phase 3 typed catalog,
the composed rule graph, the once-parsed ruleset documents, compiled script artifacts,
composed event plans, typed tag definitions, validated initial values, diagnostics, and
capabilities. `ScriptsCompiled` advances independently of `ResourcesResolved`, only
after linking and only when the complete diagnostic stream contains no error. The
bounded collector retains error severity even after its message limit, preventing a
false successful stage.

Rulesets are not reparsed for scripting. Global `extended.tags`, `tagsFile`, `globals`,
and `extended.scripts` nodes are consumed from the retained document catalog in
reference file order. Rule-local scripts, initial global/rule tag values, and legacy
stat-bonus mappings are compiled while replaying the composed operations. The seven reference default scripts
are compiled through the same exact parser catalog. Compiled gameplay calls remain
provider-independent as required by ADR 0013; their runtime behavior belongs to the
owning gameplay slices.

## Compatibility evidence

The content closure was checked against every bundled public mod fixture and the
locally available private `xcom1` -> `40k` -> `40k_ROSIGMA_edits` corpus. A direct
full-installation audit exposed and then closed unique-best editable-pointer overload
scoring, catalog list-loop lowering, custom-type operations sharing scalar core names,
hidden typed value copy, and file-scoped `RuleList.current`. The final audit parsed 512
ruleset files, attempted 3,875 scripts, retained 3,536 compiled artifacts, composed 31
event plans, registered 580 tags, validated 13,651 initial values, and reached
`scripts-compiled` with 8,818 expected warnings and zero errors. Three runs emitted the
same 9,591,410-byte typed manifest with SHA-256
`C52A5EA199A378EA557ACD9E86B87977DB107A81C1A92817ACF421B2C579B02D`.

Installations with unrelated validation errors do not claim `ScriptsCompiled`; their
script envelopes are still compiled and audited separately. The synthetic
`script-content` fixture exercises a
retained-document `tagsFile`, global and rule typed values, an ordered global event, a
rule-local script, and a generated legacy stat-bonus script.

The audit also confirmed a subtle load rule: `ScriptGlobal::load` iterates registered
tag owners, so unknown owner mappings under `extended.tags` are ignored. The managed
loader deliberately matches this behavior. Tag constants retain owner types and file
visibility, `RuleList` values resolve against the active load plan, and no later file's
tag declaration is visible while compiling an earlier file.

Authoritative reference files inspected for this slice:

- `src/Mod/Mod.cpp` and `src/Mod/ModScript.h` for file-order global loading, parser
  construction, tags, events, and default registration;
- `src/Engine/Script.cpp`, `Script.h`, and `ScriptBind.h` for declarations, null
  references, unique-best overload scoring, list-loop lowering, hidden typed
  operations, return behavior, global tags, events, and compiled containers;
- `src/Mod/RuleStatBonus.cpp` for the complete ordered stat term table, coefficient
  scaling, generated source, and rounding;
- `src/Mod/RuleItem.cpp`, `Armor.cpp`, `RuleSoldierBonus.cpp`, and the other cataloged
  rule registration sites for local scripts and initial script values;
- `src/Savegame/BattleItem.cpp` and `BattleUnit.cpp` for the seven default bodies.

## Remaining boundary

Phase 4 completes compilation compatibility, not gameplay implementation. Reference
bindings without an installed provider intentionally fail at invocation with a missing
capability diagnostic. Text/reference host-frame execution, recursive/nested host
calls, mission/arc scheduling, and each player-visible binding are completed with their
strategic or tactical owner. Phase 5 owns persistence codecs and transactional restore
of script-value state. These boundaries do not weaken script syntax, declarations,
content acceptance, or event composition.

## Validation and performance

Unit coverage includes custom and pointer declarations, null initializers, typed tag
constants, rule-local/global compilation, stat conversion, defaults, and bounded-error
gating. The compatibility corpus builds the snapshot before producing its Phase 3
manifest. `Phase3ContentBenchmarks.LoadValidateAndCompileScripts` is the end-to-end
startup measurement for the new stage; benchmark execution remains a developer-machine
measurement rather than a shared-CI gate.
