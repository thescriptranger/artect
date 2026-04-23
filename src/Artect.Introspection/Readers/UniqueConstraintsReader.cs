using System.Collections.Generic;
using System.Linq;
using Artect.Core.Schema;
using Microsoft.Data.SqlClient;

namespace Artect.Introspection.Readers;

public static class UniqueConstraintsReader
{
    public static Dictionary<(string, string), IReadOnlyList<UniqueConstraint>> Read(SqlConnection conn, IReadOnlyList<string> schemas)
    {
        var schemaList = "'" + string.Join("','", schemas) + "'";
        var rows = new List<(string Sch, string Tbl, string Name, string Col, int Ord)>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
SELECT tc.TABLE_SCHEMA, tc.TABLE_NAME, tc.CONSTRAINT_NAME, kcu.COLUMN_NAME, kcu.ORDINAL_POSITION
FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
  ON tc.CONSTRAINT_SCHEMA = kcu.CONSTRAINT_SCHEMA AND tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
WHERE tc.CONSTRAINT_TYPE = 'UNIQUE' AND tc.TABLE_SCHEMA IN ({schemaList})
ORDER BY tc.TABLE_SCHEMA, tc.TABLE_NAME, tc.CONSTRAINT_NAME, kcu.ORDINAL_POSITION;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4)));
        return rows
            .GroupBy(x => (x.Sch, x.Tbl, x.Name))
            .GroupBy(g => (g.Key.Sch, g.Key.Tbl))
            .ToDictionary(
                gg => gg.Key,
                gg => (IReadOnlyList<UniqueConstraint>)gg.Select(g => new UniqueConstraint(
                    g.Key.Name,
                    g.OrderBy(x => x.Ord).Select(x => x.Col).ToList())).ToList());
    }
}
