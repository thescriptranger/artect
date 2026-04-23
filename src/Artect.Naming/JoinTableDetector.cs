using System.Collections.Generic;
using System.Linq;
using Artect.Core.Schema;

namespace Artect.Naming;

public static class JoinTableDetector
{
    public static bool IsJoinTable(Table t)
    {
        if (t.PrimaryKey is null) return false;
        if (t.Columns.Count < 2) return false;
        var fkCols = t.ForeignKeys.SelectMany(fk => fk.ColumnPairs.Select(p => p.FromColumn)).ToHashSet();
        var nonFkCols = t.Columns.Where(c => !fkCols.Contains(c.Name)).ToList();
        return t.ForeignKeys.Count >= 2 && nonFkCols.Count == 0;
    }

    public static IReadOnlyList<Table> NonJoinTables(SchemaGraph graph) =>
        graph.Tables.Where(t => !IsJoinTable(t)).ToList();
}
