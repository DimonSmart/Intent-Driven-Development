# Project Verification Policy

`.idd/verification.yaml` is project-owned operational configuration, stored in Git beside `.idd/intent/`. It is not product intent, has no `IDD-NNNN` ID, and is not indexed in `.idd/intent/INDEX.md`. Intent specifications say what product behavior must be proved; this policy says which repository commands or user actions provide that evidence.

The file is YAML and is parsed directly as a complete YAML document:

```yaml
version: 1

checks:
  tests:
    run: dotnet test AiTestTickets.slnx
    timeout: 2m

default:
  use:
    - tests
```

Do not add Markdown headings or fenced code blocks. Markdown content in
`.idd/verification.yaml` is invalid YAML and blocks policy loading.

`version: 1` supports four stable contexts: `direct`, `subtask`, `checkpoint`, and `final`. A check has a stable ID and exactly one of `run` or `instructions`. A `run` check may include `timeout` (`30s`, `2m`, `30m`, or `1h`) and may use `confirmation: required`. `instructions` are shown to the user and are `Not verified` until the user reports their result.

`default.use` is required. A context may have either `use` or ordered `rules`. Rules with `paths` apply only when they cover the complete changed scope; the first matching rule wins. A rule without `paths` is a context fallback and goes last. If none matches, use `default`. Do not merge matching rules or introduce `kind`, `fast`, `standard`, `extended`, `manual`, components, or a rule language.

Use changed paths for `direct`; task scope plus actual changes for `subtask`;
all covered changes for `checkpoint`; and the integrated Factory change for
`final`.

If `.idd/verification.yaml` is missing, continue with the repository/platform
fallback: project script, Make/task-runner target, CI command, then the platform
default. Report that fallback. Factory does not create a separate verification
state machine around this policy.

Other files, including Markdown files under `.idd/`, do not affect policy discovery or fallback. An existing YAML policy must never silently fall back: invalid YAML, Markdown headings or fenced YAML, an unsupported version, or a schema error blocks policy loading and the current operation. Validate unknown contexts/checks, missing or dual `run`/`instructions`, `confirmation` without `run`, conflicting context `use`/`rules`, rules without `use`, missing `default`, and unsafe rule fallbacks.

Run only automated checks assigned to the context. Ask before a required confirmation check; a refusal is `Not verified` and cannot approve the context. Policy does not grant permissions or override sandbox, secret, network, destructive-command, or external-action restrictions. Record an unavailable check as `Not verified` with the precise reason and resumption condition.

For direct non-Factory implementation, completion requires conclusive evidence
for every assigned check. Missing user action remains `Not verified` and blocks
approval.

For Factory execution, each worker performs reasonable task-local checks itself.
Factory does not own a verification engine, retry budget, attempt lifecycle, or
verification evidence graph.

When a fresh planner returns exactly `# Done`, the orchestration layer runs the
configured project `final` verification when available. On success, Factory
completes. On failure, persist only a bounded diagnostic containing the failed
check/command, exit result, and relevant output, then invoke a fresh planner.
That planner decides whether a correction task is required.

If no project verification policy is configured, `# Done` after normal worker
checks is sufficient for Factory completion.
