using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits per-entity I&lt;Entity&gt;Commands interfaces into
/// Application/Features/&lt;Plural&gt;/Abstractions/.
/// </summary>
public sealed class FeatureCommandsInterfaceEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var crud = ctx.Config.Crud;
        if ((crud & (CrudOperation.Post | CrudOperation.Put | CrudOperation.Patch | CrudOperation.Delete)) == 0)
            return System.Array.Empty<EmittedFile>();

        var list = new List<EmittedFile>();
        var project = ctx.Config.ProjectName;
        var dtosNs = CleanLayout.ApplicationDtosNamespace(project);

        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.IsJoinTable) continue;
            if (!entity.HasPrimaryKey) continue;

            var name = entity.EntityTypeName;
            var ns = CleanLayout.ApplicationFeatureAbstractionsNamespace(project, name);
            var featureNs = CleanLayout.ApplicationFeatureNamespace(project, name);
            var pkType = PkType(entity.Table);

            var sb = new StringBuilder();
            sb.AppendLine($"using {dtosNs};");
            sb.AppendLine($"using {featureNs};");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns};");
            sb.AppendLine();
            sb.AppendLine($"public interface I{name}Commands");
            sb.AppendLine("{");
            if ((crud & CrudOperation.Post) != 0)
                sb.AppendLine($"    Task<{name}Dto> CreateAsync(Create{name}Command command, CancellationToken ct);");
            if ((crud & CrudOperation.Put) != 0)
                sb.AppendLine($"    Task<{name}Dto?> UpdateAsync(Update{name}Command command, CancellationToken ct);");
            if ((crud & CrudOperation.Patch) != 0)
                sb.AppendLine($"    Task<{name}Dto?> PatchAsync(Patch{name}Command command, CancellationToken ct);");
            if ((crud & CrudOperation.Delete) != 0)
                sb.AppendLine($"    Task<bool> DeleteAsync({pkType} id, CancellationToken ct);");
            sb.AppendLine("}");

            var path = CleanLayout.ApplicationFeatureAbstractionsPath(project, name, $"I{name}Commands");
            list.Add(new EmittedFile(path, sb.ToString()));
        }
        return list;
    }

    static string PkType(Table table)
    {
        var pk = table.PrimaryKey!;
        if (pk.ColumnNames.Count == 1)
        {
            var col = table.Columns.First(c =>
                string.Equals(c.Name, pk.ColumnNames[0], System.StringComparison.OrdinalIgnoreCase));
            return SqlTypeMap.ToCs(col.ClrType);
        }
        var parts = pk.ColumnNames.Select(n =>
        {
            var c = table.Columns.First(col =>
                string.Equals(col.Name, n, System.StringComparison.OrdinalIgnoreCase));
            return SqlTypeMap.ToCs(c.ClrType);
        });
        return "(" + string.Join(", ", parts) + ")";
    }
}
