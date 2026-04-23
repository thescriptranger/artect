# Artect — Product Requirements Document

**Version:** 1.0 (V1 scope)
**Status:** Draft
**Date:** 2026-04-22

---

## Context

Artect is a new scaffolding CLI, separate from the existing ApiSmith codebase. ApiSmith ships a configurable matrix of 5 architectures × 2 endpoint styles × 2 data-access strategies, but the vast majority of teams that use it settle into one specific combination — **Clean Architecture + Minimal APIs** — and treat the other options as clutter. Artect narrows that matrix to the single recommended shape and ships it as a focused, opinionated tool.

Locking down those two axes unlocks work the generic tool can't easily do: the generated code can make stronger assumptions (port/adapter separation is mandatory, not optional; every endpoint is a Minimal API lambda, never a controller action), the wizard shrinks, the spec surface shrinks, and everything the parent tool has learned about emitting idiomatic .NET — including all recent fixes (PK detection, multi-FK handling, paging, naming collisions, Shared project, repository abstractions, per-architecture placement) — carries forward as the **baseline**, not as one of many conditional paths.

Artect includes every feature that is appropriate for Clean Architecture + Minimal API. Features that exist in ApiSmith but are only meaningful outside this combination (per-architecture layouts, Controllers, Vertical Slice handlers/dispatcher, Flat folder structure, Layered/Onion project graphs) are dropped.

---

## 1. Overview

### 1.1 Problem

Teams building .NET APIs backed by SQL Server spend days stamping out boilerplate that doesn't change from project to project: Entities, DbContext, DTOs, request/response contracts, validators, mappers, repository abstractions, Minimal API endpoints, tests, Dockerfiles, CI glue. The existing ApiSmith tool solves this, but its 5-architecture × 2-endpoint matrix trades surface area for focus. Teams on Clean Architecture + Minimal API pay for flexibility they never use, and the generated output contains conditional shapes for combinations they'll never pick.

### 1.2 Goals

