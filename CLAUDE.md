# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this codebase is

Artect is a .NET 9 global tool (`dotnet artect`) that introspects a SQL Server database and scaffolds a Clean Architecture + Minimal API solution. The CLI itself is the only deliverable; the generated solution is the product. There are no automated tests in the source tree — CI just compiles the solution on three OSes.

The product spec and architectural design docs live under `docs/superpowers/specs/` and `docs/superpowers/plans/`, but `docs/` is gitignored ("Claude Code scratch" per `.gitignore`) — those files are local-only and won't appear on CI or for fresh clones.

## Build, run, package

```bash
dotnet build                                                            # whole solution; warnings = errors
dotnet build -c Release
dotnet run --project src/Artect.Cli -- new --connection "Server=.;Database=MyDb;Trusted_Connection=True;TrustServerCertificate=True"
dotnet run --project src/Artect.Cli -- new --config artect.yaml --connection "..."
dotnet pack src/Artect.Cli/Artect.Cli.csproj -c Release -o ./artifacts  # produces the global tool nupkg
```

`Directory.Build.props` enforces `TreatWarningsAsErrors=true`, `Nullable=enable`, `LangVersion=12.0`, and `Deterministic=true` repo-wide. A new warning anywhere will fail the build. CI (`.github/workflows/ci.yml`) runs `dotnet restore` + `dotnet build -c Release` on ubuntu/windows/macos; tagged `v*.*.*` pushes trigger NuGet publish from `src/Artect.Cli`.

`global.json` pins SDK 9.0.100 with `rollForward: latestMinor`. The only NuGet package referenced anywhere is `Microsoft.Data.SqlClient` (in `Artect.Introspection`); central package management is on (`Directory.Packages.props`).

## Pipeline flow

`Artect.Cli/NewCommand.cs` is the orchestration entry point. The flow is:

1. **Config** — either `YamlReader.ReadFile(path)` (replay) or `WizardRunner.Run(...)` after `SchemaProbe.ListSchemas()` enumerates available schemas. CLI flags layer on top via `ConfigOverrides.Apply`.
2. **Connection** — `ConnectionResolver.Resolve` uses `--connection` → `ARTECT_CONNECTION` env var → `connectionString:` line in `artect.yaml`, in that order. **Connection strings are never written into `artect.yaml` by the tool**; they are picked up at runtime from one of those three sources.
3. **Introspection** — `SqlServerSchemaReader.Read(schemas)` opens one connection and dispatches per-aspect readers in `Artect.Introspection/Readers/` to build a `SchemaGraph` (pure record types in `Artect.Core/Schema/`).
4. **Naming overlay** — `NamedSchemaModel.Build(graph)` produces `NamedEntity`s with C# type names, DbSet property names, and reference/collection navigations (with multi-FK disambiguation and join-table detection from `JoinTableDetector`).
5. **Emission** — `Generator(EmitterRegistry.All())` runs every emitter against an `EmitterContext`, wraps `.cs` outputs with the generated-by region, then writes files sorted by relative path under the output root.

The two-stage **`Artect.Core.Schema` (raw) → `Artect.Naming.NamedSchemaModel` (named)** split is load-bearing: schema records carry no naming opinions; all C# naming, pluralization, and correction logic lives in `Artect.Naming` and is applied once during model build.

## Project graph

```
Artect.Cli (Exe, PackAsTool, ToolCommandName=artect)
 ├── Artect.Console        (wizard, ANSI prompt I/O — no UI library)
 ├── Artect.Config         (ArtectConfig record + enums + hand-rolled YAML reader/writer)
 ├── Artect.Generation     (Generator + 47 emitters in Emitters/, CleanLayout, GeneratedByRegionWrapper)
 │    ├── Artect.Templates    (embedded *.cs.artect resources only; TemplatesMarker is the assembly handle)
 │    ├── Artect.Templating   (Tokenizer → TemplateParser → AST → Renderer + Filters)
 │    ├── Artect.Naming       (NamedSchemaModel, CasingHelper, Pluralizer, JoinTableDetector, EntityNaming)
 │    └── Artect.Config
 └── Artect.Introspection  (SqlServerSchemaReader + Readers/, Microsoft.Data.SqlClient)
      └── Artect.Naming
Artect.Core                 (Schema/*.cs records — no dependencies; the bottom of the graph)
```

## Emitter system

