using System.Collections.Generic;

namespace Artect.Config;

public sealed record ArtectConfig(
    string ProjectName,
    string OutputDirectory,
    TargetFramework TargetFramework,
    DataAccessKind DataAccess,
    bool EmitRepositoriesAndAbstractions,
    bool SplitRepositoriesByIntent,
    string GeneratedByLabel,
    bool GenerateInitialMigration,
    CrudOperation Crud,
    ApiVersioningKind ApiVersioning,
    AuthKind Auth,
    bool IncludeTestsProject,
    bool IncludeDockerAssets,
    bool PartitionStoredProceduresBySchema,
    bool IncludeChildCollectionsInResponses,
    bool ValidateForeignKeyReferences,
    IReadOnlyList<string> Schemas,
    IReadOnlyDictionary<string, string> NamingCorrections)
{
    public static ArtectConfig Defaults() => new(
        ProjectName: "MyApi",
        OutputDirectory: "./MyApi",
        TargetFramework: TargetFramework.Net9_0,
        DataAccess: DataAccessKind.EfCore,
        EmitRepositoriesAndAbstractions: true,
        SplitRepositoriesByIntent: true,
        GeneratedByLabel: "Artect 1.0.0",
        GenerateInitialMigration: false,
        Crud: CrudOperation.All,
        ApiVersioning: ApiVersioningKind.None,
        Auth: AuthKind.None,
        IncludeTestsProject: true,
        IncludeDockerAssets: true,
        PartitionStoredProceduresBySchema: false,
        IncludeChildCollectionsInResponses: false,
        ValidateForeignKeyReferences: false,
        Schemas: new[] { "dbo" },
        NamingCorrections: new Dictionary<string, string>());
}
