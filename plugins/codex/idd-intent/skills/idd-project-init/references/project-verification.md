# Project Verification Policy

`.idd/verification.md` is project-owned operational configuration, stored in Git beside `.idd/intent/`. It is not product intent, has no `IDD-NNNN` ID, and is not indexed in `.idd/intent/INDEX.md`. Intent specifications say what product behavior must be proved; this policy says which repository commands or user actions provide that evidence.

The file contains one normative YAML block:

````markdown
# Project Verification

```yaml
version: 1

checks:
  all:
    run: dotnet test

default:
  use:
    - all
```
````

`version: 1` supports four stable contexts: `direct`, `subtask`, `checkpoint`, and `final`. A check has a stable ID and exactly one of `run` or `instructions`. A `run` check may include `timeout` (`30s`, `2m`, `30m`, or `1h`) and may use `confirmation: required`. `instructions` are shown to the user and are `Not verified` until the user reports their result.

`default.use` is required. A context may have either `use` or ordered `rules`. Rules with `paths` apply only when they cover the complete changed scope; the first matching rule wins. A rule without `paths` is a context fallback and goes last. If none matches, use `default`. Do not merge matching rules or introduce `kind`, `fast`, `standard`, `extended`, `manual`, components, or a rule language.

Use changed paths for `direct`; contract scope plus actual changes for `subtask`; all `Covers` changes for `checkpoint`; and the Factory-run diff for `final`. A Subtask whose actual scope escapes its assigned rule returns `NEEDS_REPLAN`; it does not broaden verification itself.

If the policy is missing, continue with the repository/platform fallback: project script, Make/task-runner target, CI command, then the platform default. Report that fallback. Existing invalid policy must never silently fall back: invalid YAML or version blocks it; an error in a required check/rule blocks the current context; unused errors are warnings. Validate unknown contexts/checks, missing or dual `run`/`instructions`, `confirmation` without `run`, conflicting context `use`/`rules`, rules without `use`, missing `default`, and unsafe rule fallbacks.

Run only automated checks assigned to the context. Ask before a required confirmation check; a refusal is `Not verified` and cannot approve the context. Policy does not grant permissions or override sandbox, secret, network, destructive-command, or external-action restrictions. Record an unavailable check as `Not verified` with the precise reason and resumption condition.

A Subtask may return `DONE` only when every assigned `subtask` check has conclusive evidence. If any assigned check remains `Not verified`, return `BLOCKED` with `Reason`, `Verified`, `Not verified`, and `Resume when`; never complete the Subtask by recording missing evidence only.

The final reviewer owns execution of checks selected for context `final`. Before its verdict, reuse only conclusive evidence applicable to the current check definition and complete Factory diff, run every assigned automatic check that lacks such evidence, ask before `confirmation: required`, and present `instructions` checks to the user for an actual result. Read-only final review forbids implementation and state changes, not verification commands. Any assigned final check that remains `Not verified` requires a `blocked` verdict rather than approval.
