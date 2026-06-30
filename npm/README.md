# intent-driven-development

Universal CLI installer for Intent-Driven Development release artifacts.

```bash
npx intent-driven-development list-targets
npx intent-driven-development list-coding-agents
npx intent-driven-development list-packs
npx intent-driven-development init
npx intent-driven-development install --target claude
npx intent-driven-development install --target claude --pack factory
npx intent-driven-development install --coding-agent claude --pack factory
npx intent-driven-development install --target claude --entry minimal
npx intent-driven-development install --target claude --entry none
npx intent-driven-development install --target claude --entry full
npx intent-driven-development install --all
```

`minimal` is the default compact entry point.
`none` installs only generated skills and `.idd/intent/` for CodingAgents that support generated skills.
`full` installs a larger entry point with embedded methodology for legacy/debug scenarios.

For CodingAgents without generated skills, such as `gemini`, `--entry none` is rejected.

`--target` is the CLI compatibility name for selecting a CodingAgent.

Core IDD is installed by default. Core workflows are normal skills. The optional
`factory` pack adds temporary execution orchestration and automatically includes
core:

```bash
npx intent-driven-development install --target claude --pack factory
```

Factory workflows are installed as manual-only commands. They are available to
the user after installing the factory pack, but the CodingAgent must not select
them automatically. Use them through explicit slash-command invocation, for
example `/idd-factory-create-work-plan`.

Factory work artifacts live under `.idd/factory/work/`, are ignored by git by
default, and are not product specifications. They should not be reused
automatically for unrelated tasks. Durable product intent belongs in `.idd/intent/`.
Future external Work Item Provider integration is not implemented yet.

The package is a delivery wrapper. Bundled methodology and generated files are
copied from the versioned GitHub Release content during packaging.
