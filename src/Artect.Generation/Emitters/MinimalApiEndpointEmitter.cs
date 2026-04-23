using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits one <c>&lt;Plural&gt;Endpoints</c> static class per entity (and list-only for pk-less tables/views).
/// When <c>cfg.EmitUseCaseInteractors</c> is true, lambdas inject use-case interactors and delegate all
/// logic to them (one-line handlers). Otherwise, lambdas inject <c>I&lt;Entity&gt;Repository</c> (or the
/// raw data-access type) and contain the logic inline.
/// </summary>
public sealed class MinimalApiEndpointEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var list = new List<EmittedFile>();
        var project    = ctx.Config.ProjectName;
        var useInteractors = ctx.Config.EmitUseCaseInteractors;

        // ── regular entities (tables with PK) ─────────────────────────────
        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.IsJoinTable) continue;

            var plural = entity.DbSetPropertyName; // already pluralised PascalCase
            var route = CasingHelper.ToKebabCase(plural);
            string content;
            if (entity.HasPrimaryKey)
            {
                content = useInteractors
                    ? BuildFullEndpointsWithInteractors(ctx, entity, plural, route)
                    : BuildFullEndpoints(ctx, entity, plural, route);
            }
            else
            {
                content = useInteractors
                    ? BuildListOnlyEndpointsWithInteractors(ctx, entity.EntityTypeName, plural, route, project)
                    : BuildListOnlyEndpoints(ctx, entity, plural, route, isView: false);
            }

            var path = CleanLayout.EndpointPath(project, plural);
            list.Add(new EmittedFile(path, content));
        }

        // ── views (list-only) ──────────────────────────────────────────────
        foreach (var view in ctx.Graph.Views)
        {
            var typeName = CasingHelper.ToPascalCase(Pluralizer.Singularize(view.Name));
            var plural   = CasingHelper.ToPascalCase(Pluralizer.Pluralize(Pluralizer.Singularize(view.Name)));
            var route    = CasingHelper.ToKebabCase(plural);
            var content  = useInteractors
                ? BuildListOnlyEndpointsWithInteractors(ctx, typeName, plural, route, project)
                : BuildViewListOnlyEndpoints(ctx, view, typeName, plural, route);
            var path     = CleanLayout.EndpointPath(project, plural);
            list.Add(new EmittedFile(path, content));
        }

        return list;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Use-case interactor endpoints (one-liner lambdas)
    // ──────────────────────────────────────────────────────────────────────

    static string BuildFullEndpointsWithInteractors(EmitterContext ctx, NamedEntity entity, string plural, string route)
    {
        var project    = ctx.Config.ProjectName;
        var crud       = ctx.Config.Crud;
        var entityName = entity.EntityTypeName;
        var pkType     = PkClrType(entity.Table);

        var endpointNs = $"{project}.Api.Endpoints";
        var apiNs      = $"{project}.Api";
        var ucAbsNs    = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.UseCases";

        var reqNs  = $"{CleanLayout.SharedNamespace(project)}.Requests";
        var hasWrite = (crud & (CrudOperation.Post | CrudOperation.Put | CrudOperation.Patch)) != 0;

        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.AspNetCore.Builder;");
        sb.AppendLine("using Microsoft.AspNetCore.Http;");
        sb.AppendLine("using Microsoft.AspNetCore.Routing;");
        sb.AppendLine($"using {apiNs};");
        if (hasWrite)
            sb.AppendLine($"using {reqNs};");
        sb.AppendLine($"using {ucAbsNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {endpointNs};");
        sb.AppendLine();
        sb.AppendLine($"public static class {plural}Endpoints");
        sb.AppendLine("{");
        sb.AppendLine($"    public static IEndpointRouteBuilder Map{plural}Endpoints(this IEndpointRouteBuilder app)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var group = app.MapGroup(\"/api/{{version?}}/{route}\");");
        sb.AppendLine();

        if ((crud & CrudOperation.GetList) != 0)
        {
            sb.AppendLine($"        group.MapGet(\"/\", async (IList{plural}UseCase useCase, int page = 1, int pageSize = 50, System.Threading.CancellationToken ct = default) =>");
            sb.AppendLine($"            (await useCase.ExecuteAsync(page, pageSize, ct)).ToIResult());");
            sb.AppendLine();
        }

        if ((crud & CrudOperation.GetById) != 0)
        {
            sb.AppendLine($"        group.MapGet(\"/{{id}}\", async ({pkType} id, IGet{entityName}ByIdUseCase useCase, System.Threading.CancellationToken ct = default) =>");
            sb.AppendLine($"            (await useCase.ExecuteAsync(id, ct)).ToIResult());");
            sb.AppendLine();
        }

        if ((crud & CrudOperation.Post) != 0)
        {
            sb.AppendLine($"        group.MapPost(\"/\", async (Create{entityName}Request request, ICreate{entityName}UseCase useCase, System.Threading.CancellationToken ct = default) =>");
            sb.AppendLine($"            (await useCase.ExecuteAsync(request, ct)).ToIResult());");
            sb.AppendLine();
        }

        if ((crud & CrudOperation.Put) != 0)
        {
            sb.AppendLine($"        group.MapPut(\"/{{id}}\", async ({pkType} id, Update{entityName}Request request, IUpdate{entityName}UseCase useCase, System.Threading.CancellationToken ct = default) =>");
            sb.AppendLine($"            (await useCase.ExecuteAsync(id, request, ct)).ToIResult());");
            sb.AppendLine();
        }

        if ((crud & CrudOperation.Patch) != 0)
        {
            sb.AppendLine($"        group.MapPatch(\"/{{id}}\", async ({pkType} id, Update{entityName}Request request, IPatch{entityName}UseCase useCase, System.Threading.CancellationToken ct = default) =>");
            sb.AppendLine($"            (await useCase.ExecuteAsync(id, request, ct)).ToIResult());");
            sb.AppendLine();
        }

        if ((crud & CrudOperation.Delete) != 0)
        {
            sb.AppendLine($"        group.MapDelete(\"/{{id}}\", async ({pkType} id, IDelete{entityName}UseCase useCase, System.Threading.CancellationToken ct = default) =>");
            sb.AppendLine($"            (await useCase.ExecuteAsync(id, ct)).ToIResult());");
            sb.AppendLine();
        }

        sb.AppendLine("        return app;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    static string BuildListOnlyEndpointsWithInteractors(EmitterContext ctx, string entityName, string plural, string route, string project)
    {
        var endpointNs = $"{project}.Api.Endpoints";
        var apiNs      = $"{project}.Api";
        var ucAbsNs    = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.UseCases";

        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.AspNetCore.Builder;");
        sb.AppendLine("using Microsoft.AspNetCore.Http;");
        sb.AppendLine("using Microsoft.AspNetCore.Routing;");
        sb.AppendLine($"using {apiNs};");
        sb.AppendLine($"using {ucAbsNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {endpointNs};");
        sb.AppendLine();
        sb.AppendLine("// No primary key — list endpoint only.");
        sb.AppendLine($"public static class {plural}Endpoints");
        sb.AppendLine("{");
        sb.AppendLine($"    public static IEndpointRouteBuilder Map{plural}Endpoints(this IEndpointRouteBuilder app)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var group = app.MapGroup(\"/api/{{version?}}/{route}\");");
        sb.AppendLine();
        sb.AppendLine($"        group.MapGet(\"/\", async (IList{plural}UseCase useCase, int page = 1, int pageSize = 50, System.Threading.CancellationToken ct = default) =>");
        sb.AppendLine($"            (await useCase.ExecuteAsync(page, pageSize, ct)).ToIResult());");
        sb.AppendLine();
        sb.AppendLine("        return app;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Full CRUD endpoints (entity has a PK)
    // ──────────────────────────────────────────────────────────────────────

    static string BuildFullEndpoints(EmitterContext ctx, NamedEntity entity, string plural, string route)
    {
        var project  = ctx.Config.ProjectName;
        var crud     = ctx.Config.Crud;
        var repos    = ctx.Config.EmitRepositoriesAndAbstractions;
        var da       = ctx.Config.DataAccess;

        var entityName = entity.EntityTypeName;
        var pk         = entity.Table.PrimaryKey!;
        var pkCol      = entity.Table.Columns.First(c =>
            string.Equals(c.Name, pk.ColumnNames[0], System.StringComparison.OrdinalIgnoreCase));
        var pkType     = PkClrType(entity.Table);
        var pkProp     = EntityNaming.PropertyName(pkCol);
        var dbset      = entity.DbSetPropertyName;
        var dbCtx      = $"{project}DbContext";
        var repoType   = $"I{entityName}Repository";

        var endpointNs   = $"{project}.Api.Endpoints";
        var requestsNs   = $"{CleanLayout.SharedNamespace(project)}.Requests";
        var responsesNs  = $"{CleanLayout.SharedNamespace(project)}.Responses";
        var mappingsNs   = $"{CleanLayout.ApplicationNamespace(project)}.Mappings";
        var infraDataNs  = $"{CleanLayout.InfrastructureNamespace(project)}.Data";
        var repoAbsNs    = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var infraRepoNs  = $"{CleanLayout.InfrastructureNamespace(project)}.Repositories";

        var sb = new StringBuilder();

        // usings
        sb.AppendLine("using Microsoft.AspNetCore.Builder;");
        sb.AppendLine("using Microsoft.AspNetCore.Http;");
        sb.AppendLine("using Microsoft.AspNetCore.Routing;");
        sb.AppendLine($"using {responsesNs};");
        sb.AppendLine($"using {requestsNs};");
        sb.AppendLine($"using {mappingsNs};");
        if (repos)
        {
            sb.AppendLine($"using {repoAbsNs};");
        }
        else if (da == DataAccessKind.EfCore)
        {
            sb.AppendLine("using Microsoft.EntityFrameworkCore;");
            sb.AppendLine($"using {infraDataNs};");
        }
        else
        {
            sb.AppendLine($"using {infraDataNs};");
        }
        sb.AppendLine();
        sb.AppendLine($"namespace {endpointNs};");
        sb.AppendLine();
        sb.AppendLine($"public static class {plural}Endpoints");
        sb.AppendLine("{");
        sb.AppendLine($"    public static IEndpointRouteBuilder Map{plural}Endpoints(this IEndpointRouteBuilder app)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var group = app.MapGroup(\"/api/{{version?}}/{route}\");");
        sb.AppendLine();

        if ((crud & CrudOperation.GetList) != 0)
            EmitList(sb, entityName, plural, dbset, dbCtx, repoType, repos, da);
        if ((crud & CrudOperation.GetById) != 0)
            EmitGetById(sb, entityName, dbset, dbCtx, repoType, pkType, pkProp, repos, da);
        if ((crud & CrudOperation.Post) != 0)
            EmitCreate(sb, entityName, dbset, dbCtx, repoType, pkProp, route, repos, da);
        if ((crud & CrudOperation.Put) != 0)
            EmitUpdate(sb, entityName, dbset, dbCtx, repoType, pkType, pkProp, "MapPut", repos, da);
        if ((crud & CrudOperation.Patch) != 0)
            EmitUpdate(sb, entityName, dbset, dbCtx, repoType, pkType, pkProp, "MapPatch", repos, da);
        if ((crud & CrudOperation.Delete) != 0)
            EmitDelete(sb, entityName, dbset, dbCtx, repoType, pkType, pkProp, repos, da);

        sb.AppendLine();
        sb.AppendLine("        return app;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    // ──────────────────────────────────────────────────────────────────────
    // List-only endpoints for pk-less tables
    // ──────────────────────────────────────────────────────────────────────

    static string BuildListOnlyEndpoints(EmitterContext ctx, NamedEntity entity, string plural, string route, bool isView)
    {
        var project     = ctx.Config.ProjectName;
        var repos       = ctx.Config.EmitRepositoriesAndAbstractions;
        var da          = ctx.Config.DataAccess;
        var entityName  = entity.EntityTypeName;
        var dbset       = entity.DbSetPropertyName;
        var dbCtx       = $"{project}DbContext";
        var repoType    = $"I{entityName}Repository";

        var endpointNs  = $"{project}.Api.Endpoints";
        var responsesNs = $"{CleanLayout.SharedNamespace(project)}.Responses";
        var mappingsNs  = $"{CleanLayout.ApplicationNamespace(project)}.Mappings";
        var infraDataNs = $"{CleanLayout.InfrastructureNamespace(project)}.Data";
        var repoAbsNs   = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";

        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.AspNetCore.Builder;");
        sb.AppendLine("using Microsoft.AspNetCore.Http;");
        sb.AppendLine("using Microsoft.AspNetCore.Routing;");
        sb.AppendLine($"using {responsesNs};");
        sb.AppendLine($"using {mappingsNs};");
        if (repos)
            sb.AppendLine($"using {repoAbsNs};");
        else if (da == DataAccessKind.EfCore)
        {
            sb.AppendLine("using Microsoft.EntityFrameworkCore;");
            sb.AppendLine($"using {infraDataNs};");
        }
        else
            sb.AppendLine($"using {infraDataNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {endpointNs};");
        sb.AppendLine();
        sb.AppendLine("// No primary key — list endpoint only.");
        sb.AppendLine($"public static class {plural}Endpoints");
        sb.AppendLine("{");
        sb.AppendLine($"    public static IEndpointRouteBuilder Map{plural}Endpoints(this IEndpointRouteBuilder app)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var group = app.MapGroup(\"/api/{{version?}}/{route}\");");
        sb.AppendLine();
        EmitList(sb, entityName, plural, dbset, dbCtx, repoType, repos, da);
        sb.AppendLine();
        sb.AppendLine("        return app;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ──────────────────────────────────────────────────────────────────────
    // List-only endpoints for views
    // ──────────────────────────────────────────────────────────────────────

    static string BuildViewListOnlyEndpoints(EmitterContext ctx, View view, string typeName, string plural, string route)
    {
        var project     = ctx.Config.ProjectName;
        var repos       = ctx.Config.EmitRepositoriesAndAbstractions;
        var da          = ctx.Config.DataAccess;
        var dbCtx       = $"{project}DbContext";
        var dbset       = CasingHelper.ToPascalCase(Pluralizer.Pluralize(typeName));
        var repoType    = $"I{typeName}Repository";

        var endpointNs  = $"{project}.Api.Endpoints";
        var responsesNs = $"{CleanLayout.SharedNamespace(project)}.Responses";
        var mappingsNs  = $"{CleanLayout.ApplicationNamespace(project)}.Mappings";
        var infraDataNs = $"{CleanLayout.InfrastructureNamespace(project)}.Data";
        var repoAbsNs   = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";

        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.AspNetCore.Builder;");
        sb.AppendLine("using Microsoft.AspNetCore.Http;");
        sb.AppendLine("using Microsoft.AspNetCore.Routing;");
        sb.AppendLine($"using {responsesNs};");
        sb.AppendLine($"using {mappingsNs};");
        if (repos)
            sb.AppendLine($"using {repoAbsNs};");
        else if (da == DataAccessKind.EfCore)
        {
            sb.AppendLine("using Microsoft.EntityFrameworkCore;");
            sb.AppendLine($"using {infraDataNs};");
        }
        else
            sb.AppendLine($"using {infraDataNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {endpointNs};");
        sb.AppendLine();
        sb.AppendLine("// View — read-only list endpoint.");
        sb.AppendLine($"public static class {plural}Endpoints");
        sb.AppendLine("{");
        sb.AppendLine($"    public static IEndpointRouteBuilder Map{plural}Endpoints(this IEndpointRouteBuilder app)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var group = app.MapGroup(\"/api/{{version?}}/{route}\");");
        sb.AppendLine();
        EmitList(sb, typeName, plural, dbset, dbCtx, repoType, repos, da);
        sb.AppendLine();
        sb.AppendLine("        return app;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Handler builders
    // ──────────────────────────────────────────────────────────────────────

    static void EmitList(StringBuilder sb, string entityName, string plural,
        string dbset, string dbCtx, string repoType,
        bool repos, DataAccessKind da)
    {
        if (repos)
        {
            sb.AppendLine($"        group.MapGet(\"/\", async ({repoType} repo, int page = 1, int pageSize = 50, System.Threading.CancellationToken ct = default) =>");
            sb.AppendLine("        {");
            sb.AppendLine("            if (page < 1) { page = 1; }");
            sb.AppendLine("            if (pageSize < 1) { pageSize = 50; }");
            sb.AppendLine("            var result = await repo.ListAsync(page, pageSize, ct).ConfigureAwait(false);");
            sb.AppendLine("            return Results.Ok(result);");
            sb.AppendLine("        });");
        }
        else if (da == DataAccessKind.EfCore)
        {
            sb.AppendLine($"        group.MapGet(\"/\", async ({dbCtx} db, int page = 1, int pageSize = 50, System.Threading.CancellationToken ct = default) =>");
            sb.AppendLine("        {");
            sb.AppendLine("            if (page < 1) { page = 1; }");
            sb.AppendLine("            if (pageSize < 1) { pageSize = 50; }");
            sb.AppendLine($"            var query = db.{dbset}.AsNoTracking();");
            sb.AppendLine("            // Extension point: customize the IQueryable/SQL here");
            sb.AppendLine("            var totalCount = await query.CountAsync(ct).ConfigureAwait(false);");
            sb.AppendLine("            var items = await query.Skip((page - 1) * pageSize).Take(pageSize)");
            sb.AppendLine($"                .Select(e => e.ToResponse()).ToListAsync(ct).ConfigureAwait(false);");
            sb.AppendLine($"            return Results.Ok(new PagedResponse<{entityName}Response>");
            sb.AppendLine("            {");
            sb.AppendLine("                Items = items,");
            sb.AppendLine("                Page = page,");
            sb.AppendLine("                PageSize = pageSize,");
            sb.AppendLine("                TotalCount = totalCount,");
            sb.AppendLine("            });");
            sb.AppendLine("        });");
        }
        else // Dapper direct
        {
            sb.AppendLine($"        group.MapGet(\"/\", async (IDbConnectionFactory connections, int page = 1, int pageSize = 50, System.Threading.CancellationToken ct = default) =>");
            sb.AppendLine("        {");
            sb.AppendLine("            if (page < 1) { page = 1; }");
            sb.AppendLine("            if (pageSize < 1) { pageSize = 50; }");
            sb.AppendLine("            using var conn = connections.CreateOpenConnection();");
            sb.AppendLine("            // Extension point: customize the IQueryable/SQL here");
            sb.AppendLine($"            var items = (await Dapper.SqlMapper.QueryAsync<{entityName}>(conn,");
            sb.AppendLine($"                \"SELECT * FROM [{entityName}] ORDER BY (SELECT NULL) OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY\",");
            sb.AppendLine("                new { skip = (page - 1) * pageSize, take = pageSize }).ConfigureAwait(false)).AsList();");
            sb.AppendLine($"            return Results.Ok(items.Select(e => e.ToResponse()).ToList());");
            sb.AppendLine("        });");
        }
    }

    static void EmitGetById(StringBuilder sb, string entityName,
        string dbset, string dbCtx, string repoType,
        string pkType, string pkProp, bool repos, DataAccessKind da)
    {
        sb.AppendLine();
        if (repos)
        {
            sb.AppendLine($"        group.MapGet(\"/{{id}}\", async ({pkType} id, {repoType} repo, System.Threading.CancellationToken ct = default) =>");
            sb.AppendLine("        {");
            sb.AppendLine("            var item = await repo.GetByIdAsync(id, ct).ConfigureAwait(false);");
            sb.AppendLine("            return item is null ? Results.NotFound() : Results.Ok(item);");
            sb.AppendLine("        });");
        }
        else if (da == DataAccessKind.EfCore)
        {
            sb.AppendLine($"        group.MapGet(\"/{{id}}\", async ({pkType} id, {dbCtx} db, System.Threading.CancellationToken ct = default) =>");
            sb.AppendLine("        {");
            sb.AppendLine($"            var entity = await db.{dbset}.AsNoTracking()");
            sb.AppendLine($"                .FirstOrDefaultAsync(e => e.{pkProp} == id, ct).ConfigureAwait(false);");
            sb.AppendLine($"            return entity is null ? Results.NotFound() : Results.Ok(entity.ToResponse());");
            sb.AppendLine("        });");
        }
        else
        {
            sb.AppendLine($"        group.MapGet(\"/{{id}}\", async ({pkType} id, IDbConnectionFactory connections, System.Threading.CancellationToken ct = default) =>");
            sb.AppendLine("        {");
            sb.AppendLine("            using var conn = connections.CreateOpenConnection();");
            sb.AppendLine($"            var entity = await Dapper.SqlMapper.QuerySingleOrDefaultAsync<{entityName}>(conn,");
            sb.AppendLine($"                \"SELECT * FROM [{entityName}] WHERE [{pkProp}] = @id\", new {{ id }}).ConfigureAwait(false);");
            sb.AppendLine($"            return entity is null ? Results.NotFound() : Results.Ok(entity.ToResponse());");
            sb.AppendLine("        });");
        }
    }

    static void EmitCreate(StringBuilder sb, string entityName,
        string dbset, string dbCtx, string repoType,
        string pkProp, string route, bool repos, DataAccessKind da)
    {
        sb.AppendLine();
        if (repos)
        {
            sb.AppendLine($"        group.MapPost(\"/\", async (Create{entityName}Request request, {repoType} repo, System.Threading.CancellationToken ct = default) =>");
            sb.AppendLine("        {");
            sb.AppendLine($"            var validator = new {entityName}RequestValidators.Create{entityName}Validator();");
            sb.AppendLine("            var result = validator.Validate(request);");
            sb.AppendLine("            if (!result.IsValid) { return Results.BadRequest(result.Errors); }");
            sb.AppendLine("            var dto = await repo.CreateAsync(request.ToDto(), ct).ConfigureAwait(false);");
            sb.AppendLine($"            return Results.Created($\"/api/{{version?}}/{route}/{{dto.{pkProp}}}\", dto.ToResponse());");
            sb.AppendLine("        });");
        }
        else if (da == DataAccessKind.EfCore)
        {
            sb.AppendLine($"        group.MapPost(\"/\", async (Create{entityName}Request request, {dbCtx} db, System.Threading.CancellationToken ct = default) =>");
            sb.AppendLine("        {");
            sb.AppendLine($"            var validator = new {entityName}RequestValidators.Create{entityName}Validator();");
            sb.AppendLine("            var result = validator.Validate(request);");
            sb.AppendLine("            if (!result.IsValid) { return Results.BadRequest(result.Errors); }");
            sb.AppendLine("            var entity = request.ToEntity();");
            sb.AppendLine($"            db.{dbset}.Add(entity);");
            sb.AppendLine("            await db.SaveChangesAsync(ct).ConfigureAwait(false);");
            sb.AppendLine($"            return Results.Created($\"/api/{{version?}}/{route}/{{entity.{pkProp}}}\", entity.ToResponse());");
            sb.AppendLine("        });");
        }
        else
        {
            sb.AppendLine($"        group.MapPost(\"/\", async (Create{entityName}Request request, IDbConnectionFactory connections, System.Threading.CancellationToken ct = default) =>");
            sb.AppendLine("        {");
            sb.AppendLine($"            var validator = new {entityName}RequestValidators.Create{entityName}Validator();");
            sb.AppendLine("            var result = validator.Validate(request);");
            sb.AppendLine("            if (!result.IsValid) { return Results.BadRequest(result.Errors); }");
            sb.AppendLine("            // TODO: insert via Dapper and return created resource");
            sb.AppendLine("            return Results.StatusCode(501);");
            sb.AppendLine("        });");
        }
    }

    static void EmitUpdate(StringBuilder sb, string entityName,
        string dbset, string dbCtx, string repoType,
        string pkType, string pkProp,
        string mapMethod, bool repos, DataAccessKind da)
    {
        sb.AppendLine();
        if (repos)
        {
            sb.AppendLine($"        group.{mapMethod}(\"/{{id}}\", async ({pkType} id, Update{entityName}Request request, {repoType} repo, System.Threading.CancellationToken ct = default) =>");
            sb.AppendLine("        {");
            sb.AppendLine($"            var validator = new {entityName}RequestValidators.Update{entityName}Validator();");
            sb.AppendLine("            var result = validator.Validate(request);");
            sb.AppendLine("            if (!result.IsValid) { return Results.BadRequest(result.Errors); }");
            sb.AppendLine("            var existing = await repo.GetByIdAsync(id, ct).ConfigureAwait(false);");
            sb.AppendLine("            if (existing is null) { return Results.NotFound(); }");
            sb.AppendLine("            existing.ApplyUpdate(request);");
            sb.AppendLine("            await repo.UpdateAsync(existing, ct).ConfigureAwait(false);");
            sb.AppendLine("            return Results.NoContent();");
            sb.AppendLine("        });");
        }
        else if (da == DataAccessKind.EfCore)
        {
            sb.AppendLine($"        group.{mapMethod}(\"/{{id}}\", async ({pkType} id, Update{entityName}Request request, {dbCtx} db, System.Threading.CancellationToken ct = default) =>");
            sb.AppendLine("        {");
            sb.AppendLine($"            var validator = new {entityName}RequestValidators.Update{entityName}Validator();");
            sb.AppendLine("            var result = validator.Validate(request);");
            sb.AppendLine("            if (!result.IsValid) { return Results.BadRequest(result.Errors); }");
            sb.AppendLine($"            var entity = await db.{dbset}.FindAsync(new object[] {{ id }}, ct).ConfigureAwait(false);");
            sb.AppendLine("            if (entity is null) { return Results.NotFound(); }");
            sb.AppendLine("            entity.ApplyUpdate(request);");
            sb.AppendLine("            await db.SaveChangesAsync(ct).ConfigureAwait(false);");
            sb.AppendLine("            return Results.NoContent();");
            sb.AppendLine("        });");
        }
        else
        {
            sb.AppendLine($"        group.{mapMethod}(\"/{{id}}\", async ({pkType} id, Update{entityName}Request request, IDbConnectionFactory connections, System.Threading.CancellationToken ct = default) =>");
            sb.AppendLine("        {");
            sb.AppendLine($"            var validator = new {entityName}RequestValidators.Update{entityName}Validator();");
            sb.AppendLine("            var result = validator.Validate(request);");
            sb.AppendLine("            if (!result.IsValid) { return Results.BadRequest(result.Errors); }");
            sb.AppendLine("            // TODO: update via Dapper");
            sb.AppendLine("            return Results.NoContent();");
            sb.AppendLine("        });");
        }
    }

    static void EmitDelete(StringBuilder sb, string entityName,
        string dbset, string dbCtx, string repoType,
        string pkType, string pkProp, bool repos, DataAccessKind da)
    {
        sb.AppendLine();
        if (repos)
        {
            sb.AppendLine($"        group.MapDelete(\"/{{id}}\", async ({pkType} id, {repoType} repo, System.Threading.CancellationToken ct = default) =>");
            sb.AppendLine("        {");
            sb.AppendLine("            var existing = await repo.GetByIdAsync(id, ct).ConfigureAwait(false);");
            sb.AppendLine("            if (existing is null) { return Results.NotFound(); }");
            sb.AppendLine("            await repo.DeleteAsync(id, ct).ConfigureAwait(false);");
            sb.AppendLine("            return Results.NoContent();");
            sb.AppendLine("        });");
        }
        else if (da == DataAccessKind.EfCore)
        {
            sb.AppendLine($"        group.MapDelete(\"/{{id}}\", async ({pkType} id, {dbCtx} db, System.Threading.CancellationToken ct = default) =>");
            sb.AppendLine("        {");
            sb.AppendLine($"            var entity = await db.{dbset}.FindAsync(new object[] {{ id }}, ct).ConfigureAwait(false);");
            sb.AppendLine("            if (entity is null) { return Results.NotFound(); }");
            sb.AppendLine($"            db.{dbset}.Remove(entity);");
            sb.AppendLine("            await db.SaveChangesAsync(ct).ConfigureAwait(false);");
            sb.AppendLine("            return Results.NoContent();");
            sb.AppendLine("        });");
        }
        else
        {
            sb.AppendLine($"        group.MapDelete(\"/{{id}}\", async ({pkType} id, IDbConnectionFactory connections, System.Threading.CancellationToken ct = default) =>");
            sb.AppendLine("        {");
            sb.AppendLine("            using var conn = connections.CreateOpenConnection();");
            sb.AppendLine($"            var rows = await Dapper.SqlMapper.ExecuteAsync(conn,");
            sb.AppendLine($"                \"DELETE FROM [{entityName}] WHERE [{pkProp}] = @id\", new {{ id }}).ConfigureAwait(false);");
            sb.AppendLine("            return rows > 0 ? Results.NoContent() : Results.NotFound();");
            sb.AppendLine("        });");
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Returns C# PK type string — tuple for composite keys.</summary>
    static string PkClrType(Table table)
    {
        var pk = table.PrimaryKey!;
        if (pk.ColumnNames.Count == 1)
        {
            var col = table.Columns.First(c =>
                string.Equals(c.Name, pk.ColumnNames[0], System.StringComparison.OrdinalIgnoreCase));
            var cs = SqlTypeMap.ToCs(col.ClrType);
            return SqlTypeMap.IsValueType(col.ClrType) ? cs : cs;
        }
        // Composite — emit a tuple
        var parts = pk.ColumnNames.Select(n =>
        {
            var c = table.Columns.First(col =>
                string.Equals(col.Name, n, System.StringComparison.OrdinalIgnoreCase));
            return SqlTypeMap.ToCs(c.ClrType);
        });
        return "(" + string.Join(", ", parts) + ")";
    }
}
