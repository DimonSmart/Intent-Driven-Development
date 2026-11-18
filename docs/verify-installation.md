# Verify IDD Installation

Use these checks after the [README Quick Start](../README.md#quick-start) or when IDD commands are not available in a new session.

## Claude Code

List configured marketplaces and installed plugins:

```bash
claude plugin marketplace list
claude plugin list --json
```

The marketplace list should include `intent-driven-development`. The installed plugin list should include `idd-intent`.

`idd-factory` should appear only when it was installed explicitly.

## Codex

List configured marketplaces and installed plugins:

```bash
codex plugin marketplace list
codex plugin list --json
```

The marketplace list should include `intent-driven-development`. The installed plugin list should include `idd-intent`.

`idd-factory` should appear only when it was installed explicitly.

For **Factory**, both internal Planner/Worker Skills ship in the installed
`idd-factory` plugin. Restart the host session after updating plugins. Verify
both Skills exist in the same installed package as `idd-factory-run` and that
ordinary child agents can start without inheriting the Root transcript,
read installed files, wait natively and stop safely. A missing `agent_type`
selector alone is not an error. An observable child read of the entire Skill
is required to claim live behavior; a generated file or `task_name` is not
proof. Older custom-agent TOML files do not need to be removed.
See [Factory installation](factory-native-agent-installation.md).

## Verify Clarify and Brainstorm

Inspect the installed `idd-intent` plugin and confirm that both `skills/idd-intent-clarify/SKILL.md` and `skills/idd-intent-brainstorm/SKILL.md` exist. The latter must also contain `references/brainstorm-method.md` on Codex and Claude Code. These are optional read-only automatic workflows, not additional marketplace plugins. In a fresh session, ask the agent to classify a narrow ambiguous Product requirement (Clarify) and a request for fundamentally different product strategies (Brainstorm), without execution. Clear approved requirements should still route directly to `idd-intent-change`.

## Verify Repository Initialization

After running:

```text
idd-project-init
```

verify that:

- the repository contains the minimal `.idd/intent/` structure;
- the active agent instruction file contains the managed IDD integration section;
- plugin skills were not copied into the repository.

Installation and repository initialization are separate. A correctly installed plugin can be used across repositories, while `idd-project-init` prepares each target repository individually.

## If Verification Fails

Return to the [README Quick Start](../README.md#quick-start) and repeat the marketplace and plugin installation commands for the affected client.

When the marketplace exists but the plugin version is stale, follow [Updating IDD](updating-idd.md).

Start a new Claude Code or Codex session after installation or update so the installed skills and instructions are loaded cleanly.
