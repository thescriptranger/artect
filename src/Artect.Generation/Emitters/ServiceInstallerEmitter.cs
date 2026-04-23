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
/// <para>
/// Phase F: validators now bind <c>IValidator&lt;TCommand&gt;</c> (not the Shared Request type).
/// Each interactor is registered through a decorator chain
/// (Validation → Logging → Transaction for commands, Validation → Logging for queries)
/// and exposed under the open-generic <c>IUseCase&lt;TRequest, UseCaseResult&lt;TPayload&gt;&gt;</c>
/// contract. Named marker interfaces (<c>I&lt;Op&gt;&lt;Entity&gt;UseCase</c>) are no longer emitted.
/// </para>
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
        var cfg      = ctx.Config;
        var model    = ctx.Model;
        var crud     = cfg.Crud;
        var anyWrite = (crud & (CrudOperation.Post | CrudOperation.Put | CrudOperation.Patch)) != 0;

        var ucImplNs    = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";
        var appValidNs  = $"{CleanLayout.ApplicationNamespace(project)}.Validators";
        var commonNs    = CleanLayout.ApplicationCommonNamespace(project);
        var behaviorsNs = $"{commonNs}.Behaviors";
        var commandsNs  = CleanLayout.ApplicationCommandsNamespace(project);
        var queriesNs   = CleanLayout.ApplicationQueriesNamespace(project);
        var modelsNs    = CleanLayout.ApplicationModelsNamespace(project);
        var portsNs     = CleanLayout.PortsNamespace(project);

        // Entities eligible for validator registration (write-enabled, have PK, not join table)
        var validatedEntities = anyWrite
            ? model.Entities
                .Where(e => !e.IsJoinTable && e.HasPrimaryKey)
                .OrderBy(e => e.EntityTypeName, System.StringComparer.Ordinal)
                .ToList()
            : new List<NamedEntity>();

        var usings = new SortedSet<string>(System.StringComparer.Ordinal)
        {
            ucImplNs,
            commonNs,
            behaviorsNs,
            commandsNs,
            queriesNs,
            modelsNs,
            portsNs,
        };

        if (validatedEntities.Count > 0)
        {
            usings.Add(appValidNs);
        }

        var sb = new StringBuilder();

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

            sb.AppendLine("        // Validators — bound to Application Command types");
            foreach (var entity in validatedEntities)
            {
                var name = entity.EntityTypeName;
                if (registerCreate)
                    sb.AppendLine($"        services.AddScoped<IValidator<Create{name}Command>, Create{name}CommandValidator>();");
                if (registerUpdate)
                {
                    if ((crud & CrudOperation.Put) != 0)
                        sb.AppendLine($"        services.AddScoped<IValidator<Update{name}Command>, Update{name}CommandValidator>();");
                    if ((crud & CrudOperation.Patch) != 0)
                        sb.AppendLine($"        services.AddScoped<IValidator<Patch{name}Command>, Patch{name}CommandValidator>();");
                }
            }
            sb.AppendLine();
        }

        // ── Use-case interactors ──────────────────────────────────────────────
        // Each interactor is registered twice: once as its concrete type (so behaviors can resolve it),
        // and once under the open-generic IUseCase<TRequest, UseCaseResult<TPayload>> service with the
        // decorator chain composed in the factory lambda.
        var ucLines = new List<string>();

        foreach (var entity in model.Entities.Where(e => !e.IsJoinTable && e.HasPrimaryKey)
                                             .OrderBy(e => e.EntityTypeName, System.StringComparer.Ordinal))
        {
            var name   = entity.EntityTypeName;
            var plural = entity.DbSetPropertyName;

            if ((crud & CrudOperation.GetList) != 0)
                AppendQueryRegistration(ucLines, $"List{plural}", $"List{plural}Query", $"PagedResult<{name}Model>");

            if ((crud & CrudOperation.GetById) != 0)
                AppendQueryRegistration(ucLines, $"Get{name}ById", $"Get{name}ByIdQuery", $"{name}Model");

            if ((crud & CrudOperation.Post) != 0)
                AppendCommandRegistration(ucLines, $"Create{name}", $"Create{name}Command", $"{name}Model");

            if ((crud & CrudOperation.Put) != 0)
                AppendCommandRegistration(ucLines, $"Update{name}", $"Update{name}Command", "Unit");

            if ((crud & CrudOperation.Patch) != 0)
                AppendCommandRegistration(ucLines, $"Patch{name}", $"Patch{name}Command", "Unit");

            if ((crud & CrudOperation.Delete) != 0)
                AppendCommandRegistration(ucLines, $"Delete{name}", $"Delete{name}Command", "Unit");
        }

        foreach (var entity in model.Entities.Where(e => !e.IsJoinTable && !e.HasPrimaryKey)
                                             .OrderBy(e => e.EntityTypeName, System.StringComparer.Ordinal))
        {
            var name   = entity.EntityTypeName;
            var plural = entity.DbSetPropertyName;
            AppendQueryRegistration(ucLines, $"List{plural}", $"List{plural}Query", $"PagedResult<{name}Model>");
        }

        foreach (var view in ctx.Graph.Views)
        {
            var typeName = CasingHelper.ToPascalCase(Pluralizer.Singularize(view.Name), ctx.NamingCorrections);
            var plural   = CasingHelper.ToPascalCase(Pluralizer.Pluralize(Pluralizer.Singularize(view.Name)), ctx.NamingCorrections);
            AppendQueryRegistration(ucLines, $"List{plural}", $"List{plural}Query", $"PagedResult<{typeName}Model>");
        }

        if (ucLines.Count > 0)
        {
            sb.AppendLine("        // Use-case interactors — registered under IUseCase<,> with decorator chains");
            foreach (var line in ucLines)
                sb.AppendLine(line);
        }

        sb.AppendLine();
        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Appends a command interactor registration: concrete type + open-generic IUseCase&lt;,&gt;
    /// factory that threads ValidationBehavior → LoggingBehavior → TransactionBehavior.
    /// </summary>
    static void AppendCommandRegistration(List<string> sb, string opName, string commandName, string payloadType)
    {
        var concrete = $"{opName}UseCase";
        sb.Add($"        services.AddScoped<{concrete}>();");
        sb.Add($"        services.AddScoped<IUseCase<{commandName}, UseCaseResult<{payloadType}>>>(sp =>");
        sb.Add("        {");
        sb.Add($"            IUseCase<{commandName}, UseCaseResult<{payloadType}>> inner = sp.GetRequiredService<{concrete}>();");
        sb.Add($"            inner = new ValidationBehavior<{commandName}, {payloadType}>(inner, sp.GetService<IValidator<{commandName}>>());");
        sb.Add($"            inner = new LoggingBehavior<{commandName}, UseCaseResult<{payloadType}>>(inner, sp.GetRequiredService<IAppLogger<{commandName}>>());");
        sb.Add($"            inner = new TransactionBehavior<{commandName}, {payloadType}>(inner, sp.GetRequiredService<IUnitOfWork>());");
        sb.Add("            return inner;");
        sb.Add("        });");
    }

    /// <summary>
    /// Appends a query interactor registration: concrete type + open-generic IUseCase&lt;,&gt;
    /// factory that threads ValidationBehavior → LoggingBehavior. No TransactionBehavior for queries.
    /// </summary>
    static void AppendQueryRegistration(List<string> sb, string opName, string queryName, string payloadType)
    {
        var concrete = $"{opName}UseCase";
        sb.Add($"        services.AddScoped<{concrete}>();");
        sb.Add($"        services.AddScoped<IUseCase<{queryName}, UseCaseResult<{payloadType}>>>(sp =>");
        sb.Add("        {");
        sb.Add($"            IUseCase<{queryName}, UseCaseResult<{payloadType}>> inner = sp.GetRequiredService<{concrete}>();");
        sb.Add($"            inner = new ValidationBehavior<{queryName}, {payloadType}>(inner, sp.GetService<IValidator<{queryName}>>());");
        sb.Add($"            inner = new LoggingBehavior<{queryName}, UseCaseResult<{payloadType}>>(inner, sp.GetRequiredService<IAppLogger<{queryName}>>());");
        sb.Add("            return inner;");
        sb.Add("        });");
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
        // Validate the connection string at installer-call time (app startup) so a missing
        // or empty value fails fast with a clear message, rather than surfacing later as a
        // "ConnectionString property has not been initialized" exception on the first query.
        sb.AppendLine("        // Data access");
        sb.AppendLine("        var connectionString = configuration.GetConnectionString(\"DefaultConnection\");");
        sb.AppendLine("        if (string.IsNullOrWhiteSpace(connectionString))");
        sb.AppendLine("            throw new InvalidOperationException(");
        sb.AppendLine("                \"Missing connection string 'DefaultConnection'. Set it in appsettings.json, \" +");
        sb.AppendLine("                \"appsettings.Development.json, or the ConnectionStrings__DefaultConnection environment variable.\");");
        sb.AppendLine();
        if (da == DataAccessKind.EfCore)
        {
            sb.AppendLine($"        services.AddDbContext<{dbCtx}>(options => options.UseSqlServer(connectionString));");
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
            var splitRepos = cfg.SplitRepositoriesByIntent;
            foreach (var entity in sortedEntities)
            {
                var name = entity.EntityTypeName;
                if (splitRepos)
                {
                    sb.AppendLine($"        services.AddScoped<I{name}ReadRepository, {name}ReadRepository>();");
                    sb.AppendLine($"        services.AddScoped<I{name}WriteRepository, {name}WriteRepository>();");
                }
                else
                {
                    sb.AppendLine($"        services.AddScoped<I{name}Repository, {name}Repository>();");
                }
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
                    .Select(sp => CasingHelper.ToPascalCase(sp.Schema, ctx.NamingCorrections))
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
                .Select(f => CasingHelper.ToPascalCase(f.Schema, ctx.NamingCorrections))
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
