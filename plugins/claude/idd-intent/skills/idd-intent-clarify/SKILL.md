---
name: idd-intent-clarify
description: Resolve ambiguity, disproportionate complexity and missing user-facing behavior in a proposed Product requirement with focused read-only discovery; propose confirmed or unconfirmed clarified intent without file edits or automatic handoff.
user-invocable: false
disable-model-invocation: false
---

# idd-intent-clarify

Resolve ambiguity in a proposed Product requirement. This is a read-only, convergent discovery skill, not a mandatory preliminary stage and not broad creative brainstorming.

## Purpose

Separate the user outcome, actual problem, current workaround, success criterion, proposed solution, technical preference and accidental complexity. The question is: what observable product behavior should change, for whom, and why?

Use for an identified capability with missing behavioral details, a proposed implementation without an established user need, conflicting constraints or potentially excessive complexity. For explicitly alternative product concepts or open-ended exploration use idd-intent-brainstorm; for undecided screenshot traits use idd-intent-visual-brainstorm. Already confirmed changes use idd-intent-change; existing approved behavior to implement uses idd-code-implement. An unclear Engineering-only rule stays with idd-engineering-change.

Words like AI, configurable, plugin, framework and engine are possible complexity clues, not automatic reasons to investigate alternatives or reject a feature.

## Read-only boundary

Never edit files, Product Intent, Engineering Rules, code, ADRs or spikes. Never create implementation plans. Existing implementation is evidence of behavior, not normative Product Intent. Do not treat a model recommendation or silence as user confirmation. Propose a handoff, but do not invoke the next skill automatically; retain original request, independent Engineering/implementation stages and user constraints.

## Minimal relevant context

For an existing product, when available, read .idd/intent/README.md, .idd/intent/INDEX.md and the relevant owning current specification. If necessary to establish existing behavior or avoid redundant questions, inspect narrowly scoped code, tests or documentation. Do not perform a whole-repository scan. Note any unavailable source rather than inventing its contents. Never ask the user about facts already reliably established. If context is sufficient, produce an answer immediately without an interview.

## Customer discovery and evidence

Prefer concrete observations: what happened last time, who is affected, how often, current workaround, practical pain, and what successful behavior looks like. Ask the smallest number of material questions; usually one high-leverage question is better than a mechanical batch of three to five. Batch only when it accelerates a decision, and stop as soon as clarification is sufficient.

Classify evidence when useful, without inventing new routing classifications:
- observed-problem: actual user experience;
- expected-problem: credible near-term need;
- hypothetical-problem: possible future without evidence;
- solution-preference: mechanism proposed but user outcome missing;
- unclear-intent: goal or behavior still materially unknown.

An observed or justified anticipated problem may require sophisticated behavior. A hypothetical concern alone rarely justifies generic infrastructure. Evidence labels are not proof of approved Product Intent.

Ask questions about outcomes, not approval of machinery: what does the user need to do, what happens now, which constraints are non-negotiable, and what would make the new feature unnecessary? Avoid leading questions such as whether to build an AI-enabled framework.

## Adaptive clarification

1. Restate likely product meaning; separate observed facts, proposed mechanisms and assumptions.
2. Gather only missing, relevant project context.
3. Identify blocking decisions and ask focused questions if needed.
4. Challenge unjustified accidental complexity and equally acknowledge justified complexity, scale, accessibility, compliance or extension requirements.
5. Offer Original / Simpler / Minimal only when there are genuinely useful alternatives; this is not a mandatory template and a cheaper inadequate variant is not a solution. Never override an explicitly justified full-feature requirement with an MVP.
6. Present a concrete proposal and its consequences, or prepare Specification-Ready Intent when enough is known.
7. Stop before mutations. Seek explicit product confirmation only where missing, then recommend idd-intent-change; offer idd-intent-brainstorm if the user wants a wider search.

## Adaptive outputs

### Clarification Required
Summarize established understanding and constraints, explain the smallest blocking uncertainty, and ask only necessary questions.

### Clarification Proposal
Offer the product-facing requirement and relevant trade-offs. Mark it as a proposal awaiting user confirmation, never as an approved requirement.

### Specification-Ready Intent
Include as applicable the desired outcome, user/actor, confirmed observable behavior, constraints, first-version boundary, acceptance criteria, exclusions, open questions, confirmation status and recommended owning specification/idd-intent-change handoff. Keep it brief for small requests. A specification-ready *proposal* is not implicitly user-approved. Only previously explicit user approval can be labeled confirmed.

## Examples

- "Build a generic permission engine" without an observed need: ask which actor needs what access and when, not what engine features to build.
- "Use AI to categorize uploads": distinguish retrieval, manual tagging and document classification outcomes before recommending a mechanism.
- "Configurable workflows are mandatory under our approved client contracts": accept the justified complexity and only clarify missing observable behavior.
- "Save the confirmed CSV export requirement": go directly to idd-intent-change.
- "Implement approved IDD-0018": use idd-code-implement, without discovery.
