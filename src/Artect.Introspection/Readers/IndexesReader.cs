using System.Collections.Generic;
using System.Linq;
using Artect.Core.Schema;
using Microsoft.Data.SqlClient;
using Index = Artect.Core.Schema.Index;

namespace Artect.Introspection.Readers;

public static class IndexesReader
{
    public static Dictionary<(string, string), IReadOnlyList<Index>> Read(SqlConnection conn, IReadOnlyList<string> schemas)
    {
        var schemaList = "'" + string.Join("','", schemas) + "'";
        var rows = new List<(string Sch, string Tbl, string Name, bool Unique, bool Clustered, string Col, int Ord, bool Included)>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
SELECT sch.name, t.name, i.name, i.is_unique, (CASE WHEN i.type_desc = 'CLUSTERED' THEN 1 ELSE 0 END),
       c.name, ic.key_ordinal, ic.is_included_column
FROM sys.indexes i
JOIN sys.tables t ON t.object_id = i.object_id
JOIN sys.schemas sch ON sch.schema_id = t.schema_id
JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE i.is_primary_key = 0 AND i.is_hypothetical = 0 AND i.name IS NOT NULL
  AND sch.name IN ({schemaList})
ORDER BY sch.name, t.name, i.name, ic.key_ordinal, c.name;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetBoolean(3), r.GetInt32(4) == 1,
                      r.GetString(5), r.GetByte(6), r.GetBoolean(7)));
        var grouped = rows
            .GroupBy(x => (x.Sch, x.Tbl, x.Name, x.Unique, x.Clustered));
        var byTable = new Dictionary<(string, string), List<Index>>();
        foreach (var g in grouped)
        {
            var keys = g.Where(x => !x.Included).OrderBy(x => x.Ord).Select(x => x.Col).ToList();
            var includes = g.Where(x => x.Included).Select(x => x.Col).OrderBy(s => s).ToList();
            var idx = new Index(g.Key.Name, g.Key.Unique, g.Key.Clustered, keys, includes);
            var tableKey = (g.Key.Sch, g.Key.Tbl);
            if (!byTable.TryGetValue(tableKey, out var list)) { list = new List<Index>(); byTable[tableKey] = list; }
            list.Add(idx);
        }
        return byTable.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Index>)kv.Value.OrderBy(i => i.Name).ToList());
    }
}
