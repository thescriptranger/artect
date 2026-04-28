using System.Collections.Generic;

namespace Artect.Config;

public sealed record ArtectConfig(
    string ProjectName,
    string OutputDirectory,
    TargetFramework TargetFramework,
    DataAccessKind DataAccess,
    bool EmitRepositoriesAndAbstractions,
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
    int MaxPageSize,
    bool EnableDomainEvents,
    IReadOnlyList<string> Schemas,
    IReadOnlyDictionary<string, string> NamingCorrections,
    IReadOnlyDictionary<string, EntityClassification> TableClassifications,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, ColumnMetadata>> ColumnMetadata)
{
    public static ArtectConfig Defaults() => new(
        ProjectName: "MyApi",
        OutputDirectory: "./MyApi",
        TargetFramework: TargetFramework.Net9_0,
        DataAccess: DataAccessKind.EfCore,
        EmitRepositoriesAndAbstractions: true,
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
        MaxPageSize: 100,
        EnableDomainEvents: false,
        Schemas: new[] { "dbo" },
        NamingCorrections: new Dictionary<string, string>(),
        TableClassifications: new Dictionary<string, EntityClassification>(),
        ColumnMetadata: new Dictionary<string, IReadOnlyDictionary<string, ColumnMetadata>>());
}
