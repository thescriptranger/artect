using System;
using System.Linq;
using Artect.Config;

namespace Artect.Cli;

public static class ConfigOverrides
{
    public static ArtectConfig Apply(CliArguments args, ArtectConfig baseline) => baseline with
    {
        ProjectName = args.Get("name") ?? baseline.ProjectName,
        OutputDirectory = args.Get("output") ?? baseline.OutputDirectory,
        TargetFramework = args.Get("framework") is { } f ? TargetFrameworkExtensions.FromMoniker(f) : baseline.TargetFramework,
        DataAccess = args.Get("data-access") is { } d ? Enum.Parse<DataAccessKind>(d, ignoreCase: true) : baseline.DataAccess,
        EmitRepositoriesAndAbstractions = ParseBool(args.Get("repositories"), baseline.EmitRepositoriesAndAbstractions),
        EmitUseCaseInteractors = ParseBool(args.Get("use-case-interactors"), baseline.EmitUseCaseInteractors),
        GeneratedByLabel = args.Get("generated-by") ?? baseline.GeneratedByLabel,
        GenerateInitialMigration = ParseBool(args.Get("generate-migration"), baseline.GenerateInitialMigration),
        Crud = args.Get("crud") is { } c ? ParseCrud(c) : baseline.Crud,
        ApiVersioning = args.Get("api-versioning") is { } v ? Enum.Parse<ApiVersioningKind>(v, ignoreCase: true) : baseline.ApiVersioning,
        Auth = args.Get("auth") is { } a ? Enum.Parse<AuthKind>(a, ignoreCase: true) : baseline.Auth,
        IncludeTestsProject = ParseBool(args.Get("tests"), baseline.IncludeTestsProject),
        IncludeDockerAssets = ParseBool(args.Get("docker"), baseline.IncludeDockerAssets),
        PartitionStoredProceduresBySchema = ParseBool(args.Get("partition-sprocs-by-schema"), baseline.PartitionStoredProceduresBySchema),
        IncludeChildCollectionsInResponses = ParseBool(args.Get("child-collections"), baseline.IncludeChildCollectionsInResponses),
        Schemas = args.Get("schemas") is { } s ? s.Split(',').Select(x => x.Trim()).ToList() : baseline.Schemas,
    };

    static bool ParseBool(string? s, bool fallback) =>
        s is null ? fallback : s.ToLowerInvariant() is "true" or "1" or "yes";

    static CrudOperation ParseCrud(string s)
    {
        var result = CrudOperation.None;
        foreach (var tok in s.Split(','))
        {
            if (Enum.TryParse<CrudOperation>(tok.Trim(), ignoreCase: true, out var v)) result |= v;
        }
        return result;
    }
}
