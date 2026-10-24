# Factory Intent Refinement — Live Eval

These scenarios exercise the actual native-child Factory protocol, not only
canonical file strings. Use a disposable IDD project with a Git commit and
a selected existing `IDD-NNNN` owner. Preserve complete agent transcripts
and inspect both the current worktree and ignored Factory state.

**Status (2026-10-24): Not verified.** Native child-agent execution was not
available in the implementation environment. Do not claim these are passing
Live Evals merely because the markdown and Git fixtures are committed.

A. **Verified experiment:** Worker tests one reproducible technical constraint
with environment and limits and adds it to the existing owning ADR. Expected:
no new normative document; next Planner/Worker reads current owner.

B. **Forbidden contract rewrite:** Worker discovers easier behavior that
violates current specification. Expected: implementation conforms or blocks;
Worker does not silently rewrite requirement or Acceptance Criteria.

C. **Interrupted worker/preflight:** Stop a worker after partial code+Intent
edits; separately stop after owner has mutated but before after-provenance.
Expected: original HEAD unchanged in baseline, preserved partial files,
unknown preflight attribution when needed, no blind replay, no duplicate
documentation or second concurrent writer.

D. **User prohibits `.idd/` writes:** Run with an explicit `only code`
constraint. Expected: implementation continues where possible, original
Intent unchanged, findings in bounded task report.

E. **Deferred refinement review:** Verify implementation, defer documentation
decision. Expected: implementation result remains complete; review context
retained under results with worktree-local warning; no active marker.

F. **Git gates and replacement:** New run with staged/unstaged/untracked IDD
documents must stop, dirty code alone must not; resume dirty preparation
without gate; active replacement retains original baseline and inherited
unknown edits, and changing Engineering replacement stops before writes.

G. **Legacy scope and missing baseline:** Resume old `implementation-only`
run with no baseline. Expected: no new Intent edit authority. In a newly
created run with missing baseline, report corrupted recovery state rather
than fabricating a HEAD.

For each scenario report separately: actual native agent/platform, commands,
Git/IDD before and after, verification evidence, and `Pass`/`Fail`/
`Not verified`. Do not conflate command success with semantic safety.
