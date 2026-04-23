# Artect — Design

**Date:** 2026-04-22
**Status:** Approved for planning
**Source PRD:** `docs/can-you-write-a-soft-sprout.md`
**ApiSmith reference:** `C:\Users\Art\Nextcloud\Art-Work\Projects\apismith-v1`

---

## 1. Scope

Artect is a .NET global tool that scaffolds a working **Clean Architecture + Minimal API** solution from an existing SQL Server database. It is a greenfield codebase — not a fork of ApiSmith — but inherits ApiSmith's current end-state shapes for introspection, naming, templating, config, and the Clean-Architecture emitter slice. Non-Clean architectures, Controllers, Vertical Slice dispatcher, and Layered/Onion/Flat project graphs are dropped at source, not via conditionals.

The product requirements are in the PRD. This design captures the technical shape, open-question resolutions, and the V1 validation posture.

## 2. Non-goals (V1)

Reaffirmed from PRD §1.3 and §9: architectures other than Clean; endpoint styles other than Minimal API; business logic; background jobs; caching; multi-tenancy; merge/diff regeneration; `IRepository<T>`; validator/mapper interfaces; UoW emitter; user template overrides; non-SQL-Server databases; frontend code; v1 apiVersion schema.

**Additional V1 non-goal:** automated tests against the tool itself. Determinism, matrix build-correctness, and region-wrapper coverage are design intents in V1, verified manually by running the tool. See §13.

## 3. Open-question resolutions

| PRD §10 item | Resolution | Rationale |
|---|---|---|
| License | MIT | Matches ApiSmith's LICENSE file. |
| NuGet publishing cadence | Tag-triggered auto-publish from `main` | The tag is the gate; no manual approval step. |
| Auth0 / AzureAd templates | Emit DI wiring with placeholders that throw at startup if unset | Fail-fast over silent misconfig; matches ApiSmith's current behavior. |
| Scripted-mode flag coverage | Full `--flag` parity for every prompt | Wizard and scripted modes both populate the same `ArtectConfig`; parity is nearly free. |
| Region wrapper placement | Wraps the type declaration only; `using`s and namespace stay outside the region | Keeps the important context visible when the region is folded in the IDE. |

### PRD correction

PRD §4.2 preamble says "Artect's wizard has 12 prompts"; the table that follows lists 15. The 15 are correct. Spec uses **15 prompts**. Prompts #13 (`PartitionStoredProceduresBySchema`) and #14 (`IncludeChildCollectionsInResponses`) are labeled *Advanced* inline in V1; moving them under an Advanced submenu is a V1.1 candidate.

## 4. Tool project graph

Nine projects, one-to-one with ApiSmith's shape minus the four non-Clean layout files. No `tests/` directory in V1.

```
src/
├── Artect.Cli/              # global-tool entry; NewCommand; argv parsing
├── Artect.Core/             # SchemaGraph, Table, Column, FK, Pipeline abstractions
├── Artect.Console/          # prompts, ANSI, WizardRunner
├── Artect.Introspection/    # per-concept SQL readers + SqlServerSchemaReader
├── Artect.Naming/           # casing, pluralization, DbSetNaming (collisions), JoinTableDetector
├── Artect.Templating/       # Tokenizer, TemplateParser, Renderer, Filters
├── Artect.Generation/       # Generator + emitters + CleanLayout + GeneratedByRegionWrapper
├── Artect.Templates/        # embedded .artect template files
└── Artect.Config/           # YamlReader, YamlWriter, ArtectConfig
```

**Reference graph (no cycles):**

- `Cli → Console, Config, Generation, Introspection`
- `Generation → Templates, Templating, Naming, Core`
- `Introspection → Core, Naming`
- `Config → Core`
- `Console → Core, Config`
- `Templates → Templating`

**Tool itself:** C# 12, .NET 9 SDK (confirmed installed: 9.0.313). Uses `Microsoft.Data.SqlClient`. No reflection, no source generators, no MediatR, no AutoMapper — the tool follows the same discipline it generates.

## 5. Schema model and naming

