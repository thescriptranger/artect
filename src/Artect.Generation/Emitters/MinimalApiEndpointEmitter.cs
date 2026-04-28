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
        var authEnabled = ctx.Config.Auth != AuthKind.None;
        var versioning  = ctx.Config.ApiVersioning;
        var versioningEnabled = versioning != ApiVersioningKind.None;
        // V#10: UrlSegment versioning embeds the version in the route. Header /
        // QueryString strategies use the same /api/<plural> path and resolve the
        // version from headers / query parameters instead.
        var groupRoute = versioning == ApiVersioningKind.UrlSegment
            ? $"/api/v{{apiVersion:apiVersion}}/{route}"
            : $"/api/{route}";
        // The Created Location header on POST needs a CONCRETE URL — substitute v1 for
        // the route constraint so consumers can GET back the freshly-created resource.
        var createdLocationPrefix = versioning == ApiVersioningKind.UrlSegment
            ? $"/api/v1/{route}"
            : $"/api/{route}";

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

        var apiMapNs   = CleanLayout.ApiMappingsNamespace(project);
        var dtosNs     = CleanLayout.ApplicationDtosNamespace(project);
        var featureNs  = CleanLayout.ApplicationFeatureNamespace(project, name);
        var absNs      = CleanLayout.ApplicationFeatureAbstractionsNamespace(project, name);
        var appValidNs = CleanLayout.ApplicationValidatorsNamespace(project);
        var apiValidNs = CleanLayout.ApiValidatorsNamespace(project);
        var reqNs      = CleanLayout.SharedRequestsNamespace(project);
        var respNs     = CleanLayout.SharedResponsesNamespace(project);
        var endpointNs = $"{project}.Api.Endpoints";

        var filtersNs = $"{project}.Api.Filters";
        var needsValidationFilter = (crud & (CrudOperation.Post | CrudOperation.Put)) != 0;

        var sb = new StringBuilder();
        sb.AppendLine($"using {apiMapNs};");
        sb.AppendLine($"using {dtosNs};");
        sb.AppendLine($"using {featureNs};");
        sb.AppendLine($"using {absNs};");
        sb.AppendLine($"using {appValidNs};");
        sb.AppendLine($"using {apiValidNs};");
        if (needsValidationFilter)
            sb.AppendLine($"using {filtersNs};");
        sb.AppendLine($"using {reqNs};");
        sb.AppendLine($"using {respNs};");
        if (versioningEnabled)
            sb.AppendLine("using Asp.Versioning.Builder;");
        sb.AppendLine("using Microsoft.AspNetCore.Builder;");
        sb.AppendLine("using Microsoft.AspNetCore.Http;");
        sb.AppendLine("using Microsoft.AspNetCore.Routing;");
        sb.AppendLine();
        sb.AppendLine($"namespace {endpointNs};");
        sb.AppendLine();
        sb.AppendLine($"public static partial class {plural}Endpoints");
        sb.AppendLine("{");
        if (versioningEnabled)
            sb.AppendLine($"    public static IEndpointRouteBuilder Map{plural}Endpoints(this IEndpointRouteBuilder app, ApiVersionSet versionSet)");
        else
            sb.AppendLine($"    public static IEndpointRouteBuilder Map{plural}Endpoints(this IEndpointRouteBuilder app)");
        sb.AppendLine("    {");
        // V#9: when auth is configured, every endpoint in this group requires
        // authentication. Per-endpoint policies/scopes can be added via additional
        // .RequireAuthorization("policy") calls on individual MapXxx returns.
        // V#10: when versioning is configured, every endpoint in this group registers
        // against the shared version set at v1. Hand-write a separate group for V2.
        var groupChain = string.Empty;
        if (authEnabled) groupChain += ".RequireAuthorization()";
        if (versioningEnabled) groupChain += ".WithApiVersionSet(versionSet).HasApiVersion(new Asp.Versioning.ApiVersion(1, 0))";
        sb.AppendLine($"        var group = app.MapGroup(\"{groupRoute}\"){groupChain};");
        sb.AppendLine();

        if ((crud & CrudOperation.GetList) != 0)
            EmitGetList(sb, name);
        if ((crud & CrudOperation.GetById) != 0)
            EmitGetById(sb, name, pkType, pkRouteConstraint);
        // V#8: lambdas now delegate. Validation runs in ValidationFilter<TRequest>;
        // request→command translation lives in <Entity>Mappings.ToCommand(...).
        if ((crud & CrudOperation.Post) != 0)
            EmitPost(sb, name, createdLocationPrefix, pkProp);
        if ((crud & CrudOperation.Put) != 0)
            EmitUpdate(sb, name, pkType, pkRouteConstraint);
        // V#5: PATCH uses dedicated PatchXxxRequest (Optional<T?> fields) and PatchXxxHandler.
        // Domain Update method validates after merge; no API-level validator.
        if ((crud & CrudOperation.Patch) != 0)
            EmitPatch(sb, name, pkType, pkRouteConstraint);
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
        // V#11: ?sort accepts comma-separated fields (with optional leading '-' for
        // descending). The ReadService validates against a per-entity allowlist and
        // throws QueryValidationException → 400 for unknown fields.
        sb.AppendLine($"        group.MapGet(\"/\", async (I{name}ReadService reads, CancellationToken ct, int page = 1, int pageSize = 50, string? sort = null) =>");
        sb.AppendLine("        {");
        sb.AppendLine("            var (items, totalCount) = await reads.GetPagedAsync(page, pageSize, sort, ct).ConfigureAwait(false);");
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

    /// <summary>
    /// V#8: thin POST endpoint. Validation runs in <c>ValidationFilter&lt;TRequest&gt;</c>;
    /// request→command translation lives in <c>&lt;Entity&gt;Mappings.ToCommand()</c>.
    /// V#10: <paramref name="locationPrefix"/> is the concrete URL prefix used in the
    /// Created Location header — for UrlSegment versioning it embeds <c>/v1</c>.
    /// </summary>
    static void EmitPost(StringBuilder sb, string name, string locationPrefix, string pkProp)
    {
        sb.AppendLine($"        group.MapPost(\"/\", async (Create{name}Request request, Create{name}Handler handler, CancellationToken ct) =>");
        sb.AppendLine("        {");
        sb.AppendLine("            var entity = await handler.HandleAsync(request.ToCommand(), ct).ConfigureAwait(false);");
        sb.AppendLine($"            return Results.Created($\"{locationPrefix}/{{entity.{pkProp}}}\", entity.ToResponse());");
        sb.AppendLine("        })");
        sb.AppendLine($"        .AddEndpointFilter<ValidationFilter<Create{name}Request>>();");
        sb.AppendLine();
    }

    /// <summary>
    /// V#8: thin PUT endpoint. Validation runs in the filter; mapping lives in
    /// <c>&lt;Entity&gt;Mappings.ToCommand(id)</c>.
    /// </summary>
    static void EmitUpdate(StringBuilder sb, string name, string pkType, string pkRouteConstraint)
    {
        sb.AppendLine($"        group.MapPut(\"/{{id{pkRouteConstraint}}}\", async ({pkType} id, Update{name}Request request, Update{name}Handler handler, CancellationToken ct) =>");
        sb.AppendLine("        {");
        sb.AppendLine("            var entity = await handler.HandleAsync(request.ToCommand(id), ct).ConfigureAwait(false);");
        sb.AppendLine("            return entity is null ? Results.NotFound() : Results.Ok(entity.ToResponse());");
        sb.AppendLine("        })");
        sb.AppendLine($"        .AddEndpointFilter<ValidationFilter<Update{name}Request>>();");
        sb.AppendLine();
    }

    /// <summary>
    /// V#5/V#8 PATCH endpoint. Deserializes <c>Patch&lt;Entity&gt;Request</c>
    /// (Optional&lt;T?&gt; non-PK fields), maps via <c>request.ToCommand(id)</c>, calls
    /// <c>Patch&lt;Entity&gt;Handler</c>. No API-level validator — domain
    /// <c>Update</c> validates after the handler merges Optional with the existing
    /// entity. Lambda is now a single delegate call.
    /// </summary>
    static void EmitPatch(StringBuilder sb, string name, string pkType, string pkRouteConstraint)
    {
        sb.AppendLine($"        group.MapPatch(\"/{{id{pkRouteConstraint}}}\", async ({pkType} id, Patch{name}Request request, Patch{name}Handler handler, CancellationToken ct) =>");
        sb.AppendLine("        {");
        sb.AppendLine("            var entity = await handler.HandleAsync(request.ToCommand(id), ct).ConfigureAwait(false);");
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
