---
name: idd-factory-execute-subtask
description: Execute one focused workspace-writing implementation work item in an isolated worker context.
---

# idd-factory-execute-subtask

## Purpose

Execute the assigned immutable task and report what actually happened.

## Inputs and boundaries

Use the supplied self-contained task contract, supplied task-related durable
intent, relevant completed task results, current repository state, prior results
for this same task, and authoritative verification failures from earlier
attempts.

The task contract defines the concrete work to perform. Supplied task-related
durable intent is normative product input. Both constrain implementation. The
planner has already selected the stable intent references for this work item.
Factory has persisted those references, resolved them mechanically, and loaded
the complete current contents of the selected documents. Correctness for
explicitly supplied documents must not depend on rediscovering them from
`.idd/intent`.

You may still inspect current repository state, additional code, additional
durable intent, or the optional glossary when a genuine implementation
discovery makes that necessary. Do not routinely scan `.idd/intent` as a
substitute for the task-related durable intent Factory supplied.

## Repository discovery

Treat ordinary repository discovery as task-scoped and Git-visible. Start from
the task contract, supplied task-related intent, paths and names already known
from the task, files and symbols already discovered, and the structure of the
relevant part of the project. If the search area can be narrowed, investigate
only that area rather than inventorying the whole workspace.

When file enumeration is needed in a Git repository, use this as the normal
visibility model:

```bash
git ls-files --cached --others --exclude-standard
```

This is the Git-visible workspace: tracked + untracked non-ignored files. A
tracked file remains visible even if it later matches `.gitignore`; a new
untracked non-ignored file is visible; an untracked ignored file is omitted; and
a generated file that is not ignored remains visible. Do not add a separate
notion of generated-file visibility.

Prefer a scoped pathspec whenever the current task gives enough information to
narrow the search, for example:

```bash
git ls-files --cached --others --exclude-standard -- src tests
```

`src` and `tests` are only an example, not an assumed repository layout. Choose
pathspecs from the actual task and repository structure. Enumerating the complete
Git-visible workspace is acceptable only when the area cannot reasonably be
narrowed first.

Do not use a broad physical recursive filesystem scan as the default way to
understand a repository. Commands such as `Get-ChildItem -Recurse`, `find .`,
`dir /s`, or equivalent whole-workspace scans should not be ordinary discovery.
A narrow physical scan of a specific directory is allowed when the task gives a
concrete reason to inspect that directory's physical contents.

After identifying the relevant area, prefer focused investigation: search for a
specific symbol or file name, use `rg` for a specific string or pattern, read
specific files, and search within relevant directories. Do not first produce a
large file tree when a focused search can answer the same question. Do not use
ignore-bypassing search options such as `--no-ignore`, `-uuu`, or equivalents
without a concrete task-related reason.

Ignore semantics limit ordinary discovery; they do not prohibit access.
Ignored/generated paths are not forbidden. When a concrete reason arises, you
may directly read a known ignored/generated file, inspect a specific generated
directory, or perform a narrow physical scan of that specific path. Examples
include an explicitly named artifact, build output relevant to a failure,
generated source named by an error, or a log/cache/intermediate artifact that a
focused investigation made relevant. Do not expand such targeted access into a
recursive scan of the whole workspace.

If Git-visible enumeration is unavailable because the workspace is not a Git
repository or the Git command fails, fall back to filesystem discovery, but keep
the fallback task-scoped whenever possible. Do not replace a failed
`git ls-files` automatically with a recursive scan of the complete workspace.
Do not infer that a file is generated or irrelevant from directory names,
extensions, or hardcoded generated-directory lists; semantic relevance remains
an executor decision within the mechanical visibility rules above.

Make the smallest coherent product change that satisfies the contract and its
normative task-related intent. You may inspect focused code and run focused
development checks. Runtime performs the authoritative verification and
deterministically retries this same immutable task when a required check fails.
Retries preserve the task contract and selected intent IDs; Factory reloads the
current contents of those same documents for every invocation.

Do not mutate `.idd/factory/current`, `.idd/intent`, `.idd/factory.yaml`, or the
verification policy. Do not plan later Factory work, create tasks, choose a
worker or capability, decide whether the original Factory request is complete,
request replanning, or select a runtime transition.

If the task exposes an unexpected prerequisite, defect, architectural
constraint, or incomplete portion, describe that fact plainly in the semantic
report. Do not broaden the task merely to hide the discovery. Runtime will
finish the current batch, and the next planner will decide whether new work is
needed.

## Output

Return concise but complete human-readable Markdown describing what was
actually done, discovered, or left unresolved. Use the natural structure that
best fits this task; no fixed sections or fields are required.

Do not return JSON or an orchestration outcome. In particular, do not return
`completed`, `approved`, `correction-required`, `additional-work-required`,
`global-replan-required`, `intent-required`, `blocked`, `next`, `need`,
`capability`, `payload`, or `reason` as protocol signals.
