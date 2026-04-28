using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Naming;

namespace Artect.Generation.Emitters;

public sealed class InfrastructureDiEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        if (ctx.Config.DataAccess != DataAccessKind.EfCore) return System.Array.Empty<EmittedFile>();

        var project = ctx.Config.ProjectName;
        var dbCtx   = $"{project}DbContext";
        var dataNs  = $"{CleanLayout.InfrastructureNamespace(project)}.Data";
        var infraNs = CleanLayout.InfrastructureNamespace(project);
        var appAbsNs = CleanLayout.ApplicationAbstractionsNamespace(project);
        var sprocNs  = CleanLayout.InfrastructureStoredProceduresNamespace(project);

        // V#7: collect sproc + function interface/impl pairs that need DI registration.
        // Names match what StoredProceduresEmitter / DbFunctionsEmitter generate.
        var sprocPairs = BuildSprocRegistrations(ctx);
        var functionPairs = BuildFunctionRegistrations(ctx);
        var hasSprocOrFunc = sprocPairs.Count > 0 || functionPairs.Count > 0;

        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        sb.AppendLine("using Microsoft.Extensions.Configuration;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine($"using {dataNs};");
        sb.AppendLine($"using {appAbsNs};");
        if (hasSprocOrFunc)
            sb.AppendLine($"using {sprocNs};");

        var repoEntities = ctx.Model.Entities
            .Where(e => !e.ShouldSkip(EntityClassification.AggregateRoot))
            .ToList();
        var readServiceEntities = ctx.Model.Entities
            .Where(e => !e.ShouldSkip(EntityClassification.AggregateRoot, EntityClassification.ReadModel))
            .ToList();
        var diEntities = repoEntities.Union(readServiceEntities)
            .OrderBy(e => e.EntityTypeName, System.StringComparer.Ordinal)
            .ToList();
        foreach (var entity in diEntities)
        {
            sb.AppendLine($"using {CleanLayout.InfrastructureDataEntityNamespace(project, entity.EntityTypeName)};");
            sb.AppendLine($"using {CleanLayout.ApplicationFeatureAbstractionsNamespace(project, entity.EntityTypeName)};");
        }

        sb.AppendLine();
        sb.AppendLine($"namespace {infraNs};");
        sb.AppendLine();
        sb.AppendLine("public static class DependencyInjection");
        sb.AppendLine("{");
        sb.AppendLine("    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)");
        sb.AppendLine("    {");
        sb.AppendLine("        var connectionString = configuration.GetConnectionString(\"DefaultConnection\");");
        sb.AppendLine("        if (string.IsNullOrWhiteSpace(connectionString))");
        sb.AppendLine("            throw new InvalidOperationException(");
        sb.AppendLine("                \"Missing connection string 'DefaultConnection'. Set it in appsettings.json, \" +");
        sb.AppendLine("                \"appsettings.Development.json, or the ConnectionStrings__DefaultConnection environment variable.\");");
        sb.AppendLine();
        sb.AppendLine($"        services.AddDbContext<{dbCtx}>(options => options.UseSqlServer(connectionString));");
        sb.AppendLine("        services.AddScoped<IUnitOfWork, EfUnitOfWork>();");
        sb.AppendLine();

        foreach (var entity in repoEntities.OrderBy(e => e.EntityTypeName, System.StringComparer.Ordinal))
        {
            var name = entity.EntityTypeName;
            sb.AppendLine($"        services.AddScoped<I{name}Repository, {name}Repository>();");
        }

        foreach (var entity in readServiceEntities.OrderBy(e => e.EntityTypeName, System.StringComparer.Ordinal))
        {
            var name = entity.EntityTypeName;
            sb.AppendLine($"        services.AddScoped<I{name}ReadService, {name}ReadService>();");
        }

        if (hasSprocOrFunc)
        {
            sb.AppendLine();
            sb.AppendLine("        // V#7: typed sproc + function wrappers live in Infrastructure. User adapters");
            sb.AppendLine("        // (in Application/Abstractions ports + Infrastructure/Adapters impls) inject");
            sb.AppendLine("        // these to translate business intent into procedure calls.");
            foreach (var (iface, impl) in sprocPairs)
                sb.AppendLine($"        services.AddScoped<{iface}, {impl}>();");
            foreach (var (iface, impl) in functionPairs)
                sb.AppendLine($"        services.AddScoped<{iface}, {impl}>();");
        }

        sb.AppendLine();
        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return new[] { new EmittedFile($"{CleanLayout.InfrastructureDir(project)}/DependencyInjection.cs", sb.ToString()) };
    }

    /// <summary>
    /// Mirrors the names <see cref="StoredProceduresEmitter"/> generates.
    /// Returns (interfaceName, implName) pairs for DI registration.
    /// </summary>
    static List<(string Iface, string Impl)> BuildSprocRegistrations(EmitterContext ctx)
    {
        var pairs = new List<(string, string)>();
        if (ctx.Graph.StoredProcedures.Count == 0) return pairs;

        if (ctx.Config.PartitionStoredProceduresBySchema)
        {
            var schemas = ctx.Graph.StoredProcedures
                .Select(sp => sp.Schema)
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, System.StringComparer.Ordinal);
            foreach (var schema in schemas)
            {
                var schemaPascal = CasingHelper.ToPascalCase(schema, ctx.NamingCorrections);
                pairs.Add(($"I{schemaPascal}StoredProcedures", $"{schemaPascal}StoredProcedures"));
            }
        }
        else
        {
            pairs.Add(("IStoredProcedures", "StoredProcedures"));
        }
        return pairs;
    }

    /// <summary>
    /// Mirrors the names <see cref="DbFunctionsEmitter"/> generates (one pair per schema).
    /// </summary>
    static List<(string Iface, string Impl)> BuildFunctionRegistrations(EmitterContext ctx)
    {
        var pairs = new List<(string, string)>();
        if (ctx.Graph.Functions.Count == 0) return pairs;

        var schemas = ctx.Graph.Functions
            .Select(f => f.Schema)
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, System.StringComparer.Ordinal);
        foreach (var schema in schemas)
        {
            var schemaPascal = CasingHelper.ToPascalCase(schema, ctx.NamingCorrections);
            pairs.Add(($"I{schemaPascal}DbFunctions", $"{schemaPascal}DbFunctions"));
        }
        return pairs;
    }
}
