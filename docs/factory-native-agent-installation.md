# Factory native Planner/Worker: packaging and host setup

The normal Marketplace generator emits two registered **candidate** native
Factory roles, derived from exactly one source per role:

| Logical role | Canonical source | Codex plugin | Claude plugin |
| --- | --- | --- | --- |
| `idd-internal-factory-planner` | `src/canonical/factory/planner.md` | `agents/idd-internal-factory-planner.toml` | `agents/idd-internal-factory-planner.md` |
| `idd-internal-factory-worker` | `src/canonical/factory/worker.md` | `agents/idd-internal-factory-worker.toml` | `agents/idd-internal-factory-worker.md` |

Each generated role includes the canonical
`src/canonical/methodology/engineering-guardrails.md` in the child's own
instructions. Never edit generated files independently. The user-facing Factory
Skills remain **only** `idd-factory-run` and `idd-factory-configure`.

## Claude Code

Install `idd-intent` and `idd-factory` from the supported IDD Marketplace.
The Factory plugin uses Claude's conventional `agents/*.md` layout. The
expected native `subagent_type` identifiers are
`idd-factory:idd-internal-factory-planner` and
`idd-factory:idd-internal-factory-worker`. The actual installed host must
confirm these exact names before Factory creates any durable preparation.

Planner has `Read`, `Glob` and `Grep` only, with write/shell/MCP tools
disallowed in its definition. Worker has read, edit, write and command tools.
The plugin-agent fields `permissionMode`, `hooks`, and `mcpServers` are not
used because they are not enforcement for plugin subagents. Direct invocation
without a verified Factory assignment is rejected by the role guard, but the
role may be visible in user autocomplete; invisibility is not an access control.

`claude.model` and `claude.effort` mappings are applied only when supported
as exact native per-child settings; there is no ID-to-alias conversion.
The availability of an exact concrete model ID and a per-invocation effort
control must be verified together on the installed Claude Code version; an
effort-capable model by itself is not evidence that the required exact native
child overrides are exposed. Unsupported explicit settings stop the affected
Worker and suggest `idd-factory-configure`. Omitted effort on an explicit
model mapping lets the child host choose its own default, while an absent
mapping inherits model and reasoning behavior.

## Codex

The Codex plugin ships generated native TOML in `agents/*.toml`, derived from
the canonical Planner/Worker protocols. **Marketplace installation alone does
not make these roles callable in Codex CLI 0.167.1.** Its plugin manifest parser
does not consume the `agents` field, while role discovery scans the active
project/user Codex configuration layers. A Marketplace-only live probe returned
`unknown agent_type` for both roles. Do not treat files in plugin cache as
evidence that the host registered them.

### One-time role registration after Marketplace installation

In a new Codex CLI session, run `idd-factory-configure` with a request such as:

```text
Register the IDD Factory native Planner and Worker for this project.
```

The skill locates the bundled `scripts/register-codex-agents.py` inside the
**installed** Factory plugin and runs its read-only `--check` first. With
explicit authorization it runs `--apply` for the selected scope. The script
copies exact generated TOML from the same plugin version.

For advanced/manual use, resolve the script's actual installed path (not a
hardcoded cache version), then run:

```bash
python3 "/absolute/path/to/installed/idd-factory/skills/idd-factory-configure/scripts/register-codex-agents.py" --scope project --project "/absolute/path/to/repository" --check
# After reviewing the target files and explicitly authorizing installation:
python3 "/absolute/path/to/installed/idd-factory/skills/idd-factory-configure/scripts/register-codex-agents.py" --scope project --project "/absolute/path/to/repository" --apply
```

Use `--scope user` only when explicitly requested. User scope writes to
`$CODEX_HOME/agents/` (or `~/.codex/agents/` when CODEX_HOME is unset).
Existing identical definitions are left unchanged; differing definitions
block installation. `--replace` requires explicit authorization and retains
content-addressed backups. The helper never writes `config.toml`, trust,
`.idd/execution.yaml`, Product Intent or Factory state. Python 3 is required
for this helper; manual registration is possible when unavailable.

Project scope requires Codex to **trust the project**; only the user should
grant trust through supported Codex controls. The script does not alter trust.
After registration or an IDD plugin update, start a new Codex CLI session so
role definitions are reloaded, then run the read-only native role probe.
Validate the actual child role identity and host-loaded instructions.

Codex native calls must support explicit `agent_type` and `fork_turns: "none"`,
wait/stop, repository access and per-child model overrides when configured.
Some Codex Desktop collaboration tool surfaces omit `agent_type` even when the
bundled CLI executable supports it; registering TOML does **not** fix that
Desktop host limitation. Use a compatible CLI session instead.

Factory may not replace a registered Planner/Worker with a generic agent.
`task_name` identifies a child attempt, not the agent type. A failure to
resolve roles must block before any durable write with a concrete diagnostic.
Determine host support from the real top-level native schema, never only from
`functions.exec` or `ALL_TOOLS` inventory inside another tool.

## Host capability preflight

Before owner writes on a new run, and before every corresponding spawn:
resolve registered types and host support for fresh context (no parent
transcript), current repository access, native terminal wait/termination,
model/effort precedence and complete host-loaded instructions. Always use
bounded packets with phase, verified baseline/provenance, current request,
exact Q/A and (Worker) one Task plus selected ENG/IDD IDs. Never pass a full
role protocol from Root or a prompt instructing generic agents to find a
Skill. Failed evidence means `BLOCKED` with role/platform/phase/settings.

A direct role mention is not authorization. The child independently validates
the assignment and required references. No new Worker may start while the
previous Writer may remain active. An unavailable exact model override or
missing verified child termination is not permission to retry generically.

## Deferred Live verification (explicitly NOT VERIFIED)

Implementation and static contracts are supplied here, but no claim is made
that generated files have been discovered or executed by the target hosts.
Before claiming end-to-end completion, capture host version, installed path,
resolved role type and trace for Codex and Claude. Cover direct invocation
without packet, conflicts/missing definitions, current Always + Conditional
Engineering rules, `inherit` and explicit model/effort, interruption, native
wait/stop, sequential Workers, final verification recovery, and canonical
instruction provenance. Record actual permission/sandbox behavior.

Measure baseline vs candidate with the existing Factory reporting/eval
fixtures on matched inputs, models and verification. Report Root/children
input, cached input, new input, output, complete totals where available,
spawns, elapsed time and pass/fail. The older
[Skill-loading experiment](experiments/internal-agent-loading.md) remains
historical and must not be reported as proof of registered-agent savings.

This document describes expected integration and remaining evidence, not an
assertion that unsupported host behavior was tested.
