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
        var framework = _prompt.AskEnum("3. Target framework", defaults.TargetFramework);
        var dataAccess = _prompt.AskEnum("4. Data access", defaults.DataAccess);
        var repos = _prompt.AskBool("5. Create repositories and abstractions", defaults.EmitRepositoriesAndAbstractions);
        var label = _prompt.AskString("6. Generated-by label", defaults.GeneratedByLabel);
        var migration = _prompt.AskBool("7. Generate initial migration", defaults.GenerateInitialMigration);
        var crudDefaults = new List<CrudOperation>
            { CrudOperation.GetList, CrudOperation.GetById, CrudOperation.Post, CrudOperation.Put, CrudOperation.Patch, CrudOperation.Delete };
        var crudList = _prompt.AskMultiEnum("8. CRUD operations", crudDefaults);
        CrudOperation crud = CrudOperation.None;
        foreach (var c in crudList) crud |= c;
        var versioning = _prompt.AskEnum("9. API versioning", defaults.ApiVersioning);
        var auth = _prompt.AskEnum("10. Authentication", defaults.Auth);
        var tests = _prompt.AskBool("11. Include tests project", defaults.IncludeTestsProject);
        var docker = _prompt.AskBool("12. Include Docker assets", defaults.IncludeDockerAssets);
        var partition = _prompt.AskBool("13. Partition stored-procedure interfaces by schema (Advanced)", defaults.PartitionStoredProceduresBySchema);
        var childCollections = _prompt.AskBool("14. Include one-to-many child collections in responses (Advanced)", defaults.IncludeChildCollectionsInResponses);
        var schemas = _prompt.AskMultiString("15. Schemas to include", availableSchemas, new[] { "dbo" });

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
        };
    }
}
