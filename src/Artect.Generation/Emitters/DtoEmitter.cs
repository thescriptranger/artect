using System.Collections.Generic;
using System.Linq;
using Artect.Core.Schema;
using Artect.Templating;

namespace Artect.Generation.Emitters;

public sealed class DtoEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var template = TemplateParser.Parse(ctx.Templates.Load("Dto.cs.artect"));
        var list = new List<EmittedFile>();
        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.IsJoinTable) continue;
            if (!entity.HasPrimaryKey) continue;
            var data = new
            {
                Namespace = $"{CleanLayout.ApplicationNamespace(ctx.Config.ProjectName)}.Dtos",
                EntityName = entity.EntityTypeName,
                Columns = entity.Table.Columns.Select(c => new
                {
                    ClrTypeWithNullability = ClrTypeString(c),
                    PropertyName = Artect.Naming.EntityNaming.PropertyName(c),
                    Initializer = c.ClrType == ClrType.String && !c.IsNullable ? " = string.Empty;" : string.Empty,
                }).ToList(),
            };
            var rendered = Renderer.Render(template, data);
            var path = CleanLayout.DtoPath(ctx.Config.ProjectName, entity.EntityTypeName);
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