Every output file is produced by an `IEmitter` listed in `Artect.Generation/EmitterRegistry.cs` (47 emitters at last count, one per output concept). Adding output:

- **Always** route file paths and namespaces through `Artect.Generation/CleanLayout.cs`. It is the single source of truth for the generated solution's project layout (`<Root>.Api/.Application/.Domain/.Infrastructure/.Shared/.IntegrationTests`), folder conventions (`Features/<Plural>/`, `Abstractions/`, `Configurations/`, etc.), and namespace strings. Do not hard-code paths in emitters.
- Register the new emitter in `EmitterRegistry.All()`. Generator sorts emitters alphabetically by type name, so order in the registry doesn't matter, but determinism does — avoid nondeterministic enumeration in emitter logic.
- All `.cs` outputs are post-processed by `GeneratedByRegionWrapper`: it wraps the type declaration in `#region Generated by <label>` … `#endregion`. **Files matching `*.Hooks.cs` or `*.Behavior.cs` are exempt** and instead get a `// User extension point. Safe to edit.` header — these are the user-extension hooks.

Emitters use one of two output styles, and both are valid:
- **Template-based** (`Renderer.Render(TemplateParser.Parse(ctx.Templates.Load("Entity.cs.artect")), data)`) — for regular shapes. Templates are embedded resources under `Artect.Templates/Files/` keyed by file name.
- **StringBuilder** (e.g. `HandlerEmitter`) — for deeply conditional or multi-file emissions where templates would proliferate.

## Templating language (in-house, not Mustache/Liquid)

Syntax in `*.cs.artect`:
- `{{ Path.Subpath }}` — variable lookup against the current context (properties, fields, `IDictionary<string, object?>`).
- `{{ value | Filter }}` and `{{ value | Indent(4) }}` — filters from `Artect.Templating/Filters.cs`: `Humanize`, `ToPascalCase`, `ToCamelCase`, `ToKebabCase`, `ToSnakeCase`, `Pluralize`, `Singularize`, `Lower`, `Upper`, `Indent(n)`. Add new ones to the static `Registry` dictionary.
- Control flow: `{{# if cond }} … {{# elseif other }} … {{ else }} … {{/if}}` and `{{# for item in collection }} … {{/for}}`.
- Truthiness: null=false, bool=itself, string=non-empty, IEnumerable=non-empty, anything else=true.

`Artect.Templates/Artect.Templates.csproj` sets `<WithCulture>false</WithCulture>` on the embedded resource glob. **This is load-bearing** — without it, MSBuild parses the `.cs` in `Foo.cs.artect` as a culture code (cs = Czech) and routes the file into a satellite assembly, where `TemplateLoader.GetManifestResourceStream` cannot find it. Don't change this.

## Non-obvious gotchas

- **`Index` type alias.** `Artect.Core.Schema.Index` (the SQL index record) collides with `System.Index` in any file that imports `System`. Files using it need `using Index = Artect.Core.Schema.Index;` (see `SqlServerSchemaReader.cs:5` for the canonical use).
- **No connection string in YAML.** The tool never writes `connectionString:` to `artect.yaml`. Code emitting YAML must preserve this — connection strings come from CLI flag, env var, or a manually-added YAML line.
- **Determinism.** Output must be byte-identical given the same config + schema + tool version. Emitters that enumerate dictionaries, hash sets, or schema collections must sort ordinally. `Generator` already sorts emitters and emitted files; emitter-internal collections are your responsibility.
- **`docs/` is gitignored.** Specs and design notes there are local-only. Don't assume they're available to teammates or CI; don't rely on them for runtime behavior.
- **No tests.** There is no test project in the solution. The only quality gate is `dotnet build` with warnings-as-errors. Smoke testing is done by running the CLI against a live SQL Server (see `out/smoke-config.yaml` for an example replay config) and inspecting `out/`.

## Generated-output shape (locked, V1)

The wizard has no architecture or endpoint-style prompts — Clean Architecture and Minimal API are baked in. Generated solutions always contain `<Root>.Api`, `.Application`, `.Domain`, `.Infrastructure`, `.Shared`, optional `.IntegrationTests`. Domain entities use a `Result<T>` factory pattern (validation errors as `DomainError` records). Application owns use-case interfaces and abstractions; Infrastructure provides the EF Core or Dapper implementation. The full design rationale is in `docs/superpowers/specs/2026-04-22-artect-design.md` (local-only).
