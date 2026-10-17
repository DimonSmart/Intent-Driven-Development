# Test an unpublished IDD plugin branch locally

IDD stores canonical skills in `src/canonical/` and generates native Claude
and Codex plugin packages into the Git-ignored `artifacts/marketplace/`
directory. A Git branch is **not** itself a packaged plugin marketplace.
These steps work without a PR or a marketplace-branch publication.

## 1. Checkout and verify the branch

Use PowerShell from an IDD source clone:

```powershell
git fetch origin
git switch feature/visual-intent-brainstorm
git pull --ff-only
pwsh ./scripts/Check.ps1 -Mode Fast -Version 0.0.0
```

Requires .NET 10 SDK, PowerShell 7 (`pwsh`) and Git. `Fast` runs local
generator/contract tests and does not consume LLM tokens.

## 2. Generate local marketplace output

```powershell
dotnet run --project tools/generate/Generate.csproj -- --version 0.0.0
```

Expected generated paths:

```text
artifacts/marketplace/
  .claude-plugin/marketplace.json
  .agents/plugins/marketplace.json
  plugins/claude/idd-intent/
  plugins/codex/idd-intent/
```

Regenerate after editing any canonical skill, reference, metadata or manifest.
Generated output is a test artifact: never edit it as the source of truth.

## 3. Test directly with Claude Code (recommended development loop)

Use an absolute path to the generated Claude plugin. The following uses
`$PWD` at the repository root:

```powershell
$plugin = (Resolve-Path './artifacts/marketplace/plugins/claude/idd-intent').Path
claude --plugin-dir "$plugin"
```

Use a new test session after regenerating the plugin. In the session, request
Visual Intent clarification naturally and provide a sample screenshot.
If an installed stable `idd-intent` plugin is also active, disable it for
the test to avoid ambiguous skill selection. This direct loading is temporary
and does not install or publish a marketplace.

Optional syntax validation:

```powershell
claude plugin validate ./artifacts/marketplace/plugins/claude/idd-intent
```

## 4. Test with Codex through a local marketplace

Codex supports local marketplace paths. To avoid a name collision with a
previously installed production `intent-driven-development` marketplace,
make a private copy of the generated marketplace and rename **only the
copy's Codex catalog**:

```powershell
$source = (Resolve-Path './artifacts/marketplace').Path
$target = Join-Path (Resolve-Path './artifacts').Path 'marketplace-local-test'
if (Test-Path $target) { Remove-Item -LiteralPath $target -Recurse -Force }
Copy-Item -LiteralPath $source -Destination $target -Recurse
$catalogPath = Join-Path $target '.agents/plugins/marketplace.json'
$catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
$catalog.name = 'idd-local-test'
$catalog | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $catalogPath -Encoding utf8

codex plugin marketplace add "$target"
codex plugin marketplace list
codex plugin add idd-intent@idd-local-test
```

Check the installed version with `codex plugin list --json`, then start
a **new** Codex session. For changes to canonical sources, regenerate
the source output and refresh/reinstall the local plugin before re-testing;
Codex may cache installed plugins. Temporarily disable the stable
`idd-intent` installation when comparing behavior.

Depending on your Codex version, an explicitly configured local marketplace
may require client restart to refresh installed plugin files. The
`codex plugin marketplace upgrade` command primarily refreshes Git sources;
do not assume it refreshes a copied local plugin automatically.

## 5. Bundled comparison-card library

Copy your approved local library into canonical source so the plugin ships it:

```powershell
$source = 'E:\Private\VisualIntent'
$dest = '.\src\canonical\visual-intent-library'
Copy-Item "$source\VISUAL_INTENT_CATALOG.md" $dest -Force
Copy-Item "$source\Images" $dest -Recurse -Force

# Regenerate and verify packaged assets
dotnet run --project tools/generate/Generate.csproj -- --version 0.0.0
```

The generator packages all canonical library files in both platform plugins
at `skills/idd-intent-visual-brainstorm/assets/visual-intent/`. Keep the real
catalog's ID/filename mapping, and commit only images you are allowed to
redistribute. An optional external library can override packaged cards,
but the installed plugin no longer depends on your `E:` drive.

Example conversation:

> Use Visual Intent discovery with your bundled comparison cards.
> I attached a UI screenshot.
> Help me understand which parts of its visual style I want to preserve.
> Use visual comparison cards when useful, but do not update intent yet.

## 6. CI behavior without a PR

`idd-check.yml` currently runs on pushes to `main` and `pull_request`
events. Pushing only to a feature branch does **not** trigger it.
Run `pwsh ./scripts/Check.ps1 -Mode Fast -Version 0.0.0` locally for
branch validation. Do not create a PR merely to test a local skill change.
