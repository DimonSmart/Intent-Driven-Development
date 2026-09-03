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

Use changed paths for `direct`; contract scope plus actual changes for `subtask`; all `Covers` changes for `checkpoint`; and the Factory-run diff for `final`. Unexpected task scope is recorded as runtime-observed change evidence and is assessed by the planner after the batch is exhausted.

If `.idd/verification.yaml` is missing, continue with the repository/platform fallback: project script, Make/task-runner target, CI command, then the platform default. Report that fallback. Before the first Factory planning cycle, when a repository fallback check exists, the deterministic Runtime runs that same fallback once against the unchanged workspace as a baseline. If the baseline passes, or no fallback check exists, normal planning begins.

If that baseline fails, stop before any planner or executor invocation and ask the user whether to continue with the already-red repository. The recommended action is to fix the repository baseline separately and restart. If the user explicitly approves continuation, persist only `RepositoryFallbackBaselineAccepted = true`; do not parse test/build output and do not classify individual failures. With an accepted red baseline, a failed repository fallback during `subtask` verification is recorded as evidence but does not retry the current work item, because the broad fallback was already failing before the task. Strict `final` repository fallback must still pass before completion; if it remains red, block with `FINAL_VERIFICATION_FAILED` rather than starting a corrective planning cycle. Runner infrastructure failure is not an acceptable red baseline and still blocks before planning. This baseline behavior applies only when the YAML policy is absent; an existing policy remains authoritative and is not pre-run as a repository baseline.

Other files, including Markdown files under `.idd/`, do not affect policy discovery or fallback. An existing YAML policy must never silently fall back: invalid YAML, Markdown headings or fenced YAML, an unsupported version, or a schema error blocks policy loading and the current operation. Validate unknown contexts/checks, missing or dual `run`/`instructions`, `confirmation` without `run`, conflicting context `use`/`rules`, rules without `use`, missing `default`, and unsafe rule fallbacks.

Run only automated checks assigned to the context. Ask before a required confirmation check; a refusal is `Not verified` and cannot approve the context. Policy does not grant permissions or override sandbox, secret, network, destructive-command, or external-action restrictions. Record an unavailable check as `Not verified` with the precise reason and resumption condition.

For direct non-Factory implementation, completion requires conclusive evidence
for every assigned check. Missing user action remains `Not verified` and blocks
approval.

For Factory execution, the deterministic Runtime owns mandatory `subtask` and
`final` verification. Semantic workers may run focused
diagnostic commands, but those commands are not authoritative Factory evidence.
The Runtime records every gate result, distinguishes ordinary failed checks from
required user action and runner infrastructure failure. Failed subtask checks
retry the same task and failed final checks trigger a new planning cycle, except
for repository fallback after an explicitly accepted red baseline as described
above.
