using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits per-entity I&lt;Entity&gt;Repository write-side abstractions into
/// Application/Features/&lt;Plural&gt;/Abstractions/. Phase 2 replaces the old
/// I&lt;Entity&gt;Queries + I&lt;Entity&gt;Commands pair. Repositories only stage
/// changes against the DbContext — handlers commit through IUnitOfWork.
/// </summary>
public sealed class RepositoryInterfaceEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var crud = ctx.Config.Crud;
        var writeNeeded = (crud & (CrudOperation.Post | CrudOperation.Put | CrudOperation.Patch | CrudOperation.Delete)) != 0;
        var readNeeded  = (crud & CrudOperation.GetById) != 0;
        if (!writeNeeded && !readNeeded) return System.Array.Empty<EmittedFile>();

        var list = new List<EmittedFile>();
        var project   = ctx.Config.ProjectName;
        var entityNs  = $"{CleanLayout.DomainNamespace(project)}.Entities";

        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.ShouldSkip(EntityClassification.AggregateRoot)) continue;

            var name   = entity.EntityTypeName;
            var ns     = CleanLayout.ApplicationFeatureAbstractionsNamespace(project, name);
            var pkType = PkType(entity.Table);

            var sb = new StringBuilder();
            sb.AppendLine($"using {entityNs};");
            sb.AppendLine($"using {CleanLayout.ApplicationAbstractionsNamespace(project)};");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns};");
            sb.AppendLine();
            sb.AppendLine($"public interface I{name}Repository : IRepository");
            sb.AppendLine("{");
            sb.AppendLine($"    Task<{name}?> GetByIdAsync({pkType} id, CancellationToken ct);");
            sb.AppendLine($"    Task<bool> ExistsAsync({pkType} id, CancellationToken ct);");
            foreach (var (prop, type) in SingleColumnUniques(entity.Table, ctx.NamingCorrections))
                sb.AppendLine($"    Task<bool> ExistsBy{prop}Async({type} value, CancellationToken ct);");
            if ((crud & CrudOperation.Post) != 0)
                sb.AppendLine($"    Task AddAsync({name} entity, CancellationToken ct);");
            // V#3: ApplyChanges removed. Update/Patch handlers call domain methods on
            // the loaded aggregate; EF persists tracked mutations on commit.
            if ((crud & CrudOperation.Delete) != 0)
                sb.AppendLine($"    void Remove({name} entity);");
            sb.AppendLine("}");

            var path = CleanLayout.ApplicationFeatureAbstractionsPath(project, name, $"I{name}Repository");
            list.Add(new EmittedFile(path, sb.ToString()));
        }
        return list;
    }

    /// <summary>
    /// Yields (PropertyName, CsType) for every single-column UNIQUE constraint on the
    /// table whose column is NOT part of the primary key. Multi-column uniques and
    /// PK-overlapping uniques are skipped per Phase 2 spec §10 mitigation.
    /// </summary>
    internal static IEnumerable<(string PropertyName, string CsType)> SingleColumnUniques(
        Table table, IReadOnlyDictionary<string, string> corrections)
    {
        var pkCols = table.PrimaryKey is null
            ? new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(table.PrimaryKey.ColumnNames, System.StringComparer.OrdinalIgnoreCase);

        foreach (var uc in table.UniqueConstraints)
        {
            if (uc.ColumnNames.Count != 1) continue;
            var colName = uc.ColumnNames[0];
            if (pkCols.Contains(colName)) continue;

            var col = table.Columns.FirstOrDefault(c =>
                string.Equals(c.Name, colName, System.StringComparison.OrdinalIgnoreCase));
            if (col is null) continue;

            yield return (EntityNaming.PropertyName(col, corrections), SqlTypeMap.ToCs(col.ClrType));
        }
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
