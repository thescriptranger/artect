using System.Collections.Generic;
using System.Linq;
using Artect.Core.Schema;
using Artect.Naming;
using Artect.Templating;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits <c>&lt;Entity&gt;Model</c> records into <c>&lt;Project&gt;.Application.Models</c>.
/// One record per entity with a PK; join-tables skipped. The Model is the Application-internal
/// representation — repositories project to it and interactors return it. Api layer (Phase D)
/// maps Model→Response at the boundary.
/// </summary>
public sealed class EntityModelEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var template = TemplateParser.Parse(ctx.Templates.Load("EntityModel.cs.artect"));
        var list = new List<EmittedFile>();
        var includeChildren = ctx.Config.IncludeChildCollectionsInResponses;

        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.IsJoinTable) continue;
            if (!entity.HasPrimaryKey) continue;

            var childCollections = includeChildren
                ? entity.CollectionNavigations
                    .Select(n => new
                    {
                        TypeName = n.TargetEntityTypeName,
                        PropertyName = n.PropertyName,
                    })
                    .ToList()
                : new();

            var data = new
            {
                Namespace = CleanLayout.ApplicationModelsNamespace(ctx.Config.ProjectName),
                EntityName = entity.EntityTypeName,
                Columns = entity.Table.Columns.Select(c => new
                {
                    ClrTypeWithNullability = ClrTypeString(c),
                    PropertyName = Artect.Naming.EntityNaming.PropertyName(c, ctx.NamingCorrections),
                    Initializer = c.ClrType == ClrType.String && !c.IsNullable ? " = default!;" : string.Empty,
                }).ToList(),
                HasChildCollections = includeChildren && childCollections.Count > 0,
                ChildCollections = childCollections,
            };
            var rendered = Renderer.Render(template, data);
            var path = CleanLayout.ApplicationModelsPath(ctx.Config.ProjectName, $"{entity.EntityTypeName}Model");
            list.Add(new EmittedFile(path, rendered));
        }
        return list;
    }

    static string ClrTypeString(Column c)
    {
        var cs = SqlTypeMap.ToCs(c.ClrType);
        if (c.IsNullable && SqlTypeMap.IsValueType(c.ClrType)) return cs + "?";
        if (c.IsNullable && c.ClrType == ClrType.String) return cs + "?";
        return cs;
    }
}
