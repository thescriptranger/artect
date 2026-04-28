using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits per-entity I&lt;Entity&gt;ReadService interfaces into
/// Application/Features/&lt;Plural&gt;/Abstractions/. Read-side projections that
/// never expose the domain entity directly.
/// </summary>
public sealed class ReadServiceInterfaceEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var crud = ctx.Config.Crud;
        if ((crud & (CrudOperation.GetList | CrudOperation.GetById)) == 0)
            return System.Array.Empty<EmittedFile>();

        var list = new List<EmittedFile>();
        var project = ctx.Config.ProjectName;
        var dtosNs = CleanLayout.ApplicationDtosNamespace(project);

        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.ShouldSkip(EntityClassification.AggregateRoot, EntityClassification.ReadModel)) continue;

            var name   = entity.EntityTypeName;
            var ns     = CleanLayout.ApplicationFeatureAbstractionsNamespace(project, name);
            var pkType = PkType(entity.Table);

            var sb = new StringBuilder();
            sb.AppendLine($"using {dtosNs};");
            sb.AppendLine($"using {CleanLayout.ApplicationAbstractionsNamespace(project)};");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns};");
            sb.AppendLine();
            sb.AppendLine($"public interface I{name}ReadService : IReadService");
            sb.AppendLine("{");
            if ((crud & CrudOperation.GetList) != 0)
                sb.AppendLine($"    Task<(IReadOnlyList<{name}Dto> Items, int TotalCount)> GetPagedAsync(int page, int pageSize, string? sort, CancellationToken ct);");
            if ((crud & CrudOperation.GetById) != 0)
                sb.AppendLine($"    Task<{name}Dto?> GetByIdAsync({pkType} id, CancellationToken ct);");
            sb.AppendLine("}");

            var path = CleanLayout.ApplicationFeatureAbstractionsPath(project, name, $"I{name}ReadService");
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
