using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits one <c>&lt;Plural&gt;Endpoints</c> static class per entity (and list-only for pk-less tables/views).
/// Lambdas are thin one-liners: Request → Command/Query → Interactor → UseCaseResult → IResult.
/// Mapping between wire types and application types is delegated to <c>&lt;Entity&gt;ApiMappers</c>.
/// </summary>
public sealed class MinimalApiEndpointEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var list = new List<EmittedFile>();
        var project = ctx.Config.ProjectName;

        var corrections = ctx.NamingCorrections;

        // ── regular entities (tables with PK) ─────────────────────────────
        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.IsJoinTable) continue;

            var plural = entity.DbSetPropertyName; // already pluralised PascalCase
            var route = CasingHelper.ToKebabCase(plural, corrections);
            string content;
            if (entity.HasPrimaryKey)
            {
                content = BuildFullEndpoints(ctx, entity, plural, route);
            }
            else
            {
                content = BuildListOnlyEndpoints(ctx, entity.EntityTypeName, plural, route, project);
            }

            var path = CleanLayout.EndpointPath(project, plural);
            list.Add(new EmittedFile(path, content));
        }

        // ── views (list-only) ──────────────────────────────────────────────
        foreach (var view in ctx.Graph.Views)
        {
            var typeName = CasingHelper.ToPascalCase(Pluralizer.Singularize(view.Name), corrections);
            var plural   = CasingHelper.ToPascalCase(Pluralizer.Pluralize(Pluralizer.Singularize(view.Name)), corrections);
            var route    = CasingHelper.ToKebabCase(plural, corrections);
            var content  = BuildListOnlyEndpoints(ctx, typeName, plural, route, project);
            var path     = CleanLayout.EndpointPath(project, plural);
            list.Add(new EmittedFile(path, content));
        }

        return list;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Full CRUD endpoints (entity has PK)
    // ──────────────────────────────────────────────────────────────────────

    static string BuildFullEndpoints(EmitterContext ctx, NamedEntity entity, string plural, string route)
    {
        var project    = ctx.Config.ProjectName;
        var crud       = ctx.Config.Crud;
        var entityName = entity.EntityTypeName;

        var endpointNs = $"{project}.Api.Endpoints";
        var reqNs      = CleanLayout.SharedRequestsNamespace(project);
        var respNs     = CleanLayout.SharedResponsesNamespace(project);
        var mapNs      = CleanLayout.ApiMappingNamespace(project);
        var commonNs   = CleanLayout.ApplicationCommonNamespace(project);
        var modelsNs   = CleanLayout.ApplicationModelsNamespace(project);
        var commandsNs = CleanLayout.ApplicationCommandsNamespace(project);
        var queriesNs  = CleanLayout.ApplicationQueriesNamespace(project);
        var ucNs       = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";

        // PK route segment(s)
        var pk = entity.Table.PrimaryKey!;
        var corrections = ctx.NamingCorrections;
        var pkRouteParams = BuildPkRouteParams(entity, corrections);   // e.g. "int id" or "int id, Guid sub"
        var pkRouteSegments = BuildPkRouteSegments(entity, corrections); // e.g. "/{id}" or "/{id}/{sub}"

        var hasWrite = (crud & (CrudOperation.Post | CrudOperation.Put | CrudOperation.Patch)) != 0;
        var hasQuery = (crud & (CrudOperation.GetList | CrudOperation.GetById)) != 0;

        var sb = new StringBuilder();
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using Microsoft.AspNetCore.Builder;");
        sb.AppendLine("using Microsoft.AspNetCore.Http;");
        sb.AppendLine("using Microsoft.AspNetCore.Routing;");
        sb.AppendLine($"using {commonNs};");
        sb.AppendLine($"using {modelsNs};");
        if (hasWrite)
        {
            sb.AppendLine($"using {reqNs};");
            sb.AppendLine($"using {commandsNs};");
        }
        else if ((crud & CrudOperation.Delete) != 0)
        {
            sb.AppendLine($"using {commandsNs};");
        }
        if (hasQuery)
            sb.AppendLine($"using {queriesNs};");
        sb.AppendLine($"using {respNs};");
        sb.AppendLine($"using {mapNs};");
        sb.AppendLine($"using {ucNs};");
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
            // List: PagedResult<EntityModel> → PagedResponse<EntityResponse>
            sb.AppendLine($"        group.MapGet(\"/\", async (IUseCase<List{plural}Query, UseCaseResult<PagedResult<{entityName}Model>>> useCase, int? page, int? pageSize, System.Threading.CancellationToken ct) =>");
            sb.AppendLine($"            (await useCase.ExecuteAsync({entityName}ApiMappers.ToListQuery(page ?? 1, pageSize ?? 50), ct)).ToIResult(m => new PagedResponse<{entityName}Response>");
            sb.AppendLine("            {");
            sb.AppendLine("                Items = m.Items.Select(x => x.ToResponse()).ToArray(),");
            sb.AppendLine("                Page = m.Page,");
            sb.AppendLine("                PageSize = m.PageSize,");
            sb.AppendLine("                TotalCount = m.TotalCount,");
            sb.AppendLine("            }));");
            sb.AppendLine();
        }

        if ((crud & CrudOperation.GetById) != 0)
        {
            sb.AppendLine($"        group.MapGet(\"{pkRouteSegments}\", async ({pkRouteParams}, IUseCase<Get{entityName}ByIdQuery, UseCaseResult<{entityName}Model>> useCase, System.Threading.CancellationToken ct) =>");
            sb.AppendLine($"            (await useCase.ExecuteAsync({entityName}ApiMappers.ToGetByIdQuery({BuildPkArgNames(entity, corrections)}), ct)).ToIResult(m => m.ToResponse()));");
            sb.AppendLine();
        }

        if ((crud & CrudOperation.Post) != 0)
        {
            sb.AppendLine($"        group.MapPost(\"/\", async (Create{entityName}Request request, IUseCase<Create{entityName}Command, UseCaseResult<{entityName}Model>> useCase, System.Threading.CancellationToken ct) =>");
            sb.AppendLine($"            (await useCase.ExecuteAsync(request.ToCommand(), ct)).ToIResult(m => m.ToResponse()));");
            sb.AppendLine();
        }

        if ((crud & CrudOperation.Put) != 0)
        {
            sb.AppendLine($"        group.MapPut(\"{pkRouteSegments}\", async ({pkRouteParams}, Update{entityName}Request request, IUseCase<Update{entityName}Command, UseCaseResult<Unit>> useCase, System.Threading.CancellationToken ct) =>");
            sb.AppendLine($"            (await useCase.ExecuteAsync(request.ToUpdateCommand({BuildPkArgNames(entity, corrections)}), ct)).ToIResult());");
            sb.AppendLine();
        }

        if ((crud & CrudOperation.Patch) != 0)
        {
            sb.AppendLine($"        group.MapPatch(\"{pkRouteSegments}\", async ({pkRouteParams}, Update{entityName}Request request, IUseCase<Patch{entityName}Command, UseCaseResult<Unit>> useCase, System.Threading.CancellationToken ct) =>");
            sb.AppendLine($"            (await useCase.ExecuteAsync(request.ToPatchCommand({BuildPkArgNames(entity, corrections)}), ct)).ToIResult());");
            sb.AppendLine();
        }

        if ((crud & CrudOperation.Delete) != 0)
        {
            sb.AppendLine($"        group.MapDelete(\"{pkRouteSegments}\", async ({pkRouteParams}, IUseCase<Delete{entityName}Command, UseCaseResult<Unit>> useCase, System.Threading.CancellationToken ct) =>");
            sb.AppendLine($"            (await useCase.ExecuteAsync({entityName}ApiMappers.ToDeleteCommand({BuildPkArgNames(entity, corrections)}), ct)).ToIResult());");
            sb.AppendLine();
        }

        sb.AppendLine("        return app;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    static string BuildListOnlyEndpoints(EmitterContext ctx, string entityName, string plural, string route, string project)
    {
        var endpointNs = $"{project}.Api.Endpoints";
        var respNs     = CleanLayout.SharedResponsesNamespace(project);
        var mapNs      = CleanLayout.ApiMappingNamespace(project);
        var commonNs   = CleanLayout.ApplicationCommonNamespace(project);
        var modelsNs   = CleanLayout.ApplicationModelsNamespace(project);
        var queriesNs  = CleanLayout.ApplicationQueriesNamespace(project);
        var ucNs       = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";

        var sb = new StringBuilder();
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using Microsoft.AspNetCore.Builder;");
        sb.AppendLine("using Microsoft.AspNetCore.Http;");
        sb.AppendLine("using Microsoft.AspNetCore.Routing;");
        sb.AppendLine($"using {commonNs};");
        sb.AppendLine($"using {modelsNs};");
        sb.AppendLine($"using {queriesNs};");
        sb.AppendLine($"using {respNs};");
        sb.AppendLine($"using {mapNs};");
        sb.AppendLine($"using {ucNs};");
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
        sb.AppendLine($"        group.MapGet(\"/\", async (IUseCase<List{plural}Query, UseCaseResult<PagedResult<{entityName}Model>>> useCase, int? page, int? pageSize, System.Threading.CancellationToken ct) =>");
        sb.AppendLine($"            (await useCase.ExecuteAsync(new List{plural}Query(page ?? 1, pageSize ?? 50), ct)).ToIResult(m => new PagedResponse<{entityName}Response>");
        sb.AppendLine("            {");
        sb.AppendLine("                Items = m.Items.Select(x => x.ToResponse()).ToArray(),");
        sb.AppendLine("                Page = m.Page,");
        sb.AppendLine("                PageSize = m.PageSize,");
        sb.AppendLine("                TotalCount = m.TotalCount,");
        sb.AppendLine("            }));");
        sb.AppendLine();
        sb.AppendLine("        return app;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ──────────────────────────────────────────────────────────────────────
    // PK helpers
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Route segments for PK columns, e.g. "/{id}" or "/{userId}/{roleId}".</summary>
    static string BuildPkRouteSegments(NamedEntity entity, System.Collections.Generic.IReadOnlyDictionary<string, string> corrections)
    {
        var pk = entity.Table.PrimaryKey!;
        var segments = pk.ColumnNames.Select(n => $"{{{CasingHelper.ToCamelCase(n, corrections)}}}");
        return "/" + string.Join("/", segments);
    }

    /// <summary>Lambda parameter list for PK columns with their C# types, e.g. "int id" or "int userId, int roleId".</summary>
    static string BuildPkRouteParams(NamedEntity entity, System.Collections.Generic.IReadOnlyDictionary<string, string> corrections)
    {
        var pk = entity.Table.PrimaryKey!;
        return string.Join(", ", pk.ColumnNames.Select(n =>
        {
            var col = entity.Table.Columns.First(c =>
                string.Equals(c.Name, n, System.StringComparison.OrdinalIgnoreCase));
            return $"{SqlTypeMap.ToCs(col.ClrType)} {CasingHelper.ToCamelCase(n, corrections)}";
        }));
    }

    /// <summary>Comma-separated camelCase PK argument names for mapper calls, e.g. "id" or "userId, roleId".</summary>
    static string BuildPkArgNames(NamedEntity entity, System.Collections.Generic.IReadOnlyDictionary<string, string> corrections)
    {
        var pk = entity.Table.PrimaryKey!;
        return string.Join(", ", pk.ColumnNames.Select(n => CasingHelper.ToCamelCase(n, corrections)));
    }
}
