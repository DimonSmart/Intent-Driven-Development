# Updating IDD

Intent-Driven Development is actively developed. New releases may add workflows, improve agent instructions, fix problems, or clarify the methodology.

Update IDD periodically, especially when the documentation describes commands or behavior that are not available in the installed version.

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

Check the installed versions using [Verify Installation](verify-installation.md).

Start a new Claude Code or Codex session so the updated skills and instructions are loaded cleanly.

Repositories that already use IDD do not need to run `idd-project-init` again. Project-owned intent remains in the repository, while the installed plugin provides the updated workflows.