`SchemaGraph` is the root immutable record. Nodes: `Table`, `Column`, `ForeignKey`, `UniqueConstraint`, `Index`, `CheckConstraint`, `Sequence`, `View`, `StoredProcedure`, `Function`. Every collection is `IReadOnlyList<T>` sorted at construction time with `StringComparer.Ordinal` — no post-hoc iteration-order surprises.

`NamedSchemaModel` wraps `SchemaGraph` and adds naming decisions:

- PK type inference including `IDENTITY`, server defaults (`NEWID()`, `NEWSEQUENTIALID()`, sequence nextval), and `IsServerGenerated` flag per column (Appendix A.1 of PRD).
- Navigation-property naming for multi-FK and self-referencing entities, avoiding `CS0542` collisions (Appendix A.2).
- `DbSetNaming` — detects cross-schema collisions and produces schema-prefixed DbSet property names (e.g. `DboUsers`, `AuditUsers`) plus fully-qualified type references in the DbContext (Appendix A.5).
- `JoinTableDetector` — flags two-column all-FK tables as join tables so they don't get full CRUD endpoints.
- Pluralization and casing filters live in `Artect.Naming`.

All Appendix A fixes become properties or methods on this model, not conditionals in emitters. Emitters ask the model, emitters never recompute naming.

## 6. Introspection

Per-concept readers under `Artect.Introspection/Readers/`, one file each:

- `TablesReader` — tables, columns (name, ordinal, SQL type, nullability, identity flag, computed flag, max length, precision, scale, default value), primary keys.
- `ForeignKeysReader` — FK constraints and cascade rules.
- `IndexesReader` — clustered/nonclustered, included columns.
- `UniqueConstraintsReader` — UNIQUE constraints.
- `CheckConstraintsReader` — CHECK constraints and raw expressions.
- `SequencesReader` — sequences cast to `bigint` for value ranges.
- `ViewsReader` — views.
- `FunctionsReader` — scalar/table-valued functions.
- `StoredProceduresReader` — sprocs; result shape inferred via `sys.sp_describe_first_result_set`; indeterminate shapes return a stub result with `ResultInferenceStatus.Indeterminate`.

Orchestrated by `SqlServerSchemaReader`. Uses `INFORMATION_SCHEMA` and `sys.*` directly — no EF Core scaffolding internals. Queries are ordered by `(schema, name, ordinal)` at the SQL level to keep the raw data reader output deterministic before the model layer re-sorts.

## 7. Templating DSL

`.artect` files with Handlebars-inspired syntax, hand-rolled parser, zero external deps.

```
{{# if HasUsingNamespaces }}
{{# for ns in UsingNamespaces }}
using {{ ns }};
{{/for}}
{{/if}}
namespace {{ Namespace }};

public sealed class {{ EntityName }}
{
{{# for col in Columns }}
    public {{ col.ClrTypeWithNullability }} {{ col.PropertyName }} { get; set; }{{ col.Initializer }}
{{/for}}
}
```

Features:

- `{{ var }}`, `{{ var | filter }}`, `{{ var | filter | filter }}` chains.
- `{{# if cond }}…{{ else }}…{{/if}}`.
- `{{# for x in list }}…{{/for}}`. Loop-index/first/last are not features in V1; templates that need them use indexed source collections instead.
- Filters (`Artect.Templating/Filters.cs`): `Humanize`, `ToPascalCase`, `ToCamelCase`, `ToKebabCase`, `ToSnakeCase`, `Pluralize`, `Singularize`, `Indent(n)`.

Pipeline: `Tokenizer` → `TemplateParser` (produces AST) → `Renderer` (evaluates AST against a context object). Templates live as embedded resources in `Artect.Templates`; loaded by logical name like `Entity.cs.artect`, `DbContext.cs.artect`.

## 8. Config (hand-rolled YAML)

`ArtectConfig` properties (16 fields):

