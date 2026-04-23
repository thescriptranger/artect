using System.Collections.Generic;
using System.Linq;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;
using Artect.Templating;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits per-entity-per-operation <c>&lt;Op&gt;&lt;Entity&gt;Command</c> records into
/// <c>&lt;Project&gt;.Application.Commands</c>. Each command implements <c>ICommand&lt;TPayload&gt;</c>.
/// </summary>
public sealed class EntityCommandEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var template = TemplateParser.Parse(ctx.Templates.Load("EntityCommand.cs.artect"));
        var list = new List<EmittedFile>();

        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.IsJoinTable) continue;
            if (!entity.HasPrimaryKey) continue;

            var pkNames = entity.Table.PrimaryKey!.ColumnNames
                .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

            var pkCols = entity.Table.Columns.Where(c => pkNames.Contains(c.Name)).ToList();
            var nonServerGenColumns = entity.Table.Columns.Where(c => !c.IsServerGenerated).ToList();

            if (ctx.Config.Crud.HasFlag(CrudOperation.Post))
                list.Add(Build(ctx, template, entity, "Create", nonServerGenColumns, payload: $"{entity.EntityTypeName}Model"));

            if (ctx.Config.Crud.HasFlag(CrudOperation.Put))
                list.Add(Build(ctx, template, entity, "Update", entity.Table.Columns.ToList(), payload: "Unit"));

            if (ctx.Config.Crud.HasFlag(CrudOperation.Patch))
                list.Add(Build(ctx, template, entity, "Patch", entity.Table.Columns.ToList(), payload: "Unit"));

            if (ctx.Config.Crud.HasFlag(CrudOperation.Delete))
                list.Add(Build(ctx, template, entity, "Delete", pkCols, payload: "Unit"));
        }
        return list;
    }

    static EmittedFile Build(EmitterContext ctx, Artect.Templating.Ast.TemplateDocument template, NamedEntity entity, string op, IReadOnlyList<Column> columns, string payload)
    {
        var commandName = $"{op}{entity.EntityTypeName}Command";
        var data = new
        {
            Namespace = CleanLayout.ApplicationCommandsNamespace(ctx.Config.ProjectName),
            CommonNamespace = CleanLayout.ApplicationCommonNamespace(ctx.Config.ProjectName),
            CommandName = commandName,
            PayloadType = payload,
            Properties = columns.Select(c => new
            {
                ClrTypeWithNullability = ClrTypeString(c),
                PropertyName = Artect.Naming.EntityNaming.PropertyName(c),
                Initializer = c.ClrType == ClrType.String && !c.IsNullable ? " = default!;" : string.Empty,
            }).ToList(),
        };
        return new EmittedFile(
            CleanLayout.ApplicationCommandsPath(ctx.Config.ProjectName, commandName),
            Renderer.Render(template, data));
    }

    static string ClrTypeString(Column c)
    {
        var cs = SqlTypeMap.ToCs(c.ClrType);
        if (c.IsNullable && SqlTypeMap.IsValueType(c.ClrType)) return cs + "?";
        if (c.IsNullable && c.ClrType == ClrType.String) return cs + "?";
        return cs;
    }
}