1. Emit a working Clean Architecture + Minimal API solution from an existing SQL Server database in a single command.
2. Only Microsoft runtime packages in generated code. Scalar for the OpenAPI UI. Dapper if the user opts in. No AutoMapper, no MediatR, no FluentValidation, no source generators.
3. Every generated scaffold compiles with `TreatWarningsAsErrors=true` and `Nullable=enable` on day zero.
4. Deterministic, replayable generation: same config + same schema + same tool version → byte-identical output on any machine.
5. SOLID principles across the generated output: dependency inversion at every persistence seam, interface segregation per entity, single-responsibility per emitter output.
6. Faithful to the .NET industry-standard Clean Architecture interpretation (Jason Taylor template, Ardalis `CleanArchitecture`, Microsoft's "Common web application architectures" guide) — Application owns use-case interfaces, Infrastructure implements them, Domain stays pure.

### 1.3 Non-Goals (V1)

- Any architecture other than Clean Architecture (no Flat, Vertical Slice, Layered, Onion).
- Any endpoint style other than Minimal API (no Controllers, no gRPC, no GraphQL).
- Business logic generation, background jobs, caching, observability beyond default logging, multi-tenancy.
- Re-generation with merge/diff — scaffold-once model.
- v1 apiVersion schema (DTOs in server assembly, no Shared project). Artect launches directly on the v2 shape.
- Databases other than SQL Server 2017+.
- Frontend generation.

### 1.4 Lineage

Artect is a greenfield codebase, not a fork of ApiSmith. It starts from ApiSmith's **current end state** — including all in-flight and completed work as of 2026-04-22:

- Property rename + YAML back-compat for the repositories toggle (ApiSmith Tasks 1–2, complete).
- `RepositoryInterfacePath` / `RepositoryInterfaceNamespace` layout members and the architecture-specific placement for Clean (ApiSmith Task 3, complete).
- Full `RepositoryInterfaceEmitter`, `EfRepositoryEmitter`, and `VerticalSliceStoreEmitter` pipeline (ApiSmith Tasks 5–13, in flight). Artect takes the Clean-Architecture slice of this work only.
- Generated-by region feature (agreed in conversation; Day-1 in Artect).

Artect does not inherit ApiSmith's v1 schema or its cross-architecture conditionals. Every Clean-specific choice is load-bearing.

---

## 2. Target Users

.NET backend developers or tech leads who:

- Work in SQL Server-backed shops (existing line-of-business DBs, not greenfield schemas).
- Already use or want to adopt Clean Architecture + Minimal API as a team standard.
- Want reproducible, diff-friendly scaffolding checked into git, not a one-time template spit-out.
- Prefer hand-rolled, readable generated code over reflection-heavy frameworks.

---

## 3. Distribution & Form Factor

- .NET global tool: `dotnet tool install -g Artect`.
- Requires .NET 8 SDK or later on the host.
- Generates output targeting .NET 8 or .NET 9 (TFM detected from installed SDKs; wizard presents choices; default is the newest GA).
- Cross-platform: Windows 10+, Linux (.NET 8+), macOS 11+, x64 + ARM64.
- Every wizard run writes `artect.yaml` to the output directory. Checked into git; enables `artect new --config artect.yaml --connection "..."` replay on any machine.
- Connection strings resolved in order: `--connection` flag → `ARTECT_CONNECTION` env var → `connectionString:` manually added to `artect.yaml`. Never written to YAML by the tool itself.

---

## 4. Functional Requirements

### 4.1 Database Connection & Introspection

- **FR-1.** The tool accepts a SQL Server connection string via CLI flag, env var, or manual entry. It probes the DB once at wizard-time to enumerate schemas for the schema multi-select prompt, then a second time at generate-time for full introspection.
- **FR-2.** Introspection reads, in a single session: tables, columns (name, ordinal, SQL type, nullability, identity flag, computed flag, max length, precision, scale, default value), primary keys, foreign keys, unique constraints, indexes, check constraints, sequences, views, stored procedures, user-defined functions. Multi-schema support is first-class.
- **FR-3.** Queries use `INFORMATION_SCHEMA` and `sys.*` views. No EF Core reverse-engineering internals.

### 4.2 Developer Choices (Wizard Prompts)

Artect's wizard has **12 prompts** (ApiSmith has 15; architecture and endpoint style are fixed to Clean + Minimal API and therefore absent).

| # | Prompt | Options / Default |
|---|---|---|
| 1 | Project name | `MyApi` |
| 2 | Output directory | `./<ProjectName>` |
| 3 | Target framework | newest GA SDK detected on host |
| 4 | Data access | `EfCore` (default) or `Dapper` |
| 5 | Create Repositories and Abstractions | `true` (default) — emits `I<Entity>Repository` in Application/Abstractions/Repositories/ and concrete in Infrastructure/Repositories/ |
| 6 | Generated-by label | default `"Artect <version>"` — used in the `#region` wrapper on every generated `.cs` file (see §4.8) |
| 7 | Generate initial migration | `false` (default) — bootstrap scripts for `dotnet ef migrations add InitialCreate` (EF Core only) |
| 8 | CRUD operations | multi-select: `GetList`, `GetById`, `Post`, `Put`, `Patch`, `Delete`; default all |
| 9 | API versioning | `None` (default) / `UrlSegment` / `Header` / `QueryString` |
| 10 | Authentication | `None` (default) / `JwtBearer` / `Auth0` / `AzureAd` / `ApiKey` |
| 11 | Include tests project | `true` (default) — xUnit + `WebApplicationFactory` + EF Core InMemory |
| 12 | Include Docker assets | `true` (default) — Dockerfile + compose with SQL Server 2022 |
| 13 | Partition stored-procedure interfaces by schema | `false` (default) |
| 14 | Include one-to-many child collections in responses | `false` (default) — depth-1 expansion in GET payloads when on |
| 15 | Schemas to include | multi-select from discovered schemas |

Prompts 13–14 are advanced toggles. The wizard may group them under an "Advanced" submenu in V1.1; V1 asks them inline.

**FR-4.** Scripted mode: `artect new --name MyApi --connection "..."` bypasses the wizard and accepts every prompt as a flag. A future CLI reference documents the full flag surface.

### 4.3 Generated Solution Structure (Clean Architecture)

Every scaffold emits a **4-project** Clean Architecture solution plus (when apiVersion is v2 — always, in Artect) a Shared contracts project:

```
<ProjectName>/
├── artect.yaml
├── <ProjectName>.sln
├── README.md
├── .gitignore  .editorconfig
├── Dockerfile  docker-compose.yml                         # if Docker on
├── scripts/
│   ├── add-initial-migration.ps1                          # if migrations on
│   └── add-initial-migration.sh
├── src/
│   ├── <ProjectName>.Api/                                 # Minimal API endpoints, Program.cs, DI, middleware
│   │   ├── <ProjectName>.Api.csproj
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   └── Endpoints/
│   │       └── <Plural>Endpoints.cs                       # per entity
│   ├── <ProjectName>.Application/                         # use cases: dtos, validators, mappers, abstractions
│   │   ├── <ProjectName>.Application.csproj
│   │   ├── Abstractions/
│   │   │   └── Repositories/
│   │   │       └── I<Entity>Repository.cs
│   │   ├── Dtos/
│   │   ├── Mappings/
│   │   ├── Validators/
│   │   └── StoredProcedures/                              # IStoredProcedures + IDbFunctions
│   ├── <ProjectName>.Domain/                              # entities only
│   │   ├── <ProjectName>.Domain.csproj
│   │   └── Entities/
│   │       └── <Entity>.cs
│   ├── <ProjectName>.Infrastructure/                      # DbContext, repositories, connection factory
│   │   ├── <ProjectName>.Infrastructure.csproj
│   │   ├── Data/
│   │   │   ├── <ProjectName>DbContext.cs                  # EF Core
│   │   │   └── SqlDbConnectionFactory.cs                  # Dapper
│   │   └── Repositories/
│   │       └── <Entity>Repository.cs
│   └── <ProjectName>.Shared/                              # wire contracts (BCL only, NuGet-ready)
│       ├── <ProjectName>.Shared.csproj
│       ├── Requests/
│       ├── Responses/
│       ├── Enums/
│       └── Errors/
└── tests/
    └── <ProjectName>.IntegrationTests/                     # if tests on
        ├── TestWebApplicationFactory.cs
        ├── Validators/
        └── Endpoints/
```

**Project reference graph:**

```
Api  → Application, Infrastructure, Shared
Application  → Domain, Shared
Domain       → (nothing)
Infrastructure → Domain
Shared       → (nothing)
Tests        → Api
```

- **FR-5.** Domain has zero external package references (BCL only).
- **FR-6.** Shared has zero external package references (BCL only) so it packs cleanly as a NuGet for pure-console clients.
- **FR-7.** Application references Domain and Shared only — never Infrastructure directly, never `Microsoft.AspNetCore.*`.
- **FR-8.** Infrastructure references Domain and adds the chosen persistence package (`Microsoft.EntityFrameworkCore.SqlServer` for EF Core, `Dapper` for Dapper).
- **FR-9.** Api orchestrates Application and Infrastructure via DI; never references Domain directly except transitively.

### 4.4 Generated API Surface (Minimal API)

- **FR-10.** Every entity with a primary key emits a single `<Plural>Endpoints.cs` static class with a `Map<Plural>Endpoints` extension method.
- **FR-11.** The extension method registers, under `/api/{version?}/<plural>`, one minimal-API lambda per enabled CRUD operation:
  - `GET /` → `ListAsync(CancellationToken)` returning `PagedResponse<<Entity>Response>`
  - `GET /{id}` → `GetByIdAsync(id, ct)` returning `<Entity>Response` or 404
  - `POST /` → validates `Create<Entity>Request`, returns `<Entity>Response` with Created
  - `PUT /{id}` → validates `Update<Entity>Request`, returns NoContent or 404
  - `PATCH /{id}` → validates `Update<Entity>Request` (partial), returns NoContent or 404
  - `DELETE /{id}` → returns NoContent or 404
- **FR-12.** Each lambda depends on `I<Entity>Repository` (toggle on) or `<Name>DbContext` / `IDbConnectionFactory` directly (toggle off). Toggle on is the default in V1.
- **FR-13.** `Program.cs` emits `app.Map<Plural>Endpoints()` for each entity, in sorted order.
- **FR-14.** List endpoints paginate by default: `page=1`, `pageSize=50`. Users override via query string. The generated `IQueryable` (EF Core) or SQL query (Dapper) is exposed inside a marked extension-point comment so hand-editing stays safe.
- **FR-15.** Views emit read-only list + GetById endpoints only.

### 4.5 Validation

- **FR-16.** One imperative validator per `Create<Entity>Request` / `Update<Entity>Request`. No DataAnnotations, no FluentValidation, no reflection.
- **FR-17.** Validators return a `ValidationResult` (shared helper in Application, published via Shared). Endpoints check `result.IsValid` and return `400 Bad Request` with an `ApiProblem` body on failure.
- **FR-18.** Rules derived from the schema: `NOT NULL` string → required; `nvarchar(N)` → max-length; `CHECK (col >= N)` / `CHECK (col BETWEEN a AND b)` → range; `CHECK (col IN ('a','b'))` → enum value. Untranslatable CHECKs emit a `// TODO:` comment with the raw SQL.
- **FR-19.** Optional `ValidateForeignKeyReferences` flag emits a `default`-value check on required FK columns, with a `// TODO:` to wire the existence query.

### 4.6 Mapping

- **FR-20.** Hand-written extension methods. No AutoMapper, no Mapster, no reflection.
- **FR-21.** Two mapping hops per entity, consistent with apiVersion v2:
  - Entity ↔ Dto (server-side working model, in Application).
  - Dto ↔ Request/Response (wire contracts, in Shared).
- **FR-22.** Each mapper exposes a `partial void OnMapped(...)` hook so users can extend in a sibling `*.Extensions.cs` file without touching generated code.

### 4.7 Repositories and Abstractions

Default on in Artect V1. The toggle exists primarily so teams migrating from ApiSmith can recreate toggle-off output if they want.

- **FR-23.** For each entity with a primary key, emit `I<Entity>Repository` in `src/<Name>.Application/Abstractions/Repositories/` with methods: `ListAsync`, `GetByIdAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`. Shape is data-access-agnostic (no EF or Dapper symbols).
- **FR-24.** Emit `<Entity>Repository` in `src/<Name>.Infrastructure/Repositories/` implementing the interface. EF Core variant takes the scoped `<Name>DbContext` in the constructor; Dapper variant takes `IDbConnectionFactory`.
- **FR-25.** Endpoints depend only on `I<Entity>Repository` — never `DbContext`, never `IDbConnectionFactory`.
- **FR-26.** DI registration in `Program.cs`: one `services.AddScoped<I<Entity>Repository, <Entity>Repository>()` per entity, emitted in ordinal-sorted order.
- **FR-27.** Pk-less tables (views, join tables) get list-only repositories with only `ListAsync`.

The placement is the .NET industry-standard Clean Architecture interpretation: **Application owns the interface, Infrastructure implements it.** Domain has zero persistence contracts. This matches Jason Taylor's template, Ardalis's, and Microsoft's reference apps.

### 4.8 Generated-By Region

Every generated `.cs` file **except partial-class hook files meant for user extension** is wrapped in a single `#region` block with a customizable label.

- **FR-28.** Wrapper shape:

  ```csharp
  #region Generated by <label>
  // --- generated code goes here ---
  #endregion
  ```

  The region wraps everything below the top-of-file usings and namespace declaration — i.e., the actual type declaration. This keeps usings and namespace visible when the region is folded in the IDE.

- **FR-29.** The label defaults to `"Artect <version>"` (where `<version>` is the tool's assembly version, e.g. `"Artect 1.0.0"`). Customizable via:
  - Wizard prompt (#6 in §4.2).
  - `artect.yaml` key: `generatedByLabel: "Acme Scaffolder 2026-Q2"`.
  - CLI flag: `--generated-by "Acme Scaffolder"`.
- **FR-30.** The label is purely textual. No timestamps, no GUIDs, no build hashes — replay determinism requires the label be stable across runs with identical config.
- **FR-31.** Partial-class hook files (files whose only purpose is user extension, e.g. mapper `OnMapped` siblings) are **not** wrapped. Marking user-owned files "Generated by" is misleading; hook files get a short header comment instead: `// User extension point for <ClassName>. Safe to edit.`
- **FR-32.** Non-`.cs` generated files (csproj, yaml, json, Dockerfile, shell scripts, README.md) do **not** receive regions. Comment syntax varies and the cost of per-format handling outweighs the benefit.

### 4.9 Stored Procedures, Views, Functions

- **FR-33.** Stored procedures: emit `IStoredProcedures` (Application/StoredProcedures/) with typed parameter classes and result classes. Result shape inferred from `sys.sp_describe_first_result_set`. Dynamic SQL / temp tables / conditional branches defeat inference; emit a stub result class with a `// TODO` comment and log a warning in the scaffold log. Dapper implementation fully wired; EF Core implementation returns `Array.Empty<T>()` with a `// TODO` because EF Core sprocs are case-by-case.
- **FR-34.** Optional `PartitionStoredProceduresBySchema` flag: emit `I<Schema>StoredProcedures` per schema instead of one fat interface.
- **FR-35.** Views: read-only entities, `b.ToView().HasNoKey()` in `OnModelCreating`, GET-only endpoints.
- **FR-36.** Functions: `IDbFunctions` interface with typed signatures. Bodies throw `NotImplementedException` — signature's there, implementation is the user's.

### 4.10 Migrations

- **FR-37.** Opt-in via prompt #7. EF Core only (Dapper doesn't use migrations).
- **FR-38.** Emit `scripts/add-initial-migration.ps1` (Windows) and `scripts/add-initial-migration.sh` (Linux/macOS). Both install `dotnet-ef` if missing, run `dotnet ef migrations add InitialCreate`, and generate the idempotent SQL that bootstraps `__EFMigrationsHistory` so the live DB is treated as already-migrated.

### 4.11 Tests Project

- **FR-39.** Opt-in via prompt #11. Packages: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.EntityFrameworkCore.InMemory`.
- **FR-40.** Per-entity validator tests: `Rejects_null_request`, `Accepts_minimally_valid_request`, `Rejects_default_foreign_key` (when `ValidateForeignKeyReferences` on + required FK), `Rejects_value_outside_check_constraint_<Col>` per translated check constraint.
- **FR-41.** Per-entity endpoint smokes (EF Core path only; Dapper needs a real DB): `Get_list_returns_ok`, `Get_by_id_returns_404_when_missing`, `Post_returns_success_with_valid_payload`, `Put_returns_404_when_id_missing`, `Patch_returns_404_when_id_missing`, `Delete_returns_404_when_id_missing`. All use `TestWebApplicationFactory` with InMemory DbContext swapped in.
- **FR-42.** No repository unit tests emitted. Repositories are thin pass-throughs; endpoint smokes cover the integration path.

### 4.12 Docker & Repo Hygiene

- **FR-43.** Opt-in via prompt #12.
- **FR-44.** `Dockerfile` is multi-stage: `mcr.microsoft.com/dotnet/sdk` → `mcr.microsoft.com/dotnet/aspnet`, matching the generated TFM.
- **FR-45.** `docker-compose.yml` brings up the API + SQL Server 2022, wired by env vars, with a DB healthcheck and a persistent volume.
- **FR-46.** Every scaffold emits `.gitignore` and `.editorconfig` even when Docker is off.
- **FR-47.** `TreatWarningsAsErrors=true` and `Nullable=enable` on every generated csproj.

### 4.13 Shared Contracts Project

Always emitted (Artect is v2-shape-only).

- **FR-48.** `<Name>.Shared/Requests/` — `Create<Entity>Request` and `Update<Entity>Request` with DataAnnotations (`[Required]`, `[StringLength]`, `[Range]`) for client-side use.
- **FR-49.** `<Name>.Shared/Responses/` — `<Entity>Response` (no attributes) plus the generic `PagedResponse<T>` envelope.
- **FR-50.** `<Name>.Shared/Enums/` — C# enums derived from `CHECK IN` constraints.
- **FR-51.** `<Name>.Shared/Errors/` — `ValidationError` and `ApiProblem` for typed 400 responses.
- **FR-52.** BCL-only — no `Microsoft.AspNetCore.*` dependency.

---

## 5. Architecture Layout (single, fixed: Clean Architecture)

Artect does not present architecture as a choice. Every scaffold emits the four-project structure in §4.3 above, with the reference graph shown there. The only architectural flexibility is at the persistence boundary (EF Core vs. Dapper) and the repository toggle.

Rationale for this specific Clean interpretation:

- **Domain is pure.** Entities only. No repository interfaces, no persistence concerns. Matches the "Enterprise Business Rules" layer in Uncle Bob's *Clean Architecture*.
- **Application owns use-case ports.** Repository interfaces, DTOs, validators, mappers, stored-procedure interfaces. Matches the "Application Business Rules" layer and every mainstream .NET Clean template (Jason Taylor, Ardalis, Microsoft).
- **Infrastructure implements ports.** DbContext, repository implementations, connection factory. Matches the "Frameworks & Drivers" layer.
- **Api orchestrates.** Minimal API endpoints, DI wiring, middleware, auth. Matches the "Interface Adapters" layer.

---

## 6. Non-Functional Requirements

- **NFR-1. Determinism.** Two runs of `artect new --config artect.yaml --connection "..."` against the same DB and same tool version produce byte-identical output on any machine and any OS. Sort keys use `StringComparer.Ordinal`. Templates embed no timestamps, GUIDs, or reflection-ordered enumerations.
- **NFR-2. Replay gate.** A `Replay_is_byte_identical` test in the tool's own test suite runs the generator twice against an in-memory schema and compares output byte-for-byte. This test is load-bearing and must stay green for every release.
- **NFR-3. Generated code compiles clean.** Every matrix point builds green under `TreatWarningsAsErrors=true` + `Nullable=enable`.
- **NFR-4. No non-Microsoft runtime packages** in generated code, except:
  - Dapper, when the user opts in at prompt #4.
  - Scalar.AspNetCore, for the OpenAPI UI at `/scalar/v1` (Microsoft doesn't ship an equivalent).
- **NFR-5. SOLID-compliant generated output.** Dependency inversion at every persistence seam (endpoints depend on `I<Entity>Repository`, not `DbContext`); interface segregation per entity (no generic `IRepository<T>`); single responsibility per file.
- **NFR-6. Matrix test.** Every combination of data-access × auth × versioning × CRUD × tests × Docker × toggles emits a solution and runs `dotnet build` on it in CI. Failure is a release blocker.
- **NFR-7. Cross-platform.** Tool runs on Windows 10+, Linux (.NET 8+), macOS 11+, x64 + ARM64. Generated output runs anywhere .NET does.
- **NFR-8. Performance.** Scaffold a 40-table schema in under 10 seconds on a mid-range developer machine (excluding the nested `dotnet build` the matrix test runs).

---

## 7. Tech Stack (the tool itself)

- C# 12, .NET 9 SDK.
- Microsoft.Data.SqlClient for introspection (raw `INFORMATION_SCHEMA` and `sys.*` queries — no EF Core scaffolding internals).
- xUnit for the tool's own tests.
- Embedded `.artect` template files in a dedicated Templates project.
- Hand-rolled YAML reader/writer — accepts only the subset the tool emits, rejects unknown indentation. Deprecated keys parse with a warning logged to the scaffold log.
- No reflection, no source generators, no MediatR, no AutoMapper. The tool's own code follows the same discipline it generates.

Project layout of the tool:

```
src/
├── Artect.Cli/              # entry point, packs as global tool
├── Artect.Core/             # schema model + pipeline abstractions
├── Artect.Console/          # hand-rolled prompts
├── Artect.Introspection/    # per-concept SQL readers
├── Artect.Naming/           # casing, pluralization, schema segments
├── Artect.Templating/       # .artect mini-DSL
├── Artect.Generation/       # generator + emitters
├── Artect.Templates/        # embedded .artect files
└── Artect.Config/           # artect.yaml reader/writer

tests/
├── Artect.UnitTests/
├── Artect.Introspection.Tests/
├── Artect.Generation.Tests/
└── Artect.EndToEnd.Tests/   # reserved for live-DB runs
```

---

## 8. Success Metrics

- **M1.** `artect new` against a schema of ≥20 tables produces a scaffold that builds and `dotnet run`s without manual edits.
- **M2.** `artect new --config artect.yaml --connection "..."` regenerates byte-identical output on any machine.
- **M3.** Zero warnings, zero errors on generated output under strict mode, across every feature combination in the matrix test.
- **M4.** Every generated .cs file (except user-extension hooks) has the `#region Generated by <label>` wrapper, defaulting to `"Artect <version>"` unless overridden.
- **M5.** Every endpoint/handler depends on `I<Entity>Repository` by default — no `DbContext` references in `<Name>.Api/Endpoints/`.
- **M6.** No non-Microsoft packages in generated code outside the explicit exemptions.

---

## 9. Out of Scope (V1)

- Database providers other than SQL Server (PostgreSQL/MySQL/SQLite are V2 candidates).
- gRPC, GraphQL, WebSocket endpoints.
- Business logic generation.
- Background jobs, queues, caching, observability beyond default logging.
- Multi-tenancy.
- Re-generation with merge/diff — Artect is scaffold-once in V1.
- Frontend code generation.
- Unit-of-Work abstraction as a standalone emitter (EF Core's `DbContext` already is one; Dapper transaction scope is out of scope for V1).
- Generic `IRepository<T>` — per-entity interfaces preserve ISP.
- Validator / mapper interfaces — only repository ports are abstracted in V1.

---

## 10. Open Questions

- **License.** MIT or Apache 2.0 — decision pending, same as ApiSmith.
- **NuGet publishing cadence.** After a green matrix test on main, or manual gate?
- **Auth0 / AzureAd templates** — ship with issuer/audience placeholders that throw at startup if unset, or require the user to fill them in before first run? Today ApiSmith emits the wiring with placeholders.
- **Scripted-mode coverage.** Should every wizard prompt have a corresponding `--flag`, or only the common ones? V1 covers the common ones; full parity is a V2 candidate.
- **Region wrapper placement** — wrap the type declaration only, or also top-of-file `using`s and the namespace line? V1 wraps below usings and namespace to keep those visible when the region is folded. Open to pushback.

---

## 11. V2 Candidates (not committed)

- PostgreSQL support.
- Re-generation with merge/diff (currently scaffold-once).
- gRPC and GraphQL endpoints.
- Auth0 Terraform config alongside the .NET code.
- Helm / K8s manifests.
- Full scripted-mode parity (every wizard prompt gets a `--flag`).
- User-defined template overrides via a `.artect/templates/` override folder in the output directory.
- Additional Clean Architecture flavors — strict DDD aggregate roots, CQRS split, Feature folders under Application — as opt-in toggles.

---

## Appendix A — Inherited lessons from ApiSmith (2026-04-22 snapshot)

Artect carries these fixes forward as baseline behaviors, not as bug-fix commits:

- **Primary-key type inference.** Correctly handles identity `int`, `bigint`, `uniqueidentifier` PKs including server-generated defaults (`NEWID()`).
- **Multi-FK entities.** Correctly emits separate navigation properties and `WithMany` lambdas when an entity has two or more FKs to the same target.
- **Self-referencing entities.** Handles both single-FK (parent/child) and multi-FK (two columns, both self-referencing) shapes without CS0542 naming collisions.
- **Naming convention edge cases.** Casing transformations handle acronyms, leading underscores, numeric suffixes, and trailing `_id` columns.
- **Cross-schema name collisions.** Same-named entities across schemas get schema-prefixed DbSet property names and fully-qualified types in the DbContext; endpoints and tests route through a single naming helper so disambiguation stays consistent.
- **Paging defaults.** List endpoints default to `page=1, pageSize=50` with an extension-point comment for `Where/OrderBy/Skip/Take`.
- **Shared contracts project.** `<Name>.Shared` is BCL-only and packs cleanly as a NuGet for console clients.
- **Hand-rolled dispatcher.** (Inherited context only — Artect does not emit a dispatcher since Vertical Slice is out of scope.)
- **Repository toggle default.** New scaffolds default to `emitRepositoriesAndAbstractions: true`; the toggle is always respected; YAMLs without the key parse as `false` (for replay stability on any imported ApiSmith YAML).

---

## Appendix B — Sample generated `Program.cs` (Clean + Minimal API + EF Core, toggle on)

```csharp
using Microsoft.EntityFrameworkCore;
using MyApi.Application.Abstractions.Repositories;
using MyApi.Application.Validators;
using MyApi.Infrastructure.Data;
using MyApi.Infrastructure.Repositories;
using MyApi.Api.Endpoints;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddDbContext<MyApiDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Missing connection string 'DefaultConnection' in configuration.");
    options.UseSqlServer(connectionString);
});

builder.Services.AddScoped<IPostRepository, PostRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();

builder.Services.AddScoped<IValidator<CreatePostRequest>, CreatePostRequestValidator>();
builder.Services.AddScoped<IValidator<UpdatePostRequest>, UpdatePostRequestValidator>();
// ... additional validators

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.MapPostsEndpoints();
app.MapUsersEndpoints();

app.Run();

public partial class Program { }
```

The entire body of `Program.cs` above the final `public partial class Program { }` declaration lives inside a single `#region Generated by Artect 1.0.0 ... #endregion` block per §4.8.

---

## Verification (how the tool is validated end-to-end)

1. **Unit tests (`Artect.UnitTests/`).** Naming, YAML round-trip, check-constraint translator, schema model, config parse.
2. **Introspection tests (`Artect.Introspection.Tests/`).** Row-shaping and edge cases against fixture schemas (no live DB).
3. **Generation tests (`Artect.Generation.Tests/`).**
   - Path-level tests confirming every emitted file lands at the correct Clean Architecture path (Application/Abstractions/Repositories/, Infrastructure/Repositories/, Domain/Entities/, etc.).
   - `Replay_is_byte_identical` test — generator runs twice, output compared byte-for-byte.
   - Matrix test — for every combination of (EfCore|Dapper) × (None|JwtBearer|Auth0|AzureAd|ApiKey) × (None|UrlSegment|Header|QueryString) × (tests on/off) × (docker on/off) × (repositories on/off), emit a scaffold and run `dotnet build` inside it. Set `ARTECT_SKIP_NESTED_BUILD=1` for the fast dev loop; CI runs without.
   - Region wrapper test — every emitted .cs file except partial hooks contains `#region Generated by` and a matching `#endregion`.
4. **End-to-end tests (`Artect.EndToEnd.Tests/`).** Reserved for live-DB runs against a Dockerized SQL Server 2022.
5. **Manual smoke.** `artect new` wizard, point at a throwaway DB, `cd <ProjectName>`, `dotnet run --project src/<ProjectName>.Api`, hit `http://localhost:5000/scalar/v1`.
