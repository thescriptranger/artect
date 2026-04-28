using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits one &lt;Plural&gt;Endpoints.cs per entity into Api/Endpoints/.
/// Phase 2 shape: read endpoints inject I&lt;Entity&gt;ReadService; write endpoints
/// inject the per-op &lt;Verb&gt;&lt;Entity&gt;Handler. Endpoints never see
/// IUnitOfWork — handlers commit. Validation runs inline via IValidator&lt;Request&gt;.
/// </summary>
public sealed class MinimalApiEndpointEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var list = new List<EmittedFile>();
        var project = ctx.Config.ProjectName;
        var corrections = ctx.NamingCorrections;

        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.ShouldSkip(EntityClassification.AggregateRoot)) continue;

            var pluralPascal = CasingHelper.ToPascalCase(
                Pluralizer.Pluralize(entity.EntityTypeName), corrections);
            var routeKebab = CasingHelper.ToKebabCase(pluralPascal, corrections);
            list.Add(Build(ctx, entity, pluralPascal, routeKebab));
        }

        return list;
    }

    static EmittedFile Build(EmitterContext ctx, NamedEntity entity, string plural, string route)
    {
        var project = ctx.Config.ProjectName;
        var crud    = ctx.Config.Crud;
        var name    = entity.EntityTypeName;
        var corrections = ctx.NamingCorrections;

        var pk = entity.Table.PrimaryKey!;
        var pkColName = pk.ColumnNames[0];
        var pkCol = entity.Table.Columns.First(c =>
            string.Equals(c.Name, pkColName, System.StringComparison.OrdinalIgnoreCase));
        var pkProp = EntityNaming.PropertyName(pkCol, corrections);
        var pkType = SqlTypeMap.ToCs(pkCol.ClrType);
        var pkRouteConstraint = pkType switch
        {
            "int" or "long" => ":int",
            "System.Guid"   => ":guid",
            _               => string.Empty,
        };

        var nonServerGenCols = entity.Table.Columns.Where(c => !c.IsServerGenerated).ToList();
        var allCols          = entity.Table.Columns.ToList();

        var apiMapNs   = CleanLayout.ApiMappingsNamespace(project);
        var dtosNs     = CleanLayout.ApplicationDtosNamespace(project);
        var featureNs  = CleanLayout.ApplicationFeatureNamespace(project, name);
        var absNs      = CleanLayout.ApplicationFeatureAbstractionsNamespace(project, name);
        var appValidNs = CleanLayout.ApplicationValidatorsNamespace(project);
        var apiValidNs = CleanLayout.ApiValidatorsNamespace(project);
        var reqNs      = CleanLayout.SharedRequestsNamespace(project);
        var respNs     = CleanLayout.SharedResponsesNamespace(project);
        var endpointNs = $"{project}.Api.Endpoints";

        var sb = new StringBuilder();
        sb.AppendLine($"using {apiMapNs};");
        sb.AppendLine($"using {dtosNs};");
        sb.AppendLine($"using {featureNs};");
        sb.AppendLine($"using {absNs};");
        sb.AppendLine($"using {appValidNs};");
        sb.AppendLine($"using {apiValidNs};");
        sb.AppendLine($"using {reqNs};");
        sb.AppendLine($"using {respNs};");
        sb.AppendLine("using Microsoft.AspNetCore.Builder;");
        sb.AppendLine("using Microsoft.AspNetCore.Http;");
        sb.AppendLine("using Microsoft.AspNetCore.Routing;");
        sb.AppendLine();
        sb.AppendLine($"namespace {endpointNs};");
        sb.AppendLine();
        sb.AppendLine("// V#6 scaffolded CRUD baseline — generated from schema. Real business workflows");
        sb.AppendLine("// belong in their own endpoint files (or behind a different route prefix) and");
        sb.AppendLine("// should call ICommandHandler<,> / IQueryHandler<,> from " + project + ".Application.Abstractions");
        sb.AppendLine("// rather than the per-aggregate concrete handlers below.");
        sb.AppendLine($"public static partial class {plural}Endpoints");
        sb.AppendLine("{");
        sb.AppendLine($"    public static IEndpointRouteBuilder Map{plural}Endpoints(this IEndpointRouteBuilder app)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var group = app.MapGroup(\"/api/{route}\");");
        sb.AppendLine();

        if ((crud & CrudOperation.GetList) != 0)
            EmitGetList(sb, name);
        if ((crud & CrudOperation.GetById) != 0)
            EmitGetById(sb, name, pkType, pkRouteConstraint);
        if ((crud & CrudOperation.Post) != 0)
            EmitPost(sb, name, route, pkProp, nonServerGenCols, corrections);
        if ((crud & CrudOperation.Put) != 0)
            EmitUpdate(sb, name, pkColName, pkProp, pkType, pkRouteConstraint, allCols, corrections, verb: "Put", commandPrefix: "Update");
        // V#5: PATCH uses dedicated PatchXxxRequest (Optional<T?> fields) and PatchXxxHandler.
        // Domain Update method validates after merge; no API-level validator.
        if ((crud & CrudOperation.Patch) != 0)
            EmitPatch(sb, name, pkColName, pkProp, pkType, pkRouteConstraint, entity, corrections);
        if ((crud & CrudOperation.Delete) != 0)
            EmitDelete(sb, name, pkType, pkRouteConstraint);

        sb.AppendLine("        return app;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        var path = $"{CleanLayout.ApiDir(project)}/Endpoints/{plural}Endpoints.cs";
        return new EmittedFile(path, sb.ToString());
    }

    static void EmitGetList(StringBuilder sb, string name)
    {
        sb.AppendLine($"        group.MapGet(\"/\", async (I{name}ReadService reads, CancellationToken ct, int page = 1, int pageSize = 50) =>");
        sb.AppendLine("        {");
        sb.AppendLine("            if (page < 1) { page = 1; }");
        sb.AppendLine("            if (pageSize < 1) { pageSize = 50; }");
        sb.AppendLine("            var (items, totalCount) = await reads.GetPagedAsync(page, pageSize, ct).ConfigureAwait(false);");
        sb.AppendLine($"            return Results.Ok(new PagedResponse<{name}Response>");
        sb.AppendLine("            {");
        sb.AppendLine("                Items = items.Select(e => e.ToResponse()).ToList(),");
        sb.AppendLine("                Page = page,");
        sb.AppendLine("                PageSize = pageSize,");
        sb.AppendLine("                TotalCount = totalCount,");
        sb.AppendLine("            });");
        sb.AppendLine("        });");
        sb.AppendLine();
    }

    static void EmitGetById(StringBuilder sb, string name, string pkType, string pkRouteConstraint)
    {
        sb.AppendLine($"        group.MapGet(\"/{{id{pkRouteConstraint}}}\", async ({pkType} id, I{name}ReadService reads, CancellationToken ct) =>");
        sb.AppendLine("        {");
        sb.AppendLine("            var entity = await reads.GetByIdAsync(id, ct).ConfigureAwait(false);");
        sb.AppendLine("            return entity is null ? Results.NotFound() : Results.Ok(entity.ToResponse());");
        sb.AppendLine("        });");
        sb.AppendLine();
    }

    static void EmitPost(StringBuilder sb, string name, string route, string pkProp,
        IReadOnlyList<Column> nonServerGenCols, IReadOnlyDictionary<string, string> corrections)
    {
        sb.AppendLine($"        group.MapPost(\"/\", async (Create{name}Request request, Create{name}Handler handler, IValidator<Create{name}Request> validator, CancellationToken ct) =>");
        sb.AppendLine("        {");
        sb.AppendLine("            var validation = validator.Validate(request);");
        sb.AppendLine("            if (!validation.IsValid) { return validation.ToBadRequest(); }");
        sb.AppendLine();
        sb.AppendLine($"            var command = new Create{name}Command(");
        for (int i = 0; i < nonServerGenCols.Count; i++)
        {
            var col = nonServerGenCols[i];
            var prop = EntityNaming.PropertyName(col, corrections);
            var terminator = i == nonServerGenCols.Count - 1 ? ");" : ",";
            sb.AppendLine($"                request.{prop}{terminator}");
        }
        sb.AppendLine();
        sb.AppendLine("            var entity = await handler.HandleAsync(command, ct).ConfigureAwait(false);");
        sb.AppendLine($"            return Results.Created($\"/api/{route}/{{entity.{pkProp}}}\", entity.ToResponse());");
        sb.AppendLine("        });");
        sb.AppendLine();
    }

    static void EmitUpdate(StringBuilder sb, string name, string pkColName, string pkProp, string pkType, string pkRouteConstraint,
        IReadOnlyList<Column> allCols, IReadOnlyDictionary<string, string> corrections,
        string verb, string commandPrefix)
    {
        var commandType = $"{commandPrefix}{name}Command";
        var handlerType = $"{commandPrefix}{name}Handler";
        var requestType = $"Update{name}Request"; // Same Request type for Put and Patch
        var mapVerb = verb == "Put" ? "MapPut" : "MapPatch";

        sb.AppendLine($"        group.{mapVerb}(\"/{{id{pkRouteConstraint}}}\", async ({pkType} id, {requestType} request, {handlerType} handler, IValidator<{requestType}> validator, CancellationToken ct) =>");
        sb.AppendLine("        {");
        sb.AppendLine("            var validation = validator.Validate(request);");
        sb.AppendLine("            if (!validation.IsValid) { return validation.ToBadRequest(); }");
        sb.AppendLine();
        sb.AppendLine($"            var command = new {commandType}(");
        for (int i = 0; i < allCols.Count; i++)
        {
            var col = allCols[i];
            var prop = EntityNaming.PropertyName(col, corrections);
            var value = string.Equals(col.Name, pkColName, System.StringComparison.OrdinalIgnoreCase) ? "id" : $"request.{prop}";
            var terminator = i == allCols.Count - 1 ? ");" : ",";
            sb.AppendLine($"                {value}{terminator}");
        }
        sb.AppendLine();
        sb.AppendLine("            var entity = await handler.HandleAsync(command, ct).ConfigureAwait(false);");
        sb.AppendLine("            return entity is null ? Results.NotFound() : Results.Ok(entity.ToResponse());");
        sb.AppendLine("        });");
        sb.AppendLine();
    }

    /// <summary>
    /// V#5 PATCH endpoint. Deserializes <c>Patch&lt;Entity&gt;Request</c> (whose non-PK
    /// fields are <c>Optional&lt;T?&gt;</c>), builds <c>Patch&lt;Entity&gt;Command</c>
    /// 1:1, calls <c>Patch&lt;Entity&gt;Handler</c>. No API-level validator — domain
    /// <c>Update</c> validates after the handler merges Optional with the existing
    /// entity.
    /// </summary>
    static void EmitPatch(StringBuilder sb, string name, string pkColName, string pkProp, string pkType, string pkRouteConstraint,
        NamedEntity entity, IReadOnlyDictionary<string, string> corrections)
    {
        var commandType = $"Patch{name}Command";
        var handlerType = $"Patch{name}Handler";
        var requestType = $"Patch{name}Request";

        // Patch command shape (per CommandRecordsEmitter): PK + UpdateableColumns.
        var pkCols = entity.Table.PrimaryKey!.ColumnNames
            .ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        var commandCols = entity.Table.Columns
            .Where(c => pkCols.Contains(c.Name) && !entity.ColumnHasFlag(c.Name, ColumnMetadata.Ignored))
            .Concat(entity.UpdateableColumns())
            .ToList();

        sb.AppendLine($"        group.MapPatch(\"/{{id{pkRouteConstraint}}}\", async ({pkType} id, {requestType} request, {handlerType} handler, CancellationToken ct) =>");
        sb.AppendLine("        {");
        sb.AppendLine($"            var command = new {commandType}(");
        for (int i = 0; i < commandCols.Count; i++)
        {
            var col = commandCols[i];
            var prop = EntityNaming.PropertyName(col, corrections);
            var value = string.Equals(col.Name, pkColName, System.StringComparison.OrdinalIgnoreCase) ? "id" : $"request.{prop}";
            var terminator = i == commandCols.Count - 1 ? ");" : ",";
            sb.AppendLine($"                {value}{terminator}");
        }
        sb.AppendLine();
        sb.AppendLine("            var entity = await handler.HandleAsync(command, ct).ConfigureAwait(false);");
        sb.AppendLine("            return entity is null ? Results.NotFound() : Results.Ok(entity.ToResponse());");
        sb.AppendLine("        });");
        sb.AppendLine();
    }

    static void EmitDelete(StringBuilder sb, string name, string pkType, string pkRouteConstraint)
    {
        sb.AppendLine($"        group.MapDelete(\"/{{id{pkRouteConstraint}}}\", async ({pkType} id, Delete{name}Handler handler, CancellationToken ct) =>");
        sb.AppendLine("        {");
        sb.AppendLine("            var deleted = await handler.HandleAsync(id, ct).ConfigureAwait(false);");
        sb.AppendLine("            return deleted ? Results.NoContent() : Results.NotFound();");
        sb.AppendLine("        });");
        sb.AppendLine();
    }
}