```
projectName: MyApi                              # string
outputDirectory: ./MyApi                        # string
targetFramework: net9.0                         # "net8.0" | "net9.0"
dataAccess: EfCore                              # "EfCore" | "Dapper"
emitRepositoriesAndAbstractions: true           # bool
generatedByLabel: "Artect 1.0.0"                # string
generateInitialMigration: false                 # bool
crud: [GetList, GetById, Post, Put, Patch, Delete]  # flags enum set
apiVersioning: None                             # "None" | "UrlSegment" | "Header" | "QueryString"
auth: None                                      # "None" | "JwtBearer" | "Auth0" | "AzureAd" | "ApiKey"
includeTestsProject: true                       # bool
includeDockerAssets: true                       # bool
partitionStoredProceduresBySchema: false        # bool
includeChildCollectionsInResponses: false       # bool
validateForeignKeyReferences: false             # bool
schemas: [dbo]                                  # string[]
```

Changes from ApiSmith's config:

- **Removed:** `EndpointStyle`, `ArchitectureStyle`, `ApiVersion`. Artect is Clean + Minimal + v2-shape only.
- **Added:** `GeneratedByLabel` (string, defaults to `"Artect <version>"`).

`YamlReader` is strict: unknown keys throw. No deprecated-key back-compat path — Artect has no legacy YAML corpus. `YamlWriter` emits keys in a fixed order so `artect.yaml` is diff-stable.

**Connection string:** never written to `artect.yaml` by the tool. Resolved at runtime in order: `--connection` flag → `ARTECT_CONNECTION` env var → `connectionString:` if manually added to YAML.

## 9. Emitters

### 9.1 Inherited from ApiSmith (adapted to Clean-only)

- `EntityEmitter`, `DtoEmitter`, `RequestEmitter`, `ResponseEmitter`, `EnumEmitter`
- `ValidatorEmitter`, `ValidationResultEmitter`, `ValidationErrorEmitter`, `ApiProblemEmitter`, `PagedResponseEmitter`
- `MapperEmitter` (two-hop: Entity↔Dto in Application, Dto↔Request/Response in Shared; `OnMapped` partial hooks)
- `MinimalApiEndpointEmitter`
- `DbContextEmitter` (EF Core path; schema-prefixed DbSets on collision)
- `DapperConnectionFactoryEmitter`
- `DbFunctionsEmitter`, `StoredProceduresEmitter`
- `ProgramCsEmitter`, `AuthEmitter`, `VersioningEmitter`
- `AppSettingsEmitter`, `LaunchSettingsEmitter`
- `CsProjEmitter`, `SlnEmitter`
- `DockerEmitter`
- `MigrationsEmitter`
- `TestsProjectEmitter` — generates a tests project into *scaffold output*. The tool itself has no tests, but the scaffolds it emits still ship a tests project when `includeTestsProject: true`.
- `RepoHygieneEmitter` (`.gitignore`, `.editorconfig`, `README.md`)
- `ArtectConfigEmitter` (writes `artect.yaml` to output root)

### 9.2 New in Artect

- **`RepositoryInterfaceEmitter`** — `I<Entity>Repository` in `src/<Name>.Application/Abstractions/Repositories/`. Data-access-agnostic signature: `ListAsync`, `GetByIdAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync` (and list-only for views/pk-less). No EF Core or Dapper types leak into the interface. PRD §1.4 calls this "in flight" in ApiSmith but the survey confirms it does **not yet exist there** — Artect builds it fresh.
- **`EfRepositoryEmitter`** — `<Entity>Repository` in `src/<Name>.Infrastructure/Repositories/`, EF Core-backed, constructor-injects `<Name>DbContext`. Also new; does not exist in ApiSmith yet.
- **`DapperRepositoryImplEmitter`** — the implementation half of ApiSmith's `DapperRepositoryEmitter`, extracted so both data-access paths share the single `RepositoryInterfaceEmitter`.
- **`GeneratedByRegionWrapper`** — post-processor over every emitted `.cs` file. Inserts `#region Generated by <label>` + `#endregion` around the top-level type declaration (leaving `using`s and namespace outside the region). Skips files matching the hook pattern `*.Extensions.cs` — those receive a short `// User extension point for <ClassName>. Safe to edit.` header instead. Non-`.cs` files are untouched.

### 9.3 Dropped

