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

The Factory plugin packages `agents/*.toml` native definitions, advertises
the component as `agents` in its plugin manifest, and uses canonical
`developer_instructions`. Neither definition pins a model or reasoning
effort. The Root requests the registered `agent_type`; `task_name`
identifies an attempt, not the agent type.

Codex also documents project/user custom-agent search locations:
`.codex/agents/*.toml` and `~/.codex/agents/*.toml`. **Do not copy the
plugin's definitions into those locations or alter global configuration
silently.** Whether the specific installed Codex version discovers plugin
`agents/` directly must be checked with the real Marketplace installation.
If a version requires a user-approved project/user registration step, state
that prerequisite and perform it only with the user's permission. The isolated
`Factory_UsesNativeAgentsAndCompletesTwoStepCatalog` evaluation deliberately
copies **generated** agent definitions into its disposable workspace's
`.codex/agents/` before committing the test baseline. This is an
**eval-only** registration mechanism, not proof that a normal Marketplace
install discovers plugin-supplied agents and not an instruction to modify
real projects. `Factory_MarketplaceAloneDiscoversNativeAgents` is a
separate explicit opt-in Live probe without this copy. Missing,
conflicting or shadowed definitions block Factory; there is no generic-agent
fallback. Codex Planner is read-only **by role behavior**, not by a separate
sandbox: native custom agents may inherit the parent's sandbox/approvals.

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
