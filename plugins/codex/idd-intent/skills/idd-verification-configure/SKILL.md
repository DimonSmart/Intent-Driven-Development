---
name: idd-verification-configure
description: Create or deliberately update project-specific `.idd/verification.yaml` rules without changing product intent.
---

# idd-verification-configure

Create or deliberately update the project-owned `.idd/verification.yaml` policy. This skill never changes `.idd/intent/` or product intent.

## Required Reference

Read `references/project-verification.md` before proposing a policy. Treat it as the full normative policy reference.

## Workflow

1. If an existing YAML policy exists, do not overwrite it: offer review or explicit reconfiguration, preserve comments and semantically unchanged IDs, and show any removals or renames.
2. Perform cheap discovery only: solution/workspace and manifests, test projects, root scripts, package scripts, Make/task-runner targets, CI, formatter/analyzer configuration, and obvious Docker/test infrastructure. Do not run tests, external commands, destructive commands, commands using secrets, or broadly read source code.
3. Prefer a repository-owned aggregate command such as `pwsh ./scripts/Check.ps1` over classifying its internal steps.
4. Show the discovered commands and a compact minimal proposal. Ask only questions discovery cannot answer, such as Docker/external safety, costly-suite consent, or required user UI scenarios.
5. After explicit confirmation, write the complete YAML document to `.idd/verification.yaml`. For a simple project use one check plus `default`; add context sections only when different scope or cost requires them.

Never run a discovered command merely to estimate duration. Use `confirmation: required` for an agent-runnable costly command that needs user consent, and `instructions` for a check only the user or external environment can perform.
