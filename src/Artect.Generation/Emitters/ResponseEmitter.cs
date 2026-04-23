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

            var data = new
            {
                Namespace = ns,
                EntityName = entity.EntityTypeName,
                Properties = props,
            };
            var rendered = Renderer.Render(template, data);
            var path = CleanLayout.SharedResponsePath(ctx.Config.ProjectName, entity.EntityTypeName);
            list.Add(new EmittedFile(path, rendered));
        }

        return list;
    }
}
