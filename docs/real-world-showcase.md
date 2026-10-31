# Real-world IDD Showcase

Small examples teach the mechanics of a development methodology. They rarely expose the harder problem: keeping requirements, architectural constraints, and verification aligned while a substantial product changes over time. Intent-Driven Development (IDD) aims to retain **current product knowledge** independently of a particular implementation. The following public repositories provide inspectable examples, not claims of automatic correctness or guaranteed reconstruction from Intent.

## CSharpFar — a terminal application with its own window system

**[CSharpFar](https://github.com/DimonSmart/CSharpFar)** is a cross-platform C#/.NET file manager inspired by Far Manager. It is the flagship example because it contains not only application features but also a substantial, independently engineered **terminal window and UI subsystem**. This is a window system *inside a terminal*, not an operating-system window manager.

Unlike applications built around a high-level UI framework, CSharpFar composes its own full-screen surfaces (file panels, editor, viewer, and help) with ordered overlays (menus, completion lists, dialogs, and progress UI). Its infrastructure handles keyboard and mouse routing, modal isolation, focus and cursor ownership, scrollbars, resize, and reusable controls. Rendering is more than writing characters: the [layer-composition contract](https://github.com/DimonSmart/CSharpFar/blob/main/.idd/intent/IDD-0067.spec-ui-layer-composition-and-modal-resize.md) requires the visible frame, geometry, input targets, focus, and cursor state to be committed coherently. Resize or dismissal must not restore stale pixels or route an event to the wrong layer. Fine-grained invalidation supports partial redraws. The [component-library contract](https://github.com/DimonSmart/CSharpFar/blob/main/.idd/intent/IDD-0062.spec-console-ui-component-library.md) addresses independently reusable controls, terminal-cell Unicode geometry, selection, mouse capture, themes, and platform-independent boundaries.

The file manager itself adds interacting subsystems: independent dual panels, command execution and history, a streaming viewer and built-in editor with syntax highlighting, Unicode-aware input, file operations and search, folder/file-set comparison, and interruption-aware copying that can verify and resume a partial destination. FTP/FTPS and SFTP are integrated as first-party providers through CSharpFar's own plugin architecture. Windows, Linux, and macOS hosts must handle different terminal backends, input behavior, process launching, and file-system semantics. See the maintained [feature overview](https://github.com/DimonSmart/CSharpFar/blob/main/docs/features.md) rather than treating this summary as an exhaustive feature list.

### Durable requirements, implementation, and checks

Follow the artifacts rather than an invented agent-execution story:

**Product requirement → durable Intent → implementation → verification.**

For example, the requirement that closing a modal or resizing the terminal preserve the correct screen and event ownership is captured in **[IDD-0067: UI Layer Composition and Modal Resize](https://github.com/DimonSmart/CSharpFar/blob/main/.idd/intent/IDD-0067.spec-ui-layer-composition-and-modal-resize.md)**. The need for portable, consistent controls is captured in **[IDD-0062: Console UI Component Library](https://github.com/DimonSmart/CSharpFar/blob/main/.idd/intent/IDD-0062.spec-console-ui-component-library.md)**. Both documents define acceptance and verification expectations while leaving private class structures and implementation sequences changeable. Their references to engineering and regression material also distinguish durable obligations from non-normative implementation history.

Use the [CSharpFar Intent Index](https://github.com/DimonSmart/CSharpFar/blob/main/.idd/intent/INDEX.md) to navigate the other current specifications and ADRs, including file operations, copy recovery, remote providers, plugins, and terminal input. The source and test trees are available in the same repository for independent examination; a written verification plan should not be mistaken for proof that every environment was tested.

### Inspect the real application

[![Recorded CSharpFar terminal demo](https://raw.githubusercontent.com/DimonSmart/CSharpFar/main/docs/images/csharpfar-demo.gif)](https://github.com/DimonSmart/CSharpFar/blob/main/docs/demo.md)

The recording uses [CSharpFar Demo Mode](https://github.com/DimonSmart/CSharpFar/blob/main/docs/demo.md), which imports a fixture into an **isolated in-memory file system**; runtime edits are discarded and external commands are disabled. This makes the walkthrough reproducible without touching the viewer's real files. Explore [CSharpFar Features](https://github.com/DimonSmart/CSharpFar/blob/main/docs/features.md) for the product's wider scope.

## CSharpGit — consequential Git workflows in a desktop UI

**[CSharpGit](https://github.com/DimonSmart/CSharpGit)** is a cross-platform Git desktop application built with **.NET 10, Uno Platform, and Skia Desktop**. Windows and macOS are primary targets; Linux/X11 is best effort. Its complexity differs from CSharpFar: commit history and graphs, branch and remote operations, working-tree changes, staging, stash, and merge/rebase interactions must stay consistent with real Git semantics.

Some operations rewrite commit history or change references, so preconditions, user confirmation, recovery, and post-operation state refresh matter. The application must reconcile UI selections and background repository changes without executing an operation against stale state. Feature-oriented ViewModels separate history, commit details, commit actions, branch management, and working-tree responsibilities, while desktop rendering and OS integration introduce platform-specific constraints.

The current [CSharpGit Intent Index](https://github.com/DimonSmart/CSharpGit/blob/main/.idd/intent/INDEX.md) links these areas. Representative documents include **[IDD-0007: Git Workflows](https://github.com/DimonSmart/CSharpGit/blob/main/.idd/intent/IDD-0007.spec-git-workflows.md)** (stash, merge, rebase and Git-owned outcomes), **[IDD-0031: Repository History Rewrite](https://github.com/DimonSmart/CSharpGit/blob/main/.idd/intent/IDD-0031.spec-repository-history-rewrite.md)** (destructive path removal, verified backup and refresh), and **[IDD-0043: Presentation Feature Boundaries](https://github.com/DimonSmart/CSharpGit/blob/main/.idd/intent/IDD-0043.adr-presentation-feature-boundaries.md)** (ownership and immutable requests across UI features). **[IDD-0034: Machine-readable Git Output Presentation Boundary](https://github.com/DimonSmart/CSharpGit/blob/main/.idd/intent/IDD-0034.adr-machine-readable-git-output-presentation-boundary.md)** additionally documents why raw Git control characters must be rendered safely without corrupting parseable output.

CSharpGit illustrates IDD in a conventional graphical desktop product, not just in custom terminal software.

## Additional real-world projects

- **[OllamaChat](https://github.com/DimonSmart/OllamaChat)** — AI-agent and multi-agent workflow experimentation with Microsoft Agent Framework. Model-provider selection, MCP tools, retrieval, and workflow execution create nontrivial integration and state boundaries. [Product Intent](https://github.com/DimonSmart/OllamaChat/blob/main/.idd/intent/IDD-0001.spec-agentic-chat.md).
- **[QuizGenerator](https://github.com/DimonSmart/QuizGenerator)** — a .NET educational question-generation *prototype*, with source retrieval, topic planning, typed specialized agents, and review of answer choices. [Product Intent](https://github.com/DimonSmart/QuizGenerator/blob/main/.idd/intent/IDD-0001.spec-product-overview.md).
- **[LocalVectorSearchMcp](https://github.com/DimonSmart/LocalVectorSearchMcp)** — a local Markdown indexing and MCP workbench. SQLite full-text/vector search, semantic document addressing, and guarded edits must preserve document integrity and project isolation. [Product Intent](https://github.com/DimonSmart/LocalVectorSearchMcp/blob/main/.idd/intent/0001.spec-product-overview.md).

## Beyond Toy Examples: Engineering Complexity

Source-file volume alone does not establish correctness or engineering quality. The harder work is at the boundaries: independent subsystems interact, architectural invariants must survive refactoring, user behavior must change without unrelated regressions, and the same product must operate across platforms. In CSharpFar, for example, a dialog or terminal resize touches composition, input routing, cursor state, rendering, and tests simultaneously. The durable requirement is the **observable coherence of the resulting frame**, not a particular rendering class.

The following measurements show scope, not a methodology ranking.

| Project | Application source at snapshot | Test source at snapshot | Numbered Intent documents |
| --- | ---: | ---: | ---: |
| [CSharpFar](https://github.com/DimonSmart/CSharpFar/tree/67e101ac108d5d490d72436a854761754e210c96) | 826 `.cs` files; 3.21 MB | 348 `.cs` files; 2.77 MB | 60 |
| [CSharpGit](https://github.com/DimonSmart/CSharpGit/tree/d837da867fca9e0d887fc07b6e1867e62fedf9aa) | 338 `.cs` + 18 `.xaml` files; 2.44 MB | Not measured here | 45 |

**Measurement method (31 October 2026):** GitHub tree entries at the commit revisions linked in the project names; count regular tracked files under `src/` with the extensions shown, sum their uncompressed blob sizes in decimal MB (1 MB = 1,000,000 bytes). CSharpFar test figures count tracked `tests/**/*.cs`; numbered Intent documents are current `IDD-NNNN.*.md` files directly under `.idd/intent/`. These are **file counts and bytes, not lines of code (LOC)**. Files outside the selected paths or extensions are excluded. Counts may change on `main`.

These projects demonstrate that IDD artifacts can describe complex, evolving product behavior and design boundaries. They do **not** establish that IDD automatically prevents defects, that agents built every feature autonomously, or that any arbitrary product is guaranteed to be rebuilt solely from its Intent.
