# intent-driven-development

Universal CLI installer for Intent-Driven Development release artifacts.

```bash
npx intent-driven-development list-targets
npx intent-driven-development init
npx intent-driven-development install --target claude
npx intent-driven-development install --target claude --entry minimal
npx intent-driven-development install --target claude --entry none
npx intent-driven-development install --target claude --entry full
npx intent-driven-development install --all
```

`minimal` is the default compact entry point.
`none` installs only skills and `.specs` for targets that support generated skills.
`full` installs a larger entry point with embedded methodology for legacy/debug scenarios.

For targets without generated skills, such as `gemini`, `--entry none` is rejected.

The package is a delivery wrapper. Bundled methodology and generated files are
copied from the versioned GitHub Release content during packaging.
