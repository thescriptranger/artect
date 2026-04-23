using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits one <c>&lt;Plural&gt;Endpoints</c> static class per entity (and list-only for pk-less tables/views).
/// Lambdas inject use-case interactors and delegate all logic to them (one-line handlers).
/// </summary>
public sealed class MinimalApiEndpointEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var list = new List<EmittedFile>();
        var project    = ctx.Config.ProjectName;

        // ── regular entities (tables with PK) ─────────────────────────────
        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.IsJoinTable) continue;

            var plural = entity.DbSetPropertyName; // already pluralised PascalCase
            var route = CasingHelper.ToKebabCase(plural);
            string content;
            if (entity.HasPrimaryKey)
            {
                content = BuildFullEndpointsWithInteractors(ctx, entity, plural, route);
            }
            else
            {
                content = BuildListOnlyEndpointsWithInteractors(ctx, entity.EntityTypeName, plural, route, project);
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
            var content  = BuildListOnlyEndpointsWithInteractors(ctx, typeName, plural, route, project);
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
