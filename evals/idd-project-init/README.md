# `idd-project-init` and bootstrap interaction evals

This suite evaluates the blocking interaction contract for existing implemented
projects without current `IDD-NNNN` intent documents.

Run it manually after changes to:

- `src/canonical/skills/idd-project-init.md`;
- `src/canonical/skills/idd-intent-bootstrap.md`;
- Coding Agent support for structured user input;
- project initialization or bootstrap handoff behavior.

## Evaluation modes

Evaluate relevant cases in both environments when supported:

- `structured-input-available`: `request_user_input` is present in the current
  tool set and should render a client-native selection UI;
- `structured-input-unavailable`: the tool is absent and the skill must fall
  back to one blocking plain-text question.

Use an existing implemented fixture with no current `IDD-NNNN` documents unless a
case says otherwise.

## Observable assertions

For structured input, assert:

- `request_user_input` is actually called rather than described in prose;
- the request contains one short question with two or three meaningful options;
- the recommended option appears first;
- `autoResolutionMs` is absent for blocking decisions;
- the turn waits for the answer;
- no final completion response is emitted while the answer is pending;
- no broad discovery or current intent writing occurs before consent.

For fallback input, assert:

- the response ends with one direct question;
- no textual numbered or bulleted option menu is printed;
- the agent does not describe the question merely as a future next step;
- the workflow does not continue using an assumed answer.

For all modes, assert observable actions rather than exact prose. Do not inspect
wording when the semantic interaction and safety boundary are correct.

## Manual semantic rubric

- **Pass** — the user receives a real blocking decision at every required gate,
  the workflow waits, and no intent is written before explicit approval.
- **Needs review** — interaction occurs but choices are unclear, overly broad, or
  a non-blocking timeout is used.
- **Fail** — the agent only mentions a next step, prints an inert menu, continues
  without an answer, assumes consent, or writes current intent before approval.