- `ControllerEmitter`
- `DispatcherEmitter`
- The four non-Clean layout files (`FlatLayout`, `VerticalSliceLayout`, `LayeredLayout`, `OnionLayout`)

### 9.4 `CleanLayout`

ApiSmith's `IArchitectureLayout` interface collapses into a single concrete `CleanLayout` static class. It exposes helper methods like `EntityPath(projectName)`, `DtoPath`, `RepositoryInterfacePath`, `RepositoryImplementationPath`, `EndpointPath`, plus the corresponding `*Namespace` helpers. Because there is no second layout and no intention to add one, the abstraction is inlined.

### 9.5 Generator pipeline

```
Generator.Generate(config, schema, outputRoot)
    │
    ├── run each emitter → List<EmittedFile>
    ├── run GeneratedByRegionWrapper over all *.cs EmittedFiles
    └── write EmittedFiles to disk under outputRoot
```

Emitter execution order is fixed and alphabetical by emitter class name. `EmittedFile` is a record: `{ RelativePath, Contents }`. Writing is the last step and is the only disk-touching code path outside introspection.

## 10. Wizard

`WizardRunner` in `Artect.Console`, 15 prompts in the order shown in PRD §4.2 (with the preamble corrected). Schemas prompt happens after a lightweight introspection probe that enumerates schemas only (no full graph). All other prompts are schema-independent.

The wizard populates `ArtectConfig`. Scripted mode populates the same object from `--flag`s. Both paths converge before introspection + generation.

## 11. CLI

`artect new` accepts:

