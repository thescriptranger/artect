using System.Collections.Generic;
using Artect.Config;
using Artect.Templating;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits <c>.gitignore</c>, <c>.editorconfig</c>, and <c>README.md</c> at the scaffold root.
/// Always emitted (PRD FR-46).
/// The gitignore and editorconfig content comes from the corresponding .artect template files;
/// README.md is built programmatically so it can include the project name and instructions.
/// </summary>
public sealed class RepoHygieneEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var cfg     = ctx.Config;
        var project = cfg.ProjectName;
        var apiName = CleanLayout.ApiProjectName(project);

        var gitignore    = ctx.Templates.Load("Gitignore.cs.artect");
        var editorconfig = ctx.Templates.Load("Editorconfig.cs.artect");
        var readme       = BuildReadme(cfg, apiName);

        return new[]
        {
            new EmittedFile(".gitignore",   gitignore),
            new EmittedFile(".editorconfig", editorconfig),
            new EmittedFile("README.md",    readme),
        };
    }

    static string BuildReadme(ArtectConfig cfg, string apiProjectName)
    {
        var testSection = cfg.IncludeTestsProject ? $"""


        ## Tests

        Four xUnit test projects are included — one per Clean Architecture layer:

        | Project | Purpose |
        |---------|---------|
        | `tests/{cfg.ProjectName}.Domain.Tests` | Pure unit tests for domain entities and `Result<T>` logic |
        | `tests/{cfg.ProjectName}.Application.Tests` | Use-case / pipeline-behavior unit tests (mocked repositories) |
        | `tests/{cfg.ProjectName}.Infrastructure.Tests` | Repository integration tests (requires a database) |
        | `tests/{cfg.ProjectName}.Api.Tests` | Endpoint smoke / contract tests |

        Run all tests at once:

        ```bash
        dotnet test
        ```
        """ : string.Empty;

        return $"""
        # {cfg.ProjectName}

        This project was scaffolded by [Artect](https://github.com/art/artect).

        ## Solution structure

        ```
        src/
          {cfg.ProjectName}.Domain/          — Entities, value objects, Result<T>
          {cfg.ProjectName}.Application/     — Use cases, commands/queries, validators, pipeline behaviors
          {cfg.ProjectName}.Infrastructure/  — Repository implementations, EF/Dapper, migrations
          {cfg.ProjectName}.Api/             — Minimal-API endpoints, Scalar docs
          {cfg.ProjectName}.Shared/          — Wire DTOs (requests, responses, error contracts)
        tests/                               — xUnit test projects (one per layer){(cfg.IncludeTestsProject ? "" : " (not generated)")}
        ```

        ## Clean Architecture shape

        - **Domain** — entity behavior lives in `<Entity>.Behavior.cs` hook files alongside the generated entity. Add custom methods there; the scaffold will never overwrite them.
        - **Application** — every use-case implements `IUseCase<TRequest, UseCaseResult<TPayload>>`. Requests flow through a pipeline decorator chain: `ValidationBehavior` → `LoggingBehavior` → `TransactionBehavior` → the interactor.
        - **Infrastructure** — repositories are split by intent when `splitRepositoriesByIntent: true` (`I<Entity>ReadRepository` / `I<Entity>WriteRepository`).
        - **Shared** — wire contracts only; never referenced by Application or Domain.

        ## `artect.yaml` reference

        Key options:

        | Key | Notes |
        |-----|-------|
        | `crud:` | List of `GetList`, `GetById`, `Post`, `Put`, `Patch`, `Delete` |
        | `splitRepositoriesByIntent:` | Separate read/write repository interfaces |
        | `includeTestsProject:` | Emit xUnit test projects |
        | `namingCorrections:` | Map of schema identifiers to corrected Pascal-case names (e.g. `id: ID`) |

        ## How to run

        ```bash
        dotnet run --project src/{apiProjectName}
        ```

        Open the Scalar API reference at `https://localhost:5443/scalar/v1`.

        ## Connection string

        Set `ConnectionStrings:DefaultConnection` in `appsettings.json`, or use user-secrets:

        ```bash
        dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
            "Server=localhost;Database={cfg.ProjectName};Trusted_Connection=True;TrustServerCertificate=True;"
        ```
        {testSection}
        ## Re-generate

        Edit `artect.yaml` and re-run:

        ```bash
        artect generate --config artect.yaml
        ```
        """;
    }
}
