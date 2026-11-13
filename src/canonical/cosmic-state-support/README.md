# Optional COSMIC execution-state helper

`cosmic_state.py` is a **portable Python 3 standard-library-only helper** bundled as an optional asset of the `idd-intent-cosmic` plugin. It is not a replacement for semantic agent reasoning or COSMIC certification, and no Python runtime is required to invoke the core skill.

It is permitted **only** for: SHA-256 source/packet fingerprints, atomic state checkpoints outside the target project, changed-source and dependent-source invalidation using already reported `LINK` records, exact owner/link identifier checks, status verification, Rule 10c structure, and E/X/R/W arithmetic. It neither discovers nor semantically deduplicates functional processes.

A host may use an equivalent built-in deterministic tool instead. When no reliable persistence/hash mechanism is available, the skill must not claim resumability or mechanically validated completion.

Example, with `STATE` **outside** the target repository and a declared measurement boundary:

```sh
python3 cosmic_state.py init --project /path/to/project --state-dir /safe/state/run-1 --boundary product-api --strategy-file /safe/state/run-1/strategy.md
python3 cosmic_state.py status --state-dir /safe/state/run-1
python3 cosmic_state.py mark --state-dir /safe/state/run-1 --source IDD-0001.spec-api.md --packet /safe/state/run-1/packet-0001.md
python3 cosmic_state.py verify --state-dir /safe/state/run-1 --records /safe/state/run-1/records.txt --complete
python3 cosmic_state.py invalidate --state-dir /safe/state/run-1 --records /safe/state/run-1/records.txt
```

The `strategy.md` file is created by the coordinator in the same safe state directory and records scope, inclusions/exclusions, boundary, users, layer and granularity; its SHA-256 is checked before reuse. Changing measurement strategy requires a new run. The helper currently inventories **all** current numbered specs; for a partial-scope run, use a separate host-native scoped ledger rather than claiming that the helper verified that scope.\n\nThe optional flat `records.txt` contains **agent-validated** facts, not guesses inferred from filenames:

```text
OWNER | IDD-0001#P1 | IDD-0001.spec-api.md | # Create record
LINK | SRC:IDD-0002.spec-ui.md | IDD-0001#P1 | IDD-0002.spec-ui.md | # Uses create record
MOVE | IDD-0001#P1 | E | Record input | IDD-0001.spec-api.md#Create
MOVE | IDD-0001#P1 | W | Record details | IDD-0001.spec-api.md#Create
MEASURE | IDD-0001#P1 | product-api | E=1,X=0,R=0,W=1 | CFP=2
```

`SRC:` means the referring spec is known but its calling process is not, so it counts towards **referring-specs**, not confirmed caller-process fan-in.

The helper accepts only one normative OWNER per exact owner ID. It cannot identify duplicate definitions with *different* IDs; those are structural Intent issues for lint/audit and must not be reinterpreted as COSMIC deduplication. Every `MOVE` and `MEASURE` record must come from the process-measurer LLM and source evidence. The helper mechanically checks recorded counts only.

`init` does not overwrite existing runs; `invalidate` preserves unchanged completed packets and invalidates changed sources plus known dependent sources, but requires a **new run** if files were added/removed (scope change). It does not silently alter the measurement boundary. Reused packets must be reviewed for semantic consistency by the coordinator.

Run the deterministic regression tests with:

```sh
python3 -m unittest discover -s src/canonical/cosmic-state-support -p 'test_*.py'
```
