# Factory Planner/Worker installation and host capabilities

Factory has two user-facing commands, `idd-factory-run` and
`idd-factory-configure`, plus three internal Skills. The normal generator
packages each protocol from a unique canonical source into both platforms:

| Internal Skill | Canonical source | Installed relative path |
| --- | --- | --- |
| `idd-internal-factory-root` | `src/canonical/factory/root.md` | `skills/idd-internal-factory-root/SKILL.md` |
| `idd-internal-factory-planner` | `src/canonical/factory/planner.md` | `skills/idd-internal-factory-planner/SKILL.md` |
| `idd-internal-factory-worker` | `src/canonical/factory/worker.md` | `skills/idd-internal-factory-worker/SKILL.md` |

Both platform adapters generate Factory Skills directly from their canonical
sources, adding only platform frontmatter and packaging metadata. They do not
append behavioral instructions. Platform-native operation and model-parameter
rules are maintained in the canonical Skills and shared references; generated
Skill bodies are tested for exact equivalence with those sources.

## Active Factory v2 architecture

On this branch, `idd-factory-run` is the public two-level Supervisor:
it dispatches fresh internal Factory Roots, and each Root dispatches
fresh Planner/Worker children. All three roles are installed internal Skills;
Root is never a user-facing entrypoint. No flat orchestration fallback
is part of Factory v2.

Packaging or a Stage-0 smoke test does not prove live host compatibility.
Release acceptance requires real multi-batch E2E evidence before merging this architecture into `main`.

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
and trustworthy normal completion before another writer starts.
Codex Supervisor requests a one-hour Root `wait_agent(timeout_ms: 3600000)`
when allowed by the host schema; Root requests 10-minute Planner/Worker waits
(`timeout_ms: 600000`). If the host supports shorter waits only, use its
largest supported native blocking interval. These intervals are not overall
execution limits. On a normal `timed_out: true` result while a child runs,
re-arm the native wait on that same child, **never interrupt it just because
one wait expired**. Genuine native failure and uncertain termination still
block overlapping Writers and require explicit safe recovery.
Exceptional OS process cleanup is host-owned and does not require a
forced-cancellation preflight probe. Native model/
effort override support is not required to start Factory: Root checks it only
for the selected Worker's explicit `.idd/execution.yaml` mapping, immediately
before spawn. Inherited mappings need no override support. Separate per-child
sandboxing is optional; Planner must behave read-only and prefer host-enforced
read-only permissions when possible. Missing mandatory capabilities block with
a precise diagnostic, without a custom process runtime.

## Child Skill delivery

Supervisor derives the internal Root `SKILL.md` from the **actually loaded**
`idd-factory-run/SKILL.md`. Root derives its sibling Planner and Worker
`SKILL.md` paths from the **actually loaded internal Root Skill** in the
same installed plugin version, never from the source checkout, current project
folder, guessed cache version or global filename search. Root may check path
existence/access but must not read protocol bodies. Every generic child receives
its role, the exact accessible path to the installed Skill (use absolute paths,
correct quoting and Windows/Unix separators), the repository root and only a
bounded phase/task assignment. The child reads its **entire** Skill and any
mandatory references as its first substantive action, checks completeness and
returns `BLOCKED` without project work if invalid.

Codex requires `fork_turns: "none"` and uses batch-scoped `task_name`
values such as `idd_factory_root_b0001`,
`idd_factory_planner_b0001_0001` and `idd_factory_worker_b0001_0001`
for correlation. Older flat task names are accepted only by the reporter. A session without `agent_type`
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


## Two-level execution and native gating

The public Supervisor must find its internal Root in the same installed
package; Root must find internal Planner and Worker there. Codex uses
`idd_factory_root_b0001` and batch-scoped
`idd_factory_(planner|worker)_b0001_0001` native task names, all with
`fork_turns: "none"`. Supervisor never runs a Planner or Worker itself.
On each CONFIRMED FULL PLAN boundary after the Root reaches or
exceeds the positive `max_workers_per_batch` target (default 8),
Supervisor may start one fresh Root; reaching the target in the
middle of a Planner plan does NOT trigger rotation. Newly generated
Planner results contain at most 4 Tasks; previous saved plans may
contain more. Neither an empty plan nor the terminal text alone proves
successful completion. Supervisor does not auto-retry after a failure. No additional installed agent definitions
or changes to `.idd/execution.yaml` are needed.

The ordinary Linux/Windows CI proves generator contracts, syntax and report
fixtures. The user-facing nested architecture MUST additionally pass the
effective model/effort override and explicit 10-task limit-3 plan-safe E2E on
the intended Codex host. The Stage-0 read-only smoke result alone does not
prove multi-batch completion. Emergency cancellation tests are optional host
diagnostics, not a release gate. The PR is gated until those
real-host checks pass; Claude requires separate runtime evidence.

