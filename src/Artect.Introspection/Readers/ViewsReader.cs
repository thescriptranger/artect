using System.Collections.Generic;
using System.Linq;
using Artect.Core.Schema;
using Microsoft.Data.SqlClient;

namespace Artect.Introspection.Readers;

public static class ViewsReader
{
    public static IReadOnlyList<View> Read(SqlConnection conn, IReadOnlyList<string> schemas)
    {
        var schemaList = "'" + string.Join("','", schemas) + "'";
        var views = new List<(string Sch, string Nm)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
SELECT TABLE_SCHEMA, TABLE_NAME
FROM INFORMATION_SCHEMA.VIEWS
WHERE TABLE_SCHEMA IN ({schemaList})
ORDER BY TABLE_SCHEMA, TABLE_NAME;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) views.Add((r.GetString(0), r.GetString(1)));
        }
        var colsByView = new Dictionary<(string, string), List<Column>>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, ORDINAL_POSITION, DATA_TYPE, IS_NULLABLE,
       CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA IN ({schemaList})
  AND TABLE_NAME IN (SELECT TABLE_NAME FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_SCHEMA IN ({schemaList}))
ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var key = (r.GetString(0), r.GetString(1));
                if (!colsByView.TryGetValue(key, out var list)) { list = new List<Column>(); colsByView[key] = list; }
                var sqlType = r.GetString(4);
                list.Add(new Column(
                    Name: r.GetString(2), OrdinalPosition: r.GetInt32(3),
                    SqlType: sqlType, ClrType: SqlTypeMap.ToClr(sqlType),
                    IsNullable: r.GetString(5) == "YES",
                    IsIdentity: false, IsComputed: false, IsServerGenerated: false,
                    MaxLength: r.IsDBNull(6) ? null : r.GetInt32(6),
                    Precision: r.IsDBNull(7) ? null : (int?)r.GetByte(7),
                    Scale: r.IsDBNull(8) ? null : (int?)r.GetInt32(8),
                    DefaultValue: null));
            }
        }
        return views.Select(v => new View(v.Sch, v.Nm,
            colsByView.TryGetValue((v.Sch, v.Nm), out var c) ? (IReadOnlyList<Column>)c : new List<Column>())).ToList();
    }
}
