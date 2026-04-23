using System.Collections.Generic;
using System.Linq;
using Artect.Core.Schema;
using Artect.Naming;
using Artect.Templating;

namespace Artect.Generation.Emitters;

public sealed class ResponseEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var template = TemplateParser.Parse(ctx.Templates.Load("Response.cs.artect"));
        var list = new List<EmittedFile>();
        var ns = $"{CleanLayout.SharedNamespace(ctx.Config.ProjectName)}.Responses";
        var includeChildren = ctx.Config.IncludeChildCollectionsInResponses;

        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.IsJoinTable) continue;
            if (!entity.HasPrimaryKey) continue;

            var props = entity.Table.Columns.Select(c =>
            {
                var csBase = SqlTypeMap.ToCs(c.ClrType);
                string clrTypeWithNullability;
                if (c.IsNullable && SqlTypeMap.IsValueType(c.ClrType))
                    clrTypeWithNullability = csBase + "?";
                else if (c.IsNullable && c.ClrType == ClrType.String)
                    clrTypeWithNullability = csBase + "?";
                else
                    clrTypeWithNullability = csBase;

                return new
                {
                    PropertyName = EntityNaming.PropertyName(c, ctx.NamingCorrections),
                    ClrTypeWithNullability = clrTypeWithNullability,
                    Initializer = !c.IsNullable && c.ClrType == ClrType.String ? " = string.Empty;" : string.Empty,
                };
            }).ToList();

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
                Namespace = ns,
                EntityName = entity.EntityTypeName,
                Properties = props,
                HasChildCollections = includeChildren && childCollections.Count > 0,
                ChildCollections = childCollections,
            };
            var rendered = Renderer.Render(template, data);
            var path = CleanLayout.SharedResponsePath(ctx.Config.ProjectName, entity.EntityTypeName);
            list.Add(new EmittedFile(path, rendered));
        }

        return list;
    }
}
