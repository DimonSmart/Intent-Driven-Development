# IDD Factory native Planner/Worker — capability gate (2026-11-09)

**Decision: BLOCKED at stage A; production promotion NOT AUTHORIZED.**

This is a non-normative capability audit against
[`main` `3e8934be97cdf3bc45e3871b6d025f7f101b85c3`](https://github.com/DimonSmart/Intent-Driven-Development/commit/3e8934be97cdf3bc45e3871b6d025f7f101b85c3).
It records evidence and missing evidence for the requirements in
`IDD-Factory-Native-Agents-TZ-reviewed-2026-11-09`; it is **not** a successful
native E2E test or a migration release.

## Actual baseline at the pinned commit

- `src/canonical/factory/planner.md` and `worker.md` are canonical role protocols.
- `src/canonical/plugins/plugin-manifest.json` has `idd-factory.roles: []`.
  `idd-factory-run` references both full protocol documents as
  `factory-planner.md` and `factory-worker.md`.
- The normal `Generator.Run` path does **not** register native Planner/Worker
  agent types. `InternalAgentExperiment.Apply` is conditional on
  `--internal-agent-experiment` and emits `skills/idd-internal-factory-*/SKILL.md`,
  not native role definitions.
- `CodexPlatformAdapter` currently describes a native generic spawn and
  `ClaudePlatformAdapter` describes a native subagent/fork, but neither
  adapter generates a registered Factory Planner/Worker definition.
- The historical measurements in
  [internal-agent-loading.md](internal-agent-loading.md) are for *Skill
  discovery*, not native agent registration. Do not relabel them as native
  agent evidence or comparable full-run cost data.
- `.idd/intent/IDD-0001.spec-factory-orchestration.md` is still normative
  for Root-relayed role instructions. It cannot be rewritten without the
  authorized `idd-intent-change` owner workflow.

## Platform documentation reviewed

- [Codex custom agents](https://learn.chatgpt.com/docs/agent-configuration/subagents):
  standalone custom agent files live in `.codex/agents/*.toml` or
  `~/.codex/agents/*.toml`, with `name`, `description`, and
  `developer_instructions`. Agent definitions inherit sandbox/approval
  configuration from the parent. Definition-level `model` and
  `model_reasoning_effort` take precedence over spawn settings.
- [Codex plugin packaging](https://developers.openai.com/plugins/build/plugins):
  the documented portable package and local Marketplace installation are
  distinct from project/user custom-agent discovery.
  [OpenAI's example-plugin repository](https://github.com/openai/plugins/blob/main/README.md)
  mentions plugin-level `agents/` as a possible companion surface, but
  that mention alone does **not** prove that a normal installed IDD plugin
  registers callable Factory agent types in any target Codex host/version.
- [Claude custom subagents](https://code.claude.com/docs/en/sub-agents):
  installed plugin `agents/*.md` files are discovered under plugin-scoped
  names; the YAML frontmatter is not a Skill. The Markdown body becomes the
  child's system prompt. Plugin subagents ignore `permissionMode`,
  `hooks` and `mcpServers` in frontmatter.
- [Claude plugin manifest](https://code.claude.com/docs/en/plugins-reference):
  the `agents` component accepts Markdown paths; conventional plugin
  `agents/` discovery is documented.
- Claude's documented **per-invocation Agent tool** `model` parameter accepts
  family aliases (`sonnet`, `opus`, `haiku`, `fable`), **not** an arbitrary
  concrete model ID or `inherit`. A full model ID can appear in a static
  subagent definition, which does not solve Factory's dynamic
  `.idd/execution.yaml` per-Task override requirement by itself. Do not
  silently map concrete IDs to aliases; resolve through a verified native
  mechanism or block the affected Worker.

## Required host-native evidence

| Gate | Codex | Claude Code |
| --- | --- | --- |
| Documented role-definition format | DOC: project/user TOML | DOC: plugin Markdown |
| Normal Marketplace install exposes callable role | **NOT VERIFIED** | **NOT VERIFIED** |
| Native spawn selects exact registered role | **NOT VERIFIED** | **NOT VERIFIED** |
| Host loads full canonical instructions into child (without Root reading) | **NOT VERIFIED** | **NOT VERIFIED** |
| Fresh context, repository access, wait/stop lifecycle | **NOT VERIFIED** | **NOT VERIFIED** |
| Assignment guard and direct-invocation negative test | **NOT VERIFIED** | **NOT VERIFIED** |
| Planner read-only behavior and actual permissions | **NOT VERIFIED**; sandbox inherited | **NOT VERIFIED**; allowed/disallowed tools need Live test |
| Exact Worker model/effort and `inherit` | **NOT VERIFIED** | **BLOCKER for arbitrary concrete IDs via Agent per-call model; alternative mechanism NOT VERIFIED** |
| Missing/conflicting role fails closed | **NOT VERIFIED** | **NOT VERIFIED** |
| Native usage and paired token/latency measurements | **NOT VERIFIED** | **NOT VERIFIED** |

**Test environment:** the shell available for this audit has no `codex`,
`claude`, or `dotnet` executable. Consequently no installed Marketplace
test, child trace, native usage counter, generator run or test execution was
possible here. No specific host version was available to pin. This is an
**evidence gap**, not evidence that either host lacks native agents.

## Minimal path to unblock stage A

1. In an isolated disposable user/home environment, record `codex --version`
   and `claude --version`, plus exact install method, Marketplace revision
   and effective plugin directory. Do **not** write to a real user's global
   agent files or project configuration without explicit authorization.
2. Codex: install an actual IDD Marketplace candidate using the ordinary
   supported path; verify whether plugin-supplied native TOML definitions are
   discovered. If not, identify and demonstrate the exact approved supported
   registration/install scope and explicitly document any manual step.
   Confirm name conflict precedence, `agent_type`, spawn schema and
   model/effort evidence from native traces.
3. Claude: install an actual plugin with `agents/*.md`, verify both
   plugin-scoped identifiers through a real child spawn and capture its
   system-prompt provenance and tool/permission behavior. Find and test
   a supported **per-spawn** exact concrete model-ID and effort mechanism
   compatible with the existing policy, or explicitly mark these mappings
   unsupported and ensure the relevant Worker blocks without fallback.
4. For each platform, test missing/duplicate/corrupt definitions,
   direct invocation without Factory assignment, bounded task packets,
   native wait/termination, failure/recovery, `inherit` and an explicit
   model+effort, as well as Engineering Always + Conditional rules.
5. Only when **both** platforms pass all mandatory tests, proceed to
   canonical-derived native definitions, generation/contract tests,
   authorized Intent-owner updates, full Live E2E and paired efficiency
   measurement; then, and **only then**, consider production promotion and
   deleting the historical experiment mechanism.

## State preserved

No canonical protocols, Root orchestration, generated Marketplace artifacts,
legacy experiment, production generation defaults, `.idd/execution.yaml`,
or normative Product Intent owners were modified for this gate. The current
production behavior remains the only supported path until promotion.
An experimental Skill is **not** interchangeable with a registered native
agent; there is no generic-agent fallback for the intended new mode.
