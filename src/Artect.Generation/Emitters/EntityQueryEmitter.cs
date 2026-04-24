using System.Collections.Generic;
using System.Linq;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;
using Artect.Templating;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits per-entity <c>Get&lt;Entity&gt;ByIdQuery</c> and <c>List&lt;Plural&gt;Query</c> positional-record
/// queries into <c>&lt;Project&gt;.Application.Queries</c>. Each query implements <c>IQuery&lt;TPayload&gt;</c>.
/// Tables with no PK and views get a List query only.
/// </summary>
public sealed class EntityQueryEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var template = TemplateParser.Parse(ctx.Templates.Load("EntityQuery.cs.artect"));
        var list = new List<EmittedFile>();

        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.IsJoinTable) continue;

            if (entity.HasPrimaryKey && ctx.Config.Crud.HasFlag(CrudOperation.GetById))
                list.Add(BuildGetByIdQuery(ctx, template, entity));

            if (ctx.Config.Crud.HasFlag(CrudOperation.GetList))
                list.Add(BuildListQuery(ctx, template, entity.EntityTypeName, entity.DbSetPropertyName));
        }

        // Views — list-only
        foreach (var view in ctx.Graph.Views)
        {
            var typeName = CasingHelper.ToPascalCase(Pluralizer.Singularize(view.Name), ctx.NamingCorrections);
            var plural   = CasingHelper.ToPascalCase(Pluralizer.Pluralize(Pluralizer.Singularize(view.Name)), ctx.NamingCorrections);
            if (ctx.Config.Crud.HasFlag(CrudOperation.GetList))
                list.Add(BuildListQuery(ctx, template, typeName, plural));
        }

        return list;
    }

    static EmittedFile BuildGetByIdQuery(EmitterContext ctx, Artect.Templating.Ast.TemplateDocument template, NamedEntity entity)
    {
        var queryName = $"Get{entity.EntityTypeName}ByIdQuery";
        var pk = entity.Table.PrimaryKey!;
        var pkNames = pk.ColumnNames.ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        var pkCols = entity.Table.Columns.Where(c => pkNames.Contains(c.Name)).ToList();
        var argList = "(" + string.Join(", ", pkCols.Select(c => $"{SqlTypeMap.ToCs(c.ClrType)} {Artect.Naming.EntityNaming.PropertyName(c, ctx.NamingCorrections)}")) + ")";
        var data = new
        {
            Namespace = CleanLayout.ApplicationQueriesNamespace(ctx.Config.ProjectName),
            CommonNamespace = CleanLayout.ApplicationCommonNamespace(ctx.Config.ProjectName),
            ModelsNamespace = CleanLayout.ApplicationDtosNamespace(ctx.Config.ProjectName),
            HasModelsUsing = true,
            QueryName = queryName,
            PkArgList = argList,
            PayloadType = $"{entity.EntityTypeName}Dto",
        };
        return new EmittedFile(
            CleanLayout.ApplicationQueriesPath(ctx.Config.ProjectName, queryName),
            Renderer.Render(template, data));
    }

    static EmittedFile BuildListQuery(EmitterContext ctx, Artect.Templating.Ast.TemplateDocument template, string entityTypeName, string plural)
    {
        var queryName = $"List{plural}Query";
        var data = new
        {
            Namespace = CleanLayout.ApplicationQueriesNamespace(ctx.Config.ProjectName),
            CommonNamespace = CleanLayout.ApplicationCommonNamespace(ctx.Config.ProjectName),
            ModelsNamespace = CleanLayout.ApplicationDtosNamespace(ctx.Config.ProjectName),
            HasModelsUsing = true,
            QueryName = queryName,
            PkArgList = "(int Page, int PageSize)",
            PayloadType = $"PagedResult<{entityTypeName}Dto>",
        };
        return new EmittedFile(
            CleanLayout.ApplicationQueriesPath(ctx.Config.ProjectName, queryName),
            Renderer.Render(template, data));
    }
}
