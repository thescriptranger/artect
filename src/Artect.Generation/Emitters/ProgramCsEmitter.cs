using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits <c>Program.cs</c> into <c>src/&lt;Project&gt;.Api/</c>.
/// Uses StringBuilder rather than the template DSL because the number of
/// conditionals (data-access, auth, versioning, per-entity DI lines) would
/// make a Handlebars template unreadable.
/// Auth and versioning bodies are loaded as raw fragment templates so they
/// remain editable without recompiling the tool.
/// </summary>
public sealed class ProgramCsEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var project = ctx.Config.ProjectName;
        var content = Build(ctx, project);
        var path    = CleanLayout.ProgramCsPath(project);
        return new[] { new EmittedFile(path, content) };
    }

    static string Build(EmitterContext ctx, string project)
    {
        var cfg     = ctx.Config;
        var model   = ctx.Model;
        var da      = cfg.DataAccess;
        var auth    = cfg.Auth;
        var ver     = cfg.ApiVersioning;
        var repos   = cfg.EmitRepositoriesAndAbstractions;
        var crud    = cfg.Crud;
        var anyWrite = (crud & (CrudOperation.Post | CrudOperation.Put | CrudOperation.Patch)) != 0;

        var dbCtx       = $"{project}DbContext";
        var infraDataNs = $"{CleanLayout.InfrastructureNamespace(project)}.Data";
        var appAbsNs    = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var infraRepoNs = $"{CleanLayout.InfrastructureNamespace(project)}.Repositories";
        var appValidNs  = $"{CleanLayout.ApplicationNamespace(project)}.Validators";
        var sharedReqNs = $"{CleanLayout.SharedNamespace(project)}.Requests";
        var endpointNs  = $"{CleanLayout.ApiNamespace(project)}.Endpoints";
        var ucAbsNs     = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.UseCases";
        var ucImplNs    = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";
        var useInteractors = cfg.EmitUseCaseInteractors;

        // ── Collect entities that need validator DI (write-enabled, have PK, not join table) ──
        var validatedEntities = anyWrite
            ? model.Entities
                .Where(e => !e.IsJoinTable && e.HasPrimaryKey)
                .OrderBy(e => e.EntityTypeName, System.StringComparer.Ordinal)
                .ToList()
            : new List<NamedEntity>();

        // ── Usings (sorted) ───────────────────────────────────────────────────
        var usings = new SortedSet<string>(System.StringComparer.Ordinal);

        if (da == DataAccessKind.EfCore)
            usings.Add("Microsoft.EntityFrameworkCore");

        usings.Add("Scalar.AspNetCore");
        usings.Add(infraDataNs);
        usings.Add(endpointNs);

        if (repos && da == DataAccessKind.EfCore)
            usings.Add(appAbsNs);
        if (repos && da == DataAccessKind.Dapper)
        {
            usings.Add(appAbsNs);
            usings.Add(infraRepoNs);
        }

        if (useInteractors && repos)
        {
            usings.Add(ucAbsNs);
            usings.Add(ucImplNs);
        }

        if (validatedEntities.Count > 0)
        {
            usings.Add("FluentValidation");
            usings.Add(appValidNs);
            usings.Add(sharedReqNs);
        }

        var sb = new StringBuilder();

        foreach (var u in usings)
            sb.AppendLine($"using {u};");
        sb.AppendLine();

        // ── Builder setup ─────────────────────────────────────────────────────
        sb.AppendLine("var builder = WebApplication.CreateBuilder(args);");
        sb.AppendLine();

        // OpenAPI
        sb.AppendLine("builder.Services.AddOpenApi();");
        sb.AppendLine();

        // ── Data access DI ────────────────────────────────────────────────────
        if (da == DataAccessKind.EfCore)
        {
            sb.AppendLine($"builder.Services.AddDbContext<{dbCtx}>(options =>");
            sb.AppendLine("{");
            sb.AppendLine("    var connectionString = builder.Configuration.GetConnectionString(\"DefaultConnection\")");
            sb.AppendLine("        ?? throw new InvalidOperationException(\"Missing connection string 'DefaultConnection' in configuration.\");");
            sb.AppendLine("    options.UseSqlServer(connectionString);");
            sb.AppendLine("});");
        }
        else // Dapper
        {
            sb.AppendLine("builder.Services.AddSingleton<IDbConnectionFactory, SqlDbConnectionFactory>();");
        }

        // ── Repository DI (only when repos enabled) ───────────────────────────
        if (repos)
        {
            sb.AppendLine();
            var sortedEntities = model.Entities
                .Where(e => !e.IsJoinTable && e.HasPrimaryKey)
                .OrderBy(e => e.EntityTypeName, System.StringComparer.Ordinal);
            foreach (var entity in sortedEntities)
            {
                var name = entity.EntityTypeName;
                sb.AppendLine($"builder.Services.AddScoped<I{name}Repository, {name}Repository>();");
            }
        }

        // ── Use-case interactor DI ────────────────────────────────────────────
        if (useInteractors && repos)
        {
            // Collect all registration lines and sort alpha-by-type-name for determinism.
            var ucLines = new SortedSet<string>(System.StringComparer.Ordinal);

            // Entities with PK — full set of enabled ops
            foreach (var entity in model.Entities.Where(e => !e.IsJoinTable && e.HasPrimaryKey))
            {
                var name   = entity.EntityTypeName;
                var plural = entity.DbSetPropertyName;

                if ((crud & CrudOperation.GetList) != 0)
                    ucLines.Add($"builder.Services.AddScoped<IList{plural}UseCase, List{plural}UseCase>();");
                if ((crud & CrudOperation.GetById) != 0)
                    ucLines.Add($"builder.Services.AddScoped<IGet{name}ByIdUseCase, Get{name}ByIdUseCase>();");
                if ((crud & CrudOperation.Post) != 0)
                    ucLines.Add($"builder.Services.AddScoped<ICreate{name}UseCase, Create{name}UseCase>();");
                if ((crud & CrudOperation.Put) != 0)
                    ucLines.Add($"builder.Services.AddScoped<IUpdate{name}UseCase, Update{name}UseCase>();");
                if ((crud & CrudOperation.Patch) != 0)
                    ucLines.Add($"builder.Services.AddScoped<IPatch{name}UseCase, Patch{name}UseCase>();");
                if ((crud & CrudOperation.Delete) != 0)
                    ucLines.Add($"builder.Services.AddScoped<IDelete{name}UseCase, Delete{name}UseCase>();");
            }

            // Pk-less entities — list only
            foreach (var entity in model.Entities.Where(e => !e.IsJoinTable && !e.HasPrimaryKey))
            {
                var plural = entity.DbSetPropertyName;
                ucLines.Add($"builder.Services.AddScoped<IList{plural}UseCase, List{plural}UseCase>();");
            }

            // Views — list only
            foreach (var view in ctx.Graph.Views)
            {
                var plural = CasingHelper.ToPascalCase(Pluralizer.Pluralize(Pluralizer.Singularize(view.Name)));
                ucLines.Add($"builder.Services.AddScoped<IList{plural}UseCase, List{plural}UseCase>();");
            }

            if (ucLines.Count > 0)
            {
                sb.AppendLine();
                foreach (var line in ucLines)
                    sb.AppendLine(line);
            }
        }

        // ── Validator DI ──────────────────────────────────────────────────────
        if (validatedEntities.Count > 0)
        {
            sb.AppendLine();
            var registerCreate = (crud & CrudOperation.Post) != 0;
            var registerUpdate = (crud & (CrudOperation.Put | CrudOperation.Patch)) != 0;

            foreach (var entity in validatedEntities)
            {
                var name = entity.EntityTypeName;
                if (registerCreate)
                    sb.AppendLine($"builder.Services.AddScoped<IValidator<Create{name}Request>, Create{name}RequestValidator>();");
                if (registerUpdate)
                    sb.AppendLine($"builder.Services.AddScoped<IValidator<Update{name}Request>, Update{name}RequestValidator>();");
            }
        }

        // ── Auth fragment ─────────────────────────────────────────────────────
        if (auth != AuthKind.None)
        {
            sb.AppendLine();
            var fragmentName = auth switch
            {
                AuthKind.JwtBearer => "AuthJwt.cs.artect",
                AuthKind.Auth0     => "AuthAuth0.cs.artect",
                AuthKind.AzureAd   => "AuthAzureAd.cs.artect",
                AuthKind.ApiKey    => "AuthApiKey.cs.artect",
                _                  => null,
            };
            if (fragmentName is not null)
            {
                var fragment = ctx.Templates.Load(fragmentName);
                sb.Append(fragment);
            }
        }

        // ── Versioning fragment ───────────────────────────────────────────────
        if (ver != ApiVersioningKind.None)
        {
            sb.AppendLine();
            var fragmentName = ver switch
            {
                ApiVersioningKind.UrlSegment  => "VersioningUrlSegment.cs.artect",
                ApiVersioningKind.Header      => "VersioningHeader.cs.artect",
                ApiVersioningKind.QueryString => "VersioningQueryString.cs.artect",
                _                             => null,
            };
            if (fragmentName is not null)
            {
                var fragment = ctx.Templates.Load(fragmentName);
                sb.Append(fragment);
            }
        }

        // ── Stored procedure / function DI ────────────────────────────────────
        if (ctx.Graph.StoredProcedures.Count > 0)
        {
            sb.AppendLine();
            if (cfg.PartitionStoredProceduresBySchema)
            {
                var sprocSchemas = ctx.Graph.StoredProcedures
                    .Select(sp => CasingHelper.ToPascalCase(sp.Schema))
                    .Distinct(System.StringComparer.Ordinal)
                    .OrderBy(s => s, System.StringComparer.Ordinal);
                foreach (var schema in sprocSchemas)
                    sb.AppendLine($"builder.Services.AddScoped<I{schema}StoredProcedures, {schema}StoredProcedures>();");
            }
            else
            {
                sb.AppendLine("builder.Services.AddScoped<IStoredProcedures, StoredProcedures>();");
            }
        }

        if (ctx.Graph.Functions.Count > 0)
        {
            var fnSchemas = ctx.Graph.Functions
                .Select(f => CasingHelper.ToPascalCase(f.Schema))
                .Distinct(System.StringComparer.Ordinal)
                .OrderBy(s => s, System.StringComparer.Ordinal);
            foreach (var schema in fnSchemas)
                sb.AppendLine($"builder.Services.AddScoped<I{schema}DbFunctions, {schema}DbFunctions>();");
        }

        // ── Build the app ─────────────────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine("var app = builder.Build();");
        sb.AppendLine();

        // ── Middleware pipeline ───────────────────────────────────────────────
        sb.AppendLine("if (app.Environment.IsDevelopment())");
        sb.AppendLine("{");
        sb.AppendLine("    app.MapOpenApi();");
        sb.AppendLine("    app.MapScalarApiReference();");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("app.UseHttpsRedirection();");

        if (auth != AuthKind.None)
        {
            sb.AppendLine("app.UseAuthentication();");
            sb.AppendLine("app.UseAuthorization();");
        }

        sb.AppendLine();

        // ── Endpoint mapping (alpha-sorted) ───────────────────────────────────
        var allEntityPlurals = model.Entities
            .Where(e => !e.IsJoinTable)
            .Select(e => e.DbSetPropertyName)
            .OrderBy(p => p, System.StringComparer.Ordinal);

        var viewPlurals = ctx.Graph.Views
            .Select(v => CasingHelper.ToPascalCase(Pluralizer.Pluralize(Pluralizer.Singularize(v.Name))))
            .OrderBy(p => p, System.StringComparer.Ordinal);

        foreach (var plural in allEntityPlurals.Concat(viewPlurals).OrderBy(p => p, System.StringComparer.Ordinal))
            sb.AppendLine($"app.Map{plural}Endpoints();");

        sb.AppendLine();
        sb.AppendLine("app.Run();");
        sb.AppendLine();
        sb.AppendLine("// Required for WebApplicationFactory<Program> in integration tests.");
        sb.AppendLine("public partial class Program { }");

        return sb.ToString();
    }
}
