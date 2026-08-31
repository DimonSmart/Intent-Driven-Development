# Factory dynamic task graph

Factory no longer executes a predefined global sequence of phases. The authoritative operational model is the persisted graph of the user's task plus deterministic runtime policy.

## Scheduling model

The .NET runtime inspects `state.json` and deterministically chooses one concrete operation: initial decomposition, exact continuation, work-item verification, scoped refinement of an outline, dispatch of ready work, bounded global replan, strict final verification after ordinary product work quiesces, final-review materialization, final review dispatch, finalization, or a structured stop.

There is no `CurrentWorkflowStep` and no transition table that maps worker outcomes to another global phase. LLM workers never choose what runtime executes next.

## Work definitions

Each task node has an immutable ID and contract provenance. Definitions are:

- `Outline`: known future scope whose exact executable contract depends on earlier results.
- `Executable`: self-contained work with a registered capability.

Capabilities are semantic work types such as `implementation`, `research`, `documentation`, and `semantic-review`. Runtime maps them to canonical roles/skills and execution profiles. A worker cannot inject an arbitrary role or skill.

Work-item lifecycle is operational state, not a second graph: `Planned`, `Ready`, `Dispatching`, `Running`, `Waiting`, `AwaitingVerification`, `Completed`, `Blocked`, `Failed`, `Superseded`, `Cancelled`.

Dependencies are the only prerequisite edges. Sequence is a deterministic tie-break/order hint and need not remain topological after valid runtime mutations.

## Dynamic growth

Initial decomposition may be intentionally partial. Runtime can refine a dependency-ready outline into executable work or a small replacement subgraph.

An executing/reviewing worker can return a typed `additional-work-required` requirement. Runtime validates the requested capability, creates the new node, records the dependency/result provenance, and resumes the source only after the dependency completes.

`global-replan-required` is reserved for a real strategy change affecting the remaining graph. The replanner proposes mutations; it never writes state directly.

## Revisions and history

`Revision` is the CAS state revision and changes on persisted state updates.

`GraphRevision` changes only when graph topology or definitions change. Completed work is immutable. Contract changes create new immutable contract artifacts.

`graph/mutations/*.json` is append-only diagnostic provenance. It is not replayed to reconstruct state; `state.json` is authoritative. Orphan diagnostic/contract artifacts after a crash are safe and do not become current state.

## Verification

Runtime executes authoritative checks. Intermediate work can persist `verificationExpectations` per stable check ID:

- `must-pass` (or omitted): a failure is an unexpected regression;
- `may-fail`: that named intermediate RED is expected.

All failed checks must be explicitly `may-fail` for an intermediate result to be accepted as expected RED. Final verification is always strict.

No successful verification invokes an LLM classifier or hidden verification-fix loop.

## Review and finalization

Semantic reviews are ordinary read-only graph nodes. A defect materializes corrective graph work and preserves the completed review as evidence. Final integrated review is mandatory. Runtime first reaches ordinary product-work quiescence and passes strict final deterministic verification, then materializes and dispatches the final semantic-review node. Materializing that read-only node advances `GraphRevision` but does not require an otherwise identical second full verification.

For successful completion, strict final verification and the approved final review must both refer to the current `GraphRevision`, with no unfinished required work or active continuation. Any later product/corrective graph mutation makes the previous final verification/review stale and requires a fresh strict final verification followed by a fresh final review.

## Recovery

Recovery follows the authoritative state snapshot and exact attempt artifacts. Events and graph history are diagnostics only.

- If authoritative state contains an active attempt but its `invocation.json` is absent, semantic dispatch is known not to have begun. Runtime clears that attempt and safely retries the exact persisted operation.
- If `invocation.json` exists but `result.json` does not, the attempt is treated as interrupted. For workspace-write work, runtime recovers the persisted workspace-change evidence, clears the active attempt, and retries the exact operation.
- If both `invocation.json` and `result.json` exist, runtime validates attempt/result identity and protocol, recovers workspace-change evidence, and reuses the persisted result without duplicate semantic dispatch.

Recovery never replays transcripts, events, or graph history to reconstruct current state.
