using System.Collections.Generic;
using Artect.Config;

namespace Artect.Console;

public sealed class WizardRunner
{
    readonly Prompt _prompt;
    readonly IConsoleIO _io;

    public WizardRunner(IConsoleIO io)
    {
        _io = io;
        _prompt = new Prompt(io);
    }

    public ArtectConfig Run(IReadOnlyList<string> availableSchemas)
    {
        _io.WriteLine(Ansi.Colour("Artect wizard", Ansi.Bold));
        _io.WriteLine(Ansi.Colour("──────────────", Ansi.Dim));

        var defaults = ArtectConfig.Defaults();
        var name = _prompt.AskString("1. Project name", defaults.ProjectName);
        var output = _prompt.AskString("2. Output directory", $"./{name}");

        var framework = _prompt.AskEnum("3. Target framework", defaults.TargetFramework, FrameworkLabel);
        var dataAccess = _prompt.AskEnum("4. Data access", defaults.DataAccess, DataAccessLabel);
        var repos = _prompt.AskBool("5. Create repositories and abstractions", defaults.EmitRepositoriesAndAbstractions);
        var label = _prompt.AskString("6. Generated-by label", defaults.GeneratedByLabel);
        var migration = _prompt.AskBool("7. Generate initial migration", defaults.GenerateInitialMigration);

        var crudDefaults = new List<CrudOperation>
            { CrudOperation.GetList, CrudOperation.GetById, CrudOperation.Post, CrudOperation.Put, CrudOperation.Patch, CrudOperation.Delete };
        var crudList = _prompt.AskMultiEnum("8. HTTP operations to generate", crudDefaults, CrudLabel);
        CrudOperation crud = CrudOperation.None;
        foreach (var c in crudList) crud |= c;

        var versioning = _prompt.AskEnum("9. API versioning", defaults.ApiVersioning, VersioningLabel);
        var auth = _prompt.AskEnum("10. Authentication", defaults.Auth, AuthLabel);
        var tests = _prompt.AskBool("11. Include tests project", defaults.IncludeTestsProject);
        var docker = _prompt.AskBool("12. Include Docker assets", defaults.IncludeDockerAssets);
        var partition = _prompt.AskBool("13. Partition stored-procedure interfaces by schema (Advanced)", defaults.PartitionStoredProceduresBySchema);
        var childCollections = _prompt.AskBool("14. Include one-to-many child collections in responses (Advanced)", defaults.IncludeChildCollectionsInResponses);
        var schemas = _prompt.AskMultiString("15. Schemas to include", availableSchemas, new[] { "dbo" });
        var scalar = _prompt.AskBool("16. Enable Scalar UI for browsing the API in development", defaults.EnableScalarUi);

        return defaults with
        {
            ProjectName = name,
            OutputDirectory = output,
            TargetFramework = framework,
            DataAccess = dataAccess,
            EmitRepositoriesAndAbstractions = repos,
            GeneratedByLabel = label,
            GenerateInitialMigration = migration,
            Crud = crud,
            ApiVersioning = versioning,
            Auth = auth,
            IncludeTestsProject = tests,
            IncludeDockerAssets = docker,
            PartitionStoredProceduresBySchema = partition,
            IncludeChildCollectionsInResponses = childCollections,
            Schemas = schemas,
            EnableScalarUi = scalar,
        };
    }

    static string FrameworkLabel(TargetFramework tfm) => tfm switch
    {
        TargetFramework.Net8_0 => "net8.0",
        TargetFramework.Net9_0 => "net9.0",
        TargetFramework.Net10_0 => "net10.0",
        _ => tfm.ToString(),
    };

    static string DataAccessLabel(DataAccessKind d) => d switch
    {
        DataAccessKind.EfCore => "EF Core",
        DataAccessKind.Dapper => "Dapper",
        _ => d.ToString(),
    };

    static string CrudLabel(CrudOperation c) => c switch
    {
        CrudOperation.GetList => "GET (list)",
        CrudOperation.GetById => "GET (by id)",
        CrudOperation.Post => "POST",
        CrudOperation.Put => "PUT",
        CrudOperation.Patch => "PATCH",
        CrudOperation.Delete => "DELETE",
        _ => c.ToString(),
    };

    static string VersioningLabel(ApiVersioningKind v) => v switch
    {
        ApiVersioningKind.None => "None",
        ApiVersioningKind.UrlSegment => "URL segment (/api/v1/...)",
        ApiVersioningKind.Header => "Header (X-Api-Version)",
        ApiVersioningKind.QueryString => "Query string (?api-version=...)",
        _ => v.ToString(),
    };

    static string AuthLabel(AuthKind a) => a switch
    {
        AuthKind.None => "None",
        AuthKind.JwtBearer => "JWT Bearer",
        AuthKind.Auth0 => "Auth0",
        AuthKind.AzureAd => "Azure AD",
        AuthKind.ApiKey => "API Key",
        _ => a.ToString(),
    };
}
