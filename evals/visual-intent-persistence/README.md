# Visual Intent persistence: bounded pilot report

Date: 2026-10-20. Source: `DimonSmart/CSharpGit`, current `main`,
`.idd/intent/IDD-0019.spec-compact-design-system.md`.

## Authorization and method

This is an **isolated, in-memory scenario rehearsal**, not a product decision,
Git mutation or live UI test. The repository `DimonSmart/CSharpGit` was read
only. Temporary changes below are **hypothetical** unless the original
specification had already confirmed the visual aspect. No CSharpGit branch or
tracked file was published; the scenarios must not be mistaken for approved
revisions to CSharpGit Product Intent.

Original owner: `IDD-0019`, which already contains `## Visual Intent`,
`## Behavior`, durable constraints, Acceptance Criteria and Verification.
The test kept the existing owner and all section headings; no new document or
`INDEX.md` change was justified.

## Scenarios and semantic diff

1. **Add (confirmed meaning): passed, in-memory.** Under `## Visual Intent`,
   added the History text-column spacing principle already confirmed in
   Behavior/Acceptance Criteria: small semantic gaps distinguish adjacent Git
   values without always-on vertical dividers. Scope: History; existing row
   density and commit-graph constraints remained intact. This is a deliberate
   within-owner rehearsal, not an additional approved CSharpGit change.
2. **Modify (hypothetical `Essential` to `Preferred` + exception): passed,
   in-memory.** Replaced the general small-corners statement with a
   workspace-scoped *preference* for small corners and an explicit start-screen
   exception. Corresponding Acceptance Criteria stopped treating radius as a
   hard gate; Verification checks preference, exceptions and preservation of
   depth hierarchy. The new exception **has not been user approved** and must
   not be committed to CSharpGit.
3. **Remove (hypothetical): passed, in-memory.** Removed the strict two-tone
   icon obligation from Visual Intent and the associated Behavior, Acceptance
   Criteria and Verification wording. Preserved icon recognizability,
   Fetch/Pull distinction, dynamic labels, accessibility names and monochrome
   dense-row glyphs. **The removal is not an approved product decision.**
4. **Negative/unconfirmed reference: passed, modeled.** No screenshot-only
   palette, unspecified dynamics or comparison-card properties were added.
5. **New product without visual rules: not executed as an agent run.** The
   intended fixture would add confirmed visual requirements to an existing
   owning spec first; new-spec creation is conditional on the usual ownership
   check. No unapproved real project requirements were invented.

In-memory textual comparison (non-diff-based counts of unique lines):
16 added/replaced lines, 19 removed/replaced lines.
All original section headings stayed; unrelated contract markers were
retained. This textual smoke check is **not** proof of the full semantic
correctness of an independent agent's edit.

## Results and limitations

- `passed`: add: pre-approved History column-gap meaning preserved
- `passed`: modify: screen-specific exception and soft Preferred strength
- `passed`: remove: obsolete two-tone obligation and associated checks gone
- `passed`: remove: Fetch/Pull icons and accessible labels retained
- `passed`: preserve: untouched workflow headings
- `passed`: preserve: commit graph
- `passed`: preserve: History
- `passed`: preserve: normal density
- `passed`: preserve: XAML
- `passed`: preserve: keyboard
- `passed`: preserve: bulk
- `passed`: preserve: Focus
- `passed`: preserve: hover
- `passed`: preserve: Original / Changed
- `passed`: preserve: Non-Goals
- `passed`: negative: no screenshot-only style promoted
- `not executed`: Invoke the installed `idd-intent-change` Coding Agent
  end-to-end on a separate CSharpGit worktree; this connector session has no
  local Agent invocation or isolated writable CSharpGit worktree.
- `not executed`: Give the post-change repository **only** to an independent
  Coding Agent and score its ownership, Essential/Preferred interpretation,
  exceptions and implementation freedom (AC-13).
- `not executed`: Real GUI, platform, or design-system verification.
- `not executed`: A true second product fixture with no previous visual
  rules and an independent agent running the internal creation gate.
- `not executed`: local `dotnet test` / PowerShell test suite, because the
  available execution sandbox does not have dotnet or PowerShell.

The pilot confirms constrained **text transformations** and demonstrates
the required linked acceptance/verification edits. It does **not** satisfy
the full agent-execution and fresh-agent interpretability checks. Those
remaining checks must be run in a suitable environment before reporting the
corresponding acceptance criteria as fully verified.
