---
name: idd-factory-research
description: Perform one focused read-only research work item in an isolated worker context and return findings for dependent graph work.
context: fork
agent: Explore
argument-hint: "[active research work-item path]"
allowed-tools: [Read, Glob, Grep]
---

# idd-factory-research

## Purpose

Perform one focused read-only `research` work item and return a durable result that another graph node can consume. This worker answers the supplied research contract; it does not coordinate the Factory.

## Inputs and boundaries

Read the supplied immutable work-item contract, relevant durable intent, completed dependency result summaries/references, prior result references for this same item, and focused repository evidence needed to answer the research question.

Do not modify product files, Factory state, graph history, `.idd/factory.yaml`, durable intent, or verification policy. Do not select another role/skill or decide what runtime should execute next.

Keep investigation proportional to the contract. Return findings and evidence rather than speculative implementation changes.

## Dynamic discovery

If research exposes a concrete missing prerequisite that should be performed separately, return `additional-work-required` with a typed `payload.additionalWork`/`payload.requirement` containing `capability`, `goal`, and `reason` plus optional context, constraints, expected output, stable verification IDs, and expectations.

Use `global-replan-required` only when the discovery invalidates the global remaining strategy. A local prerequisite is not a global replan.

## Outcomes

Return protocol version 2 with role `researcher` and one outcome:

- `completed` — include concise findings, evidence/references, unresolved concerns if any;
- `additional-work-required` — runtime should materialize a focused dependency;
- `global-replan-required` — the global remaining strategy must change;
- `intent-required` — durable product meaning is missing;
- `blocked` — an external condition prevents useful research.

Runtime persists the result reference and owns all scheduling and graph mutation.
