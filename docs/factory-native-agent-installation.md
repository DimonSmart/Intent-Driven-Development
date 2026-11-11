# Factory Planner/Worker installation and host capabilities

Factory has two user-facing commands, `idd-factory-run` and
`idd-factory-configure`, plus two internal Skills. The normal generator
packages each protocol from a unique canonical source into both platforms:

| Internal Skill | Canonical source | Installed relative path |
| --- | --- | --- |
| `idd-internal-factory-planner` | `src/canonical/factory/planner.md` | `skills/idd-internal-factory-planner/SKILL.md` |
| `idd-internal-factory-worker` | `src/canonical/factory/worker.md` | `skills/idd-internal-factory-worker/SKILL.md` |

## Normal setup

Install `idd-intent` and optional `idd-factory` using the host's normal
Marketplace installation commands (see the repository README). Open a **new
session** after installing or updating the plugins. Invoke `idd-factory-run`
for suitable implementation requests, directly or through the existing route.
No extra registration is necessary in `.codex/agents/`, `~/.codex/agents/`
or Claude custom-agent settings. The package contains no Factory-specific
`agents/*.toml` or `agents/*.md` definitions. `idd-factory-configure` only
manages optional `.idd/execution.yaml` model-selection policy.

The host must provide ordinary native children with fresh context (no inherited
parent transcript), shared workspace and access to the installed package,
bounded task delivery, terminal result, native blocking or event-driven wait,
and reliable interruption/stop before another writer starts. Exact explicit
per-child model/effort overrides must be possible for configured policy values.
Separate per-child sandboxing is optional; Planner must behave read-only and
prefer host-enforced read-only permissions when possible. Unsupported actual
capabilities block with a precise diagnostic, without a custom process runtime.

## Child Skill delivery

Root derives both internal `SKILL.md` paths from the **actually loaded**
`idd-factory-run/SKILL.md`, never from the source checkout, current project
folder, guessed cache version or global filename search. Root may check path
existence/access but must not read protocol bodies. Every generic child receives
its role, the exact accessible path to the installed Skill (use absolute paths,
correct quoting and Windows/Unix separators), the repository root and only a
bounded phase/task assignment. The child reads its **entire** Skill and any
mandatory references as its first substantive action, checks completeness and
returns `BLOCKED` without project work if invalid.

Codex requires `fork_turns: "none"` and uses `task_name` values such as
`idd_factory_planner_01` for correlation. A session without `agent_type`
can run Factory. Do not invent an `agent_type` parameter; if a host requires
an agent type, use only its built-in generic type. Claude uses a native generic
subagent/fork with equivalent isolation and lifecycle guarantees. Avoid
`context: fork` on the internal Claude Skill itself, as Root already spawned
the child. A `task_name` is neither proof of Skill loading nor model/effort
attestation: use real child trace reads and native settings for those claims.

## Legacy files and verification

Older installs may have registered `idd-internal-factory-*.toml` under a
user/project scope. Factory now ignores those definitions: do not overwrite,
remove or migrate them automatically. Updating plugins does not modify
`.idd/execution.yaml`, verification policy, current Product Intent or running
Factory state. Existing pending runs retain their scope/provenance contracts.

Use the generated candidate in an isolated test home and install both plugins
via Marketplace. Run the multi-task `TwoStepCatalog` E2E, a missing-assignment
BLOCKED probe, exact model/effort and unavailable-model scenarios, interruption
recovery and final verification. Capture parent/child correlation, full
installed-Skill reads in child rollouts, no protocol bodies in Root spawn
messages, native no-history settings, sequential Writers and independent
product verification. CLI and Claude compatibility do not prove Desktop
compatibility; each host must be verified on its own. A real Codex Desktop run
with no `agent_type` selector is needed to close the original regression.
