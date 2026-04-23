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

    static string BuildReadme(ArtectConfig cfg, string apiProjectName) => $"""
        # {cfg.ProjectName}

        This project was scaffolded by [Artect](https://github.com/art/artect).

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

        ## Re-generate

        Edit `artect.yaml` and re-run:

        ```bash
        artect generate --config artect.yaml
        ```
        """;
}
