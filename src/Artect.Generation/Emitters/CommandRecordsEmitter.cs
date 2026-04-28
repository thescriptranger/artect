using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits positional-record Create/Update/Patch command types per enabled write CRUD op into
/// Application/Features/&lt;Plural&gt;/. Delete is represented by primitive PK args (no record).
/// </summary>
public sealed class CommandRecordsEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var crud = ctx.Config.Crud;
        if ((crud & (CrudOperation.Post | CrudOperation.Put | CrudOperation.Patch)) == 0)
            return System.Array.Empty<EmittedFile>();

        var list = new List<EmittedFile>();

        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.ShouldSkip(EntityClassification.AggregateRoot)) continue;

            var name = entity.EntityTypeName;
            var nonServerGen = entity.Table.Columns.Where(c => !c.IsServerGenerated).ToList();
            var allCols = entity.Table.Columns.ToList();

            if ((crud & CrudOperation.Post) != 0)
                list.Add(BuildRecord(ctx, entity, $"Create{name}Command", nonServerGen));
            if ((crud & CrudOperation.Put) != 0)
                list.Add(BuildRecord(ctx, entity, $"Update{name}Command", allCols));
            if ((crud & CrudOperation.Patch) != 0)
                list.Add(BuildRecord(ctx, entity, $"Patch{name}Command", allCols));
        }
        return list;
    }

    static EmittedFile BuildRecord(EmitterContext ctx, NamedEntity entity, string recordName, IReadOnlyList<Column> cols)
    {
        var project = ctx.Config.ProjectName;
        var ns = CleanLayout.ApplicationFeatureNamespace(project, entity.EntityTypeName);
        var corrections = ctx.NamingCorrections;

        var sb = new StringBuilder();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"public sealed record {recordName}(");
        for (int i = 0; i < cols.Count; i++)
        {
            var col = cols[i];
            var cs = SqlTypeMap.ToCs(col.ClrType);
            var propType = col.IsNullable && (SqlTypeMap.IsValueType(col.ClrType) || col.ClrType == ClrType.String)
                ? cs + "?"
                : cs;
            var propName = EntityNaming.PropertyName(col, corrections);
            var terminator = i == cols.Count - 1 ? ");" : ",";
            sb.AppendLine($"    {propType} {propName}{terminator}");
        }

        var path = CleanLayout.ApplicationFeaturePath(project, entity.EntityTypeName, recordName);
        return new EmittedFile(path, sb.ToString());
    }
}
