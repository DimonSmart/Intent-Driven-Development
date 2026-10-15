# Updating IDD

`.idd/verification.yaml` is project-owned and is not automatically rewritten by a plugin update. Review or reconfigure it explicitly with `idd-verification-configure`.

Intent-Driven Development is actively developed. New releases may add workflows, improve agent instructions, fix problems, or clarify the methodology.

Update IDD periodically, especially when the documentation describes commands or behavior that are not available in the installed version.

This page updates the installed plugin code. It does not automatically migrate project-owned intent documents. After updating, review [Updates and Breaking Changes](updates-and-breaking-changes.md) for any repository changes that must be applied separately.

## Claude Code

Refresh the marketplace and update `idd-intent`:

```bash
claude plugin marketplace update intent-driven-development
claude plugin update idd-intent@intent-driven-development
```

When Factory is installed, update it as well:

```bash
claude plugin update idd-factory@intent-driven-development
```

## Codex

Refresh the marketplace snapshot and reinstall `idd-intent` from it:

```bash
codex plugin marketplace upgrade intent-driven-development
codex plugin add idd-intent@intent-driven-development
```

When Factory is installed, reinstall it as well:

```bash
codex plugin add idd-factory@intent-driven-development
```

## After Updating

1. Review [Updates and Breaking Changes](updates-and-breaking-changes.md) and apply any relevant project migrations.
2. Check the installed versions using [Verify Installation](verify-installation.md).
3. Start a new Claude Code or Codex session so the updated skills and instructions are loaded cleanly.

Repositories that already use IDD do not need to run `idd-project-init` again. Project-owned intent remains in the repository, while the installed plugin provides the updated workflows.

## Obsolete Factory commands after an update

Starting with the unified model-management interface, use
`idd-factory-configure` for both configuration and model refresh. The former
`idd-factory-update-effort-models` skill was removed from newly generated
Codex and Claude Code plugin packages; it is not an alias.

Already installed plugin snapshots, caches or open sessions can still display
the old command. Update or reinstall the **idd-factory** plugin using the steps
above, inspect the installed skills if it remains, remove stale local copies
according to your host's supported plugin management workflow, and start a
**new session**. Regenerating the marketplace alone does not delete host-local
plugin caches or change a running session. A separate `idd-intent` installation
may explain or route Factory requests, but requires the `idd-factory` plugin
for applying model policy.
