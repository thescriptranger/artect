using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits per-layer DI installer extension classes:
/// <list type="bullet">
///   <item><c>src/&lt;Project&gt;.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs</c></item>
///   <item><c>src/&lt;Project&gt;.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs</c></item>
/// </list>
/// Namespace is <c>Microsoft.Extensions.DependencyInjection</c> so the extension
/// methods surface automatically via implicit usings in Program.cs.
/// </summary>
public sealed class ServiceInstallerEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var project = ctx.Config.ProjectName;
        return new[]
        {
            new EmittedFile(CleanLayout.ApplicationInstallerPath(project), BuildApplicationInstaller(ctx, project)),
            new EmittedFile(CleanLayout.InfrastructureInstallerPath(project), BuildInfrastructureInstaller(ctx, project)),
        };
    }

    // ── Application installer ─────────────────────────────────────────────────

    static string BuildApplicationInstaller(EmitterContext ctx, string project)
    {
        var cfg          = ctx.Config;
        var model        = ctx.Model;
        var crud         = cfg.Crud;
        var anyWrite     = (crud & (CrudOperation.Post | CrudOperation.Put | CrudOperation.Patch)) != 0;
        var useInteractors = cfg.EmitUseCaseInteractors;

        var ucAbsNs    = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.UseCases";
        var ucImplNs   = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";
        var appValidNs = $"{CleanLayout.ApplicationNamespace(project)}.Validators";
        var sharedReqNs = $"{CleanLayout.SharedNamespace(project)}.Requests";

        // Entities eligible for validators (write-enabled, have PK, not join table)
        var validatedEntities = anyWrite
            ? model.Entities
                .Where(e => !e.IsJoinTable && e.HasPrimaryKey)
                .OrderBy(e => e.EntityTypeName, System.StringComparer.Ordinal)
                .ToList()
            : new List<NamedEntity>();

        var usings = new SortedSet<string>(System.StringComparer.Ordinal);

        if (validatedEntities.Count > 0)
        {
            usings.Add(appValidNs);
            usings.Add(sharedReqNs);
        }

        if (useInteractors)
        {
            usings.Add(ucAbsNs);
            usings.Add(ucImplNs);
        }

        var sb = new StringBuilder();

        // Usings — FluentValidation alias is not needed; IValidator<> is registered by full name
        foreach (var u in usings)
            sb.AppendLine($"using {u};");
        sb.AppendLine();

        sb.AppendLine("namespace Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("public static class ApplicationServiceCollectionExtensions");
        sb.AppendLine("{");
        sb.AppendLine("    public static IServiceCollection AddApplicationServices(this IServiceCollection services)");
        sb.AppendLine("    {");

        // ── Validators ────────────────────────────────────────────────────────
        if (validatedEntities.Count > 0)
        {
            var registerCreate = (crud & CrudOperation.Post) != 0;
            var registerUpdate = (crud & (CrudOperation.Put | CrudOperation.Patch)) != 0;

            sb.AppendLine("        // Validators");
            foreach (var entity in validatedEntities)
            {
                var name = entity.EntityTypeName;
                if (registerCreate)
                    sb.AppendLine($"        services.AddScoped<FluentValidation.IValidator<{sharedReqNs}.Create{name}Request>, Create{name}RequestValidator>();");
                if (registerUpdate)
                    sb.AppendLine($"        services.AddScoped<FluentValidation.IValidator<{sharedReqNs}.Update{name}Request>, Update{name}RequestValidator>();");
            }
        }

        // ── Use-case interactors ──────────────────────────────────────────────
        if (useInteractors)
        {
            var ucLines = new SortedSet<string>(System.StringComparer.Ordinal);

            // Entities with PK — full set of enabled ops
            foreach (var entity in model.Entities.Where(e => !e.IsJoinTable && e.HasPrimaryKey))
            {
                var name   = entity.EntityTypeName;
                var plural = entity.DbSetPropertyName;

                if ((crud & CrudOperation.GetList) != 0)
                    ucLines.Add($"        services.AddScoped<IList{plural}UseCase, List{plural}UseCase>();");
                if ((crud & CrudOperation.GetById) != 0)
                    ucLines.Add($"        services.AddScoped<IGet{name}ByIdUseCase, Get{name}ByIdUseCase>();");
                if ((crud & CrudOperation.Post) != 0)
                    ucLines.Add($"        services.AddScoped<ICreate{name}UseCase, Create{name}UseCase>();");
                if ((crud & CrudOperation.Put) != 0)
                    ucLines.Add($"        services.AddScoped<IUpdate{name}UseCase, Update{name}UseCase>();");
                if ((crud & CrudOperation.Patch) != 0)
                    ucLines.Add($"        services.AddScoped<IPatch{name}UseCase, Patch{name}UseCase>();");
                if ((crud & CrudOperation.Delete) != 0)
                    ucLines.Add($"        services.AddScoped<IDelete{name}UseCase, Delete{name}UseCase>();");
            }

            // Pk-less entities — list only
            foreach (var entity in model.Entities.Where(e => !e.IsJoinTable && !e.HasPrimaryKey))
            {
                var plural = entity.DbSetPropertyName;
                ucLines.Add($"        services.AddScoped<IList{plural}UseCase, List{plural}UseCase>();");
            }

            // Views — list only
            foreach (var view in ctx.Graph.Views)
            {
                var plural = CasingHelper.ToPascalCase(Pluralizer.Pluralize(Pluralizer.Singularize(view.Name)));
                ucLines.Add($"        services.AddScoped<IList{plural}UseCase, List{plural}UseCase>();");
            }

            if (ucLines.Count > 0)
            {
                if (validatedEntities.Count > 0)
                    sb.AppendLine();
                sb.AppendLine("        // Use-case interactors");
                foreach (var line in ucLines)
                    sb.AppendLine(line);
            }
        }

        sb.AppendLine();
        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    // ── Infrastructure installer ──────────────────────────────────────────────

    static string BuildInfrastructureInstaller(EmitterContext ctx, string project)
    {
        var cfg      = ctx.Config;
        var model    = ctx.Model;
        var da       = cfg.DataAccess;
        var repos    = cfg.EmitRepositoriesAndAbstractions;

        var dbCtx       = $"{project}DbContext";
        var infraDataNs = $"{CleanLayout.InfrastructureNamespace(project)}.Data";
        var appAbsNs    = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var infraRepoNs = $"{CleanLayout.InfrastructureNamespace(project)}.Repositories";
        var appPortsNs  = CleanLayout.PortsNamespace(project);
        var infraLogNs  = CleanLayout.LoggingNamespace(project);

        var usings = new SortedSet<string>(System.StringComparer.Ordinal);
        usings.Add("Microsoft.Extensions.Configuration");

        if (da == DataAccessKind.EfCore)
            usings.Add("Microsoft.EntityFrameworkCore");

        usings.Add(infraDataNs);
        usings.Add(appPortsNs);

        if (repos)
        {
            usings.Add(appAbsNs);
            usings.Add(infraRepoNs);
        }

        var sb = new StringBuilder();

        foreach (var u in usings)
            sb.AppendLine($"using {u};");
        sb.AppendLine();

        sb.AppendLine("namespace Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("public static class InfrastructureServiceCollectionExtensions");
        sb.AppendLine("{");
        sb.AppendLine("    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)");
        sb.AppendLine("    {");

        // ── Data access ───────────────────────────────────────────────────────
        sb.AppendLine("        // Data access");
        if (da == DataAccessKind.EfCore)
        {
            sb.AppendLine($"        services.AddDbContext<{dbCtx}>(options =>");
            sb.AppendLine("        {");
            sb.AppendLine("            var connectionString = configuration.GetConnectionString(\"DefaultConnection\")");
            sb.AppendLine("                ?? throw new InvalidOperationException(\"Missing connection string 'DefaultConnection' in configuration.\");");
            sb.AppendLine("            options.UseSqlServer(connectionString);");
            sb.AppendLine("        });");
        }
        else // Dapper
        {
            sb.AppendLine("        services.AddSingleton<IDbConnectionFactory, SqlDbConnectionFactory>();");
        }

        // ── Repositories ──────────────────────────────────────────────────────
        if (repos)
        {
            sb.AppendLine();
            sb.AppendLine("        // Repositories");
            var sortedEntities = model.Entities
                .Where(e => !e.IsJoinTable && e.HasPrimaryKey)
                .OrderBy(e => e.EntityTypeName, System.StringComparer.Ordinal);
            foreach (var entity in sortedEntities)
            {
                var name = entity.EntityTypeName;
                sb.AppendLine($"        services.AddScoped<I{name}Repository, {name}Repository>();");
            }
        }

        // ── Logger port + clock ───────────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine("        // Ports");
        sb.AppendLine($"        services.AddScoped(typeof({appPortsNs}.IAppLogger<>), typeof({infraLogNs}.MicrosoftAppLogger<>));");
        sb.AppendLine("        services.AddSingleton(System.TimeProvider.System);");

        // ── Unit of Work ──────────────────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine("        // Unit of Work");
        if (da == DataAccessKind.EfCore)
            sb.AppendLine($"        services.AddScoped<{appPortsNs}.IUnitOfWork, {infraDataNs}.EfUnitOfWork>();");
        else
            sb.AppendLine($"        services.AddScoped<{appPortsNs}.IUnitOfWork, {infraDataNs}.DapperUnitOfWork>();");

        // ── Stored procedures ─────────────────────────────────────────────────
        if (ctx.Graph.StoredProcedures.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("        // Stored procedures");
            if (cfg.PartitionStoredProceduresBySchema)
            {
                var sprocSchemas = ctx.Graph.StoredProcedures
                    .Select(sp => CasingHelper.ToPascalCase(sp.Schema))
                    .Distinct(System.StringComparer.Ordinal)
                    .OrderBy(s => s, System.StringComparer.Ordinal);
                foreach (var schema in sprocSchemas)
                    sb.AppendLine($"        services.AddScoped<I{schema}StoredProcedures, {schema}StoredProcedures>();");
            }
            else
            {
                sb.AppendLine("        services.AddScoped<IStoredProcedures, StoredProcedures>();");
            }
        }

        // ── DB functions ──────────────────────────────────────────────────────
        if (ctx.Graph.Functions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("        // DB functions");
            var fnSchemas = ctx.Graph.Functions
                .Select(f => CasingHelper.ToPascalCase(f.Schema))
                .Distinct(System.StringComparer.Ordinal)
                .OrderBy(s => s, System.StringComparer.Ordinal);
            foreach (var schema in fnSchemas)
                sb.AppendLine($"        services.AddScoped<I{schema}DbFunctions, {schema}DbFunctions>();");
        }

        sb.AppendLine();
        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
