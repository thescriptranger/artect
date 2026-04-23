using System.Collections.Generic;
using System.Linq;
using Artect.Core.Schema;
using Microsoft.Data.SqlClient;

namespace Artect.Introspection.Readers;

public static class CheckConstraintsReader
{
    public static Dictionary<(string, string), IReadOnlyList<CheckConstraint>> Read(SqlConnection conn, IReadOnlyList<string> schemas)
    {
        var schemaList = "'" + string.Join("','", schemas) + "'";
        var rows = new List<CheckConstraint>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
SELECT sch.name, t.name, cc.name, col.name, cc.definition
FROM sys.check_constraints cc
JOIN sys.tables t ON t.object_id = cc.parent_object_id
JOIN sys.schemas sch ON sch.schema_id = t.schema_id
LEFT JOIN sys.columns col ON col.object_id = cc.parent_object_id AND col.column_id = cc.parent_column_id
WHERE sch.name IN ({schemaList})
ORDER BY sch.name, t.name, cc.name;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            rows.Add(new CheckConstraint(
                Name: r.GetString(2),
                TableSchema: r.GetString(0),
                TableName: r.GetString(1),
                ColumnName: r.IsDBNull(3) ? null : r.GetString(3),
                Expression: r.GetString(4)));
        }
        return rows.GroupBy(x => (x.TableSchema, x.TableName))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CheckConstraint>)g.OrderBy(c => c.Name).ToList());
    }
}
