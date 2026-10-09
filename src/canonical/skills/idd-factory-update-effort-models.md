# IDD Factory Update Effort Models

Update the LLMs assigned to Factory effort levels during the life of a project,
including upgrades, downgrades, and replacement of unavailable models.

Here, effort levels mean the existing `ExecutionProfile` values `economy`,
`standard`, and `strong`. They are distinct from the platform settings
`reasoningEffort` and `effort`. Preserve those settings, including omitted
settings, unless the user explicitly requests a change.

Read `references/factory-execution-policy.md` first. This skill prepares the
model update; `idd-factory-configure`, available in the same Factory plugin,
owns validation and persistence in the target project's `.idd/execution.yaml`.

## Read the request and current policy

Accept natural-language requests to refresh all levels, upgrade or downgrade
selected levels, reduce cost or latency, or assign exact model IDs. Use the
active platform unless the user identifies another supported platform.

Read and retain the current policy as the proposal's baseline, including
whether the file is absent. For an existing file, validate its structure using
the reference. Report malformed policy and hand any requested repair to
`idd-factory-configure`; do not infer missing mappings or replace the strategy
silently.

When the file is absent or globally `inherit`, there are no concrete per-level
models to replace. A move to explicit mappings may override only requested
profiles and platforms. Missing mappings remain inherited; do not copy the
session model into unspecified levels or require model IDs for unselected
profiles. Show inherited levels as `inherit` in the proposal without persisting
that word as a model ID. Resolve missing choices only for requested overrides
through `idd-factory-configure`.

For an explicit mapping, preserve unselected levels and other platform
sections. Keep the three semantic profile names and the planner's classification
rules unchanged. The user may deliberately use the same model at multiple levels.

Respect review-only requests: return the analysis/proposal without applying it.
Do not start Factory, spawn workers, run model benchmarks, schedule recurring
updates, or modify mappings automatically in response to a worker failure.

## Prepare the update

For exact model IDs supplied by the user, apply the requested replacements to
the proposed policy without substituting your own recommendations.

For requests requiring model selection, use the reference's model-information
priority. Prefer current host/platform information; when consulting vendor
documentation, verify that it is current using the available browsing tools.
Never infer quality, price, latency, or account availability from model names
alone. If reliable evidence is unavailable, state what is unknown and request
the concrete IDs or missing decision rather than inventing a recommendation.

Choose according to the user's quality, cost, or latency goal and the level's
required capability. A newly released model is not by itself a reason to
replace a working mapping. Both upgrades and downgrades are legitimate. Avoid
hardcoded recommended model IDs, mandatory distinct models, or a fixed vendor
family ordering. Resolve descriptions such as "cheaper" or "latest" to concrete
IDs during configuration; never save those descriptions as runtime aliases.

Show one table containing each level, current model/settings, proposed
model/settings, and the reason for each change. Identify the target platform,
the source of recommendations, and any unverified account availability. If a
candidate cannot support a preserved reasoning setting, surface the conflict
and resolve it before saving; do not silently drop or change the setting.

If the resulting policy is unchanged, report that outcome and finish without
rewriting the file. Otherwise prepare a complete valid policy, carrying forward
all settings outside the requested changes.

## Apply through the configuration owner

Continue through the installed `idd-factory-configure` in the same user request.
Pass the original request, baseline file contents or absence, complete proposed
policy, reasons/uncertainties, and any exact user authorization or confirmation.
Load and follow that skill's platform-specific configuration guidance. If it is
unavailable, report an incomplete Factory installation and leave the proposal
unapplied; do not implement a second writer.

Automatically selected mappings require explicit confirmation before saving.
An exact, unambiguous user instruction to apply specified replacements already
authorizes those replacements; do not ask for the same decision again. A
review-only request never authorizes persistence. The configuration owner must
reuse an already confirmed proposal rather than repeat the strategy question
or replace it with a new recommendation.

Before applying, the configuration owner re-reads the file and compares it with
the baseline. If it changed, merge only the requested changes onto current
policy and show the revised proposal. Obtain a new decision if the authorized
result would change; do not overwrite intervening edits with the old snapshot.

After validation, atomic save when available, and re-read by the configuration
owner, report the final mapping and when it takes effect. Existing workers
retain their model; the next worker reads the updated policy without requiring
a new plan. Leave `.idd/factory/current/`, Product Intent, and Engineering Rules
untouched. Git remains the history mechanism for the policy.
