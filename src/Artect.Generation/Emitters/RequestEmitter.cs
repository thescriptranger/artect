using System.Collections.Generic;
using System.Linq;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;
using Artect.Templating;

namespace Artect.Generation.Emitters;

public sealed class RequestEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var template = TemplateParser.Parse(ctx.Templates.Load("Request.cs.artect"));
        var list = new List<EmittedFile>();
        var ns = $"{CleanLayout.SharedNamespace(ctx.Config.ProjectName)}.Requests";

        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.ShouldSkip(EntityClassification.AggregateRoot)) continue;

            var pkCols = entity.Table.PrimaryKey!.ColumnNames
                .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

            // Create request: skip PK (identity) columns
            var createProps = BuildProperties(entity, pkCols, isCreate: true, ctx.NamingCorrections);
            var createData = new
            {
                Namespace = ns,
                Kind = "Create",
                EntityName = entity.EntityTypeName,
                Properties = createProps,
            };
            var createRendered = Renderer.Render(template, createData);
            var createPath = CleanLayout.SharedRequestPath(ctx.Config.ProjectName, entity.EntityTypeName, "Create");
            list.Add(new EmittedFile(createPath, createRendered));

            // Update request: include PK columns
            var updateProps = BuildProperties(entity, pkCols, isCreate: false, ctx.NamingCorrections);
            var updateData = new
            {
                Namespace = ns,
                Kind = "Update",
                EntityName = entity.EntityTypeName,
                Properties = updateProps,
            };
            var updateRendered = Renderer.Render(template, updateData);
            var updatePath = CleanLayout.SharedRequestPath(ctx.Config.ProjectName, entity.EntityTypeName, "Update");
            list.Add(new EmittedFile(updatePath, updateRendered));
        }

        return list;
    }

    static IReadOnlyList<object> BuildProperties(NamedEntity entity, HashSet<string> pkCols, bool isCreate, System.Collections.Generic.IReadOnlyDictionary<string, string> corrections)
    {
        var result = new List<object>();
        foreach (var c in entity.Table.Columns)
        {
            // Drop columns flagged Ignored in artect.yaml columnMetadata.
            if (entity.ColumnHasFlag(c.Name, ColumnMetadata.Ignored)) continue;
            // For Create requests skip server-generated / identity PK columns
            if (isCreate && pkCols.Contains(c.Name) && c.IsServerGenerated) continue;

            var csBase = SqlTypeMap.ToCs(c.ClrType);
            string clrTypeWithNullability;
            if (c.IsNullable && SqlTypeMap.IsValueType(c.ClrType))
                clrTypeWithNullability = csBase + "?";
            else if (c.IsNullable && c.ClrType == ClrType.String)
                clrTypeWithNullability = csBase + "?";
            else
                clrTypeWithNullability = csBase;

            var required = !c.IsNullable && c.ClrType == ClrType.String;
            var hasStringLength = c.ClrType == ClrType.String && c.MaxLength is > 0;
            var stringLength = hasStringLength ? c.MaxLength!.Value : 0;
            var initializer = !c.IsNullable && c.ClrType == ClrType.String ? " = string.Empty;" : string.Empty;

            result.Add(new
            {
                PropertyName = EntityNaming.PropertyName(c, corrections),
                ClrTypeWithNullability = clrTypeWithNullability,
                Initializer = initializer,
                Required = required,
                HasStringLength = hasStringLength,
                StringLength = stringLength,
                HasRange = false,
                RangeMin = string.Empty,
                RangeMax = string.Empty,
            });
        }
        return result;
    }
}
