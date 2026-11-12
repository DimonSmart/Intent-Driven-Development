# idd-intent-brainstorm

Explore product opportunities and substantially different solution principles. This skill performs read-only divergent research, optional critical comparison and user-led convergence. It is not the default ambiguity or simplification gate.

## When and how to use

Choose Brainstorm when the user explicitly wants creative possibilities, different product strategies, analogies, reframing, new opportunities or an open-ended "what else could we do?" conversation. A single incomplete feature request goes to idd-intent-clarify; extracting unconfirmed visual traits goes to idd-intent-visual-brainstorm. Terms like plugin, configurable, framework, AI or engine alone do not trigger Brainstorm. Direct named-skill selection takes precedence over automatic routing.

Respect initiative: when asked for ideas, produce ideas rather than only questions; when asked for guided co-creation, ask productive questions; when asked to independently research and recommend, use accessible evidence before requesting optional clarification. Never require a mode or preliminary interview.

## Boundaries

- Read-only: no edits to files, Product Intent, Engineering Rules, specs, ADRs, spikes, code or implementation plans. Do not create a permanent brainstorm state or discussion history under .idd.
- Do not assume the first user idea is best, nor that reduction in scope is always good.
- Distinguish confirmed facts, inspected current behavior, hypotheses, unknowns and explicit user decisions. Implementation is not itself Product truth.
- Never invent source citations, code inspection or external research; say when a needed tool or source is unavailable.
- Recommendations, rankings and agent-selected directions are NOT user approval. Do not automatically invoke another skill or turn exploratory hypotheses into normative specs.
- Preserve all user constraints, prior confirmations, rejected directions and independent requested Engineering/implementation stages at handoff.

## Adaptive cycle

Understand -> Investigate -> Reframe -> Diverge -> Challenge -> Converge -> Confirm / Continue is a toolbox, **not seven mandatory sequential steps**. Skip, merge or loop stages as the user needs.

### Understand
Identify the opportunity and actors, the aim of this discussion, agreed constraints and material unknowns. If the user is still exploring their goal, help discover it without forcing a precise goal statement up front.

### Investigate
When useful and permitted, read only relevant Product Intent README/INDEX/owning spec, targeted code/tests/docs and external analogs or approaches. Code can establish *existing behavior* but cannot authorize desired behavior. Bound each investigation; do not scan the entire repository or internet by default. Use web/search only when available. Attribute grounded facts; label unsupported ideas as hypotheses.

### Reframe
Separate means from ends and offer possible alternate problem definitions. A request for more automation could actually concern poor visibility; a proposed platform could be a one-off job. These are candidate framings, not declarations of the user's "true problem".

### Diverge
Generate conceptually distinct approaches, including ones requiring no new feature. Select techniques as useful: First Principles, Assumption Reversal, Analogical Thinking, Reverse Brainstorming, Constraint Removal and Opportunity Exploration. The optional references/brainstorm-method.md explains compact recipes; load it only when it improves exploration.

Three to five meaningfully different approaches is a useful starting point, never a quota or hard cap. Basic/Advanced/Universal versions of the same plugin engine are not diverse concepts. Do not invent a third direction when only two are credible. Support additional creative rounds on request.

### Challenge
For promising alternatives, test core assumptions, conditions of success, failure modes, hidden cost, risks, reversibility and problem coverage. Challenge the agent's own recommendation as well. Identify plausible experiments that would distinguish alternatives. Separate actual constraints and risks from speculation; a formal risk assessment is optional.

### Converge
Only when useful or requested, compare user value, completeness, evidence, cost, implementation complexity, time to benefit, reversibility, risks and confirmed Product Intent. Low cost alone is not the goal. Outcomes can be a selected option, combination, shortlist, rejected initial idea, deferred decision, more investigation or continued ideation. Do not force a winner.

### Confirm / Continue
Ask for explicit approval when it would create a Product decision. If the user asks for more alternatives, keep exploring without demanding a specification. For an approved but still ambiguous Product direction, recommend idd-intent-clarify; for sufficiently specified, user-confirmed Product behavior, recommend idd-intent-change. For technical validation or normative architecture, recommend existing spike / ADR owner workflows, not decisions made here. Handoffs never automatically execute.

## Adaptive depth

Focused: one narrow alternative or a quick reframing. Exploratory: compare multiple genuinely different concepts. Deep: bounded analogy research, multiple techniques and adversarial review. These are not public commands, new routing enums or forced modes. Choose by discussion needs, not simply the word "complex".

Possible architectural patterns may be *candidates* in a Product comparison (for example, fixed roles, RBAC or capabilities), but this skill cannot approve architecture, create ADRs, plan implementation or write code.

## Brainstorm Decision Brief (temporary)

Use compact conversational output when suitable, or a Decision Brief with only useful sections:
- Problem / Opportunity and possible reframings.
- Known Facts, with evidence/source and scope.
- Assumptions and Unknowns.
- Alternative Directions with different principles.
- Trade-offs, Risks, strongest counterarguments and possible experiments.
- Promising Direction and Rejected or Deferred Alternatives, where applicable.
- Decision Status: exploring, recommended but unconfirmed, user-confirmed or deferred.
- Next Step: more exploration, Clarify, Intent Change, or existing spike/ADR owner.

A brief is temporary and is not a new .idd/intent document. Only explicitly confirmed Product decisions can be transferred to Specification-Ready Intent; no intermediate ranking, guess or discussion transcript may be persisted as truth automatically.

## Examples

- "Find unusual ways to work with archives": consider virtual browsing, integration with OS utilities, temporary extraction, external apps, improved workflows and no-change alternatives; do not limit options to different plugin sizes.
- "Give me more ideas, don't choose": continue research without pushing a decision.
- "Use this screenshot to specify typography": use Visual Brainstorm instead.
- "Write the already approved ZIP read-only behavior to Intent": use Intent Change directly.
