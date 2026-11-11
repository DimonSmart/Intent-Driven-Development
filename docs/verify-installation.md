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

For **Codex Factory**, installation and native role registration are separate.
Ask `idd-factory-configure` to check/register native roles for the selected
project or user scope. A plugin in `codex plugin list` or TOML in the Marketplace
cache does not establish registration. Restart Codex after authorization and
registration; validate explicit `agent_type`, both role identities, loaded
instructions and native wait via a no-assignment `BLOCKED` probe. Codex Desktop
sessions whose spawn schema hides `agent_type` remain unsupported even if the
CLI registers roles. See [installation details](factory-native-agent-installation.md).

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
