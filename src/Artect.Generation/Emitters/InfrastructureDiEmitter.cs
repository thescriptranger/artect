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
        var interceptorsNs = $"{infraNs}.Interceptors";

        // V#7: collect sproc + function interface/impl pairs that need DI registration.
        // Names match what StoredProceduresEmitter / DbFunctionsEmitter generate.
        var sprocPairs = BuildSprocRegistrations(ctx);
        var functionPairs = BuildFunctionRegistrations(ctx);
        var hasSprocOrFunc = sprocPairs.Count > 0 || functionPairs.Count > 0;

        // V#12: when any entity uses Audit / TenantId we must register the SaveChanges
        // interceptor and (for Tenant) bind ITenantContext. NoTenantContext is the
        // shipped placeholder; users override in their own DI extension method.
        var anyAudit = ctx.Model.Entities.Any(e => e.AnyColumnHasFlag(ColumnMetadata.Audit));
        var anyTenant = ctx.Model.Entities.Any(e => e.AnyColumnHasFlag(ColumnMetadata.TenantId));
        var hasAuditingInterceptor = anyAudit || anyTenant;
        var emitDomainEvents = ctx.Config.EnableDomainEvents;
        var hasInterceptor = hasAuditingInterceptor || emitDomainEvents;
        var outboxNs = $"{infraNs}.Outbox";

        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        sb.AppendLine("using Microsoft.Extensions.Configuration;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine($"using {dataNs};");
        sb.AppendLine($"using {appAbsNs};");
        if (hasSprocOrFunc)
            sb.AppendLine($"using {sprocNs};");
        if (hasInterceptor)
            sb.AppendLine($"using {interceptorsNs};");
        if (emitDomainEvents)
        {
            sb.AppendLine($"using {outboxNs};");
            sb.AppendLine("using Microsoft.Extensions.Hosting;");
        }

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
        if (anyTenant)
        {
            sb.AppendLine("        // V#12: ship a placeholder tenant context that returns Guid.Empty so the");
            sb.AppendLine("        // generated solution compiles and runs out of the box. Replace this binding");
            sb.AppendLine("        // (e.g. in Api.AddApi) with an HttpContext-aware impl that reads the tenant");
            sb.AppendLine("        // claim from the authenticated user before going to production.");
            sb.AppendLine("        services.AddScoped<ITenantContext, NoTenantContext>();");
        }
        if (hasInterceptor)
        {
            if (hasAuditingInterceptor)
            {
                sb.AppendLine("        // V#12: SaveChanges interceptor centralizes audit + tenant stamping. Scoped");
                sb.AppendLine("        // because it depends on the request-scoped ITenantContext.");
                sb.AppendLine("        services.AddScoped<AuditingSaveChangesInterceptor>();");
            }
            if (emitDomainEvents)
            {
                sb.AppendLine("        // V#13: outbox interceptor enrolls pending domain events as OutboxMessage rows");
                sb.AppendLine("        // in the same transaction as the aggregate write — at-least-once delivery.");
                sb.AppendLine("        services.AddScoped<DomainEventOutboxInterceptor>();");
            }
            sb.AppendLine($"        services.AddDbContext<{dbCtx}>((sp, options) =>");
            sb.AppendLine("        {");
            sb.AppendLine("            options.UseSqlServer(connectionString);");
            if (hasAuditingInterceptor)
                sb.AppendLine("            options.AddInterceptors(sp.GetRequiredService<AuditingSaveChangesInterceptor>());");
            if (emitDomainEvents)
                sb.AppendLine("            options.AddInterceptors(sp.GetRequiredService<DomainEventOutboxInterceptor>());");
            sb.AppendLine("        });");
        }
        else
        {
            sb.AppendLine($"        services.AddDbContext<{dbCtx}>(options => options.UseSqlServer(connectionString));");
        }
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

        if (emitDomainEvents)
        {
            sb.AppendLine();
            sb.AppendLine("        // V#13: domain-events publishing pipeline. The default publisher logs only —");
            sb.AppendLine("        // override the IDomainEventPublisher binding (Service Bus, Kafka, etc.) before");
            sb.AppendLine("        // production. The dispatcher is a hosted service that drains the outbox table.");
            sb.AppendLine("        services.AddScoped<IDomainEventPublisher, LoggingDomainEventPublisher>();");
            sb.AppendLine("        services.AddHostedService<OutboxDispatcher>();");
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