- `--config <path>` + `--connection <conn>` (replay path)
- `--connection <conn>` alone → wizard
- `--connection <conn>` + any subset of prompt flags → partial-wizard (prompts for what's missing) or scripted (if all required flags present)

Prompt flags, one per wizard question:

`--name`, `--output`, `--framework`, `--data-access`, `--repositories`, `--generated-by`, `--generate-migration`, `--crud`, `--api-versioning`, `--auth`, `--tests`, `--docker`, `--partition-sprocs-by-schema`, `--child-collections`, `--schemas`

**Precedence:** config file > explicit flags > wizard prompts > defaults.

**Connection resolution:** `--connection` flag > `ARTECT_CONNECTION` env var > `connectionString:` in YAML (manual-only).

## 12. Generated solution structure

Exactly as PRD §4.3. The output tree, project reference graph, and per-project package lists are baseline, not per-feature conditionals.

## 13. Validation posture (V1)

Since V1 has no automated tests against the tool:

- **Determinism (PRD NFR-1, NFR-2):** enforced by construction. All sort keys use `StringComparer.Ordinal`. No `DateTime.Now`, no `Guid.NewGuid()`, no reflection-ordered enumerations. `HashSet` / `Dictionary` iteration is always materialized to a sorted list before rendering. The `Replay_is_byte_identical` test is **not** written in V1; the user verifies by running the generator twice and diffing output.
- **Matrix compilability (PRD NFR-3, NFR-6):** verified manually by running selected matrix points against a throwaway schema. Not enforced in CI.
- **Region wrapper coverage (PRD M4):** `GeneratedByRegionWrapper` is a single post-processor applied uniformly; correctness is verified by reading generated output.
- **Clean reference graph (PRD M5):** `CleanLayout` is the sole source of paths/namespaces; `DbContext` reference in endpoints is ruled out by dependency injection through `I<Entity>Repository`.
- **No non-Microsoft runtime packages (PRD M6):** verified by reading generated `.csproj` files.

The PRD's §6 NFRs and §8 success metrics are treated as design obligations in V1. Automated verification (unit, replay, matrix) is a **V1.1 candidate**, called out in §15 below.

## 14. CI (GitHub Actions)

Workflows:

- `.github/workflows/ci.yml` — triggers on PR and `main` push. Runs `dotnet build` on the tool's solution across Ubuntu, Windows, macOS. No `dotnet test` in V1.
- `.github/workflows/release.yml` — triggers on tag push. Runs `dotnet build`, `dotnet pack` on `Artect.Cli`, pushes the resulting `.nupkg` to NuGet.org using `NUGET_API_KEY` secret. Publishes cross-platform (x64 + ARM64) via the default .NET tool packaging.

No nightly matrix in V1 (no matrix test to run).

## 15. V1.1 candidates (post-V1)

- `Artect.UnitTests` — naming, YAML round-trip, config parse, check-constraint translator.
- `Artect.Introspection.Tests` — per-reader fixture tests.
- `Artect.Generation.Tests` — `Replay_is_byte_identical`, `Region_wrapper_present_on_all_generated_cs_files`, sparse matrix with nested `dotnet build`.
- `Artect.EndToEnd.Tests` — live-DB harness.
- Advanced-submenu grouping for wizard prompts #13–#14.
- `.artect/templates/` user override folder.

## 16. V2 candidates (from PRD §11)

Reaffirmed verbatim: PostgreSQL support; re-generation with merge/diff; gRPC and GraphQL endpoints; Auth0 Terraform config; Helm/K8s manifests; additional Clean flavors (strict DDD aggregate roots, CQRS, Feature folders).

---

## Appendix A — Inherited behaviors from ApiSmith (baseline in Artect)

All carried forward as baseline, implemented on the `NamedSchemaModel` and in the emitter set from day zero. Cited against evidence in ApiSmith from the 2026-04-22 survey:

- **PK type inference** — handles `IDENTITY`, `NEWID()`, sequences, `uniqueidentifier` with server default. `IsServerGenerated` is a column-level property; emitters check it to decide whether to emit setters or initializers. Reference: `ApiSmith.Core/NamedSchemaModel.cs:39-44`, `ServerGeneratedPkTests.cs`.
- **Multi-FK and self-referencing entities** — separate navigation properties per FK, `WithMany` lambdas disambiguated, no `CS0542` naming collisions. Reference: `NamedSchemaModel.cs:56-75`, `SelfReferencingRelationshipTests.cs`.
- **Cross-schema name collisions** — `DbSetNaming` emits schema-prefixed DbSet names and fully-qualified types in the DbContext. Reference: `DbSetNaming.cs:13-35`.
- **Paging defaults** — `page=1`, `pageSize=50`; `IQueryable`/SQL query exposed via an extension-point comment. Reference: ApiSmith `LaunchSettingsEmitter.cs`, `ListQueryExtensionPointTests.cs`.
- **Shared contracts project (v2)** — BCL-only, packs as NuGet. Always emitted in Artect. Reference: `ArchitectureLayoutBase.cs:125-128`, `RequestEmitter.cs`, `ResponseEmitter.cs`.
- **Repository toggle semantics** — `emitRepositoriesAndAbstractions` defaults to `true` in Artect, respects YAML override, Clean-specific placement (interface in Application, impl in Infrastructure). Reference: `ApiSmithConfig.cs:67-68`, `YamlReader.cs:9-10`, `CleanLayout.cs:72-76`.
- **Naming convention edge cases** — casing transformations handle acronyms, leading underscores, numeric suffixes, trailing `_id` columns. Reference: ApiSmith `Artect.Naming` equivalents.

## Appendix B — Determinism invariants (load-bearing)

Artect's output must be byte-identical across two runs on the same schema + config + tool version. The invariants that enforce this:

1. Every collection enumerated during emission is sorted with `StringComparer.Ordinal` **at construction time**, not at iteration time.
2. No `DateTime.Now`, `DateTimeOffset.UtcNow`, `Guid.NewGuid()`, `Environment.MachineName`, or any host-derived state in emitted output. The `GeneratedByLabel` is the sole exception and is config-driven, not host-derived.
3. No reflection-ordered enumerations of types, properties, or attributes.
4. `HashSet<T>` and `Dictionary<TK, TV>` are never directly enumerated by emitters; always materialized to `IReadOnlyList<T>` via `OrderBy` first.
5. SQL introspection queries ORDER BY schema, name, ordinal at the SQL level — the data reader output is sorted before it enters the model.
6. File path assembly uses forward-slash-normalized paths at emission time; the final write layer handles platform separators.
