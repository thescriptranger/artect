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

        // V#15: per-entity using directives are no longer needed — repositories and
        // read services are registered via a marker-driven assembly scan below.

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
            sb.AppendLine("        services.AddScoped<ITenantContext, NoTenantContext>();");
        }
        if (hasInterceptor)
        {
            if (hasAuditingInterceptor)
            {
                sb.AppendLine("        services.AddScoped<AuditingSaveChangesInterceptor>();");
            }
            if (emitDomainEvents)
            {
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

        sb.AppendLine("        var infrastructureAssembly = typeof(DependencyInjection).Assembly;");
        sb.AppendLine("        foreach (var impl in infrastructureAssembly.GetTypes())");
        sb.AppendLine("        {");
        sb.AppendLine("            if (impl.IsAbstract || impl.IsInterface) continue;");
        sb.AppendLine("            foreach (var iface in impl.GetInterfaces())");
        sb.AppendLine("            {");
        sb.AppendLine("                if (iface == typeof(IRepository) || iface == typeof(IReadService)) continue;");
        sb.AppendLine("                if (typeof(IRepository).IsAssignableFrom(iface) || typeof(IReadService).IsAssignableFrom(iface))");
        sb.AppendLine("                    services.AddScoped(iface, impl);");
        sb.AppendLine("            }");
        sb.AppendLine("        }");

        if (hasSprocOrFunc)
        {
            sb.AppendLine();
            foreach (var (iface, impl) in sprocPairs)
                sb.AppendLine($"        services.AddScoped<{iface}, {impl}>();");
            foreach (var (iface, impl) in functionPairs)
                sb.AppendLine($"        services.AddScoped<{iface}, {impl}>();");
        }

        if (emitDomainEvents)
        {
            sb.AppendLine();
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
