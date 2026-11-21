# AgentTrace

AgentTrace is a read-only .NET 10 CLI for reconstructing Codex agent executions from local JSONL rollouts and the optional Codex SQLite state database. It reports agent hierarchy, event chronology, tool activity, token accounting and evidence gaps. It also recognizes IDD Factory runs and evaluates their Factory-specific completion evidence.

This directory is a self-contained source project. Neither building nor testing AgentTrace requires the parent Intent-Driven-Development repository, its generated plugins, or its scripts.

## Build and test

Install the .NET 10 SDK. From this directory:

```sh
dotnet build AgentTrace.slnx
dotnet test AgentTrace.slnx
```

NuGet restores the standard dependencies (Microsoft.Data.Sqlite, YamlDotNet, xUnit and Microsoft.NET.Test.Sdk). Test fixtures live in `tests/Idd.Factory.Report.Tests/Fixtures/`.

## Use

From this directory, use the existing executable name `idd-factory-report`:

```sh
dotnet run --project src/Idd.Factory.Report -- latest --repo /path/to/project
dotnet run --project src/Idd.Factory.Report -- list --repo /path/to/project
dotnet run --project src/Idd.Factory.Report -- <thread-id> --repo /path/to/project
dotnet run --project src/Idd.Factory.Report -- <thread-id> --run 2 --repo /path/to/project
dotnet run --project src/Idd.Factory.Report -- latest --repo /path/to/project --codex-home /path/to/.codex --json report.json --markdown report.md --verbose
```

`--repo` identifies the project being analyzed; it need not be IDD. `--codex-home` overrides the local Codex data location. The CLI preserves `latest`, `list`, explicit thread selection, `--run`, `--json`, `--markdown` and `--verbose`. By default it prints a console report; JSON and Markdown files are optional. See [report semantics and limitations](docs/factory-report.md).

## Architecture

- `src/Idd.Factory.Report/Codex/` contains the general Codex JSONL reader, event normalization, SQLite state reader and Codex trace models. These classes do not depend on IDD Factory semantics.
- `src/Idd.Factory.Report/Factory/` interprets Factory-specific roles, batches, checkpoints and completion/verification evidence using the generic Codex data.
- `src/Idd.Factory.Report/Cli/` contains argument handling and console, JSON and Markdown presentation.
- `tests/Idd.Factory.Report.Tests/` exercises parser compatibility, nested agents, parallel work, multiple runs, token accounting, incomplete/conflicting evidence and Factory semantics.

For now the layers remain in one assembly and retain existing public names and output schemas. The dependency direction is Codex trace data → Factory interpretation → reports/CLI; there is no extension framework or additional platform adapter.

## Local evidence and privacy

AgentTrace reads local Codex JSONL/SQLite traces, which can include user prompts, working directories, tool arguments, tool output and other sensitive content. Run it with appropriate filesystem permissions; restrict access to the original traces and generated reports. Analysis is read-only: the tool does not modify the input traces, agent sessions or the analyzed repository. Missing or ambiguous evidence is reported instead of guessed.

The optional `.idd/` directory of an analyzed project is **input data**, not a build-time dependency on the IDD source tree. Without Factory evidence, general Codex trace interpretation still applies where supported.

## Current IDD integrations

While this directory lives in the IDD monorepo, IDD intentionally depends *on* AgentTrace:

- `../Intent-Driven-Development.slnx` includes the AgentTrace projects.
- `../tests/Idd.Factory.LiveTests/Idd.Factory.LiveTests.csproj` references `agent-trace/src/Idd.Factory.Report/` directly for diagnostic assertions.
- `../scripts/Check.ps1` runs its tests and invokes the CLI after Factory live tests.
- `../.github/workflows/idd-check.yml` verifies the module in isolation by copying only this directory outside the repository.

These are parent-repository integrations, not AgentTrace dependencies. The marketplace plugin generator/publisher does not package this directory.

## Moving to an independent repository

Move this entire `agent-trace/` directory unchanged to the new repository. In IDD, replace or remove the root solution entries, LiveTests `ProjectReference`, `Check.ps1` tool invocation and CI integration (typically replacing the project reference with a published dependency). No internal project reference or source-path rewriting is needed within AgentTrace. Packaging and publishing as a .NET Tool remain future work.
