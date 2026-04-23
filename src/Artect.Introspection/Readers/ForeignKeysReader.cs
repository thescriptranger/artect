using System.Collections.Generic;
using System.Linq;
using Artect.Core.Schema;
using Microsoft.Data.SqlClient;

namespace Artect.Introspection.Readers;

public static class ForeignKeysReader
{
    public static Dictionary<(string, string), IReadOnlyList<ForeignKey>> Read(SqlConnection conn, IReadOnlyList<string> schemas)
    {
        var schemaList = "'" + string.Join("','", schemas) + "'";
        var rows = new List<(string FkName, string FromSch, string FromTbl, string FromCol, string ToSch, string ToTbl, string ToCol, int Ord, string DeleteAction, string UpdateAction)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
SELECT fk.name,
       ps.name AS from_schema, pt.name AS from_table, pc.name AS from_column,
       rs.name AS to_schema, rt.name AS to_table, rc.name AS to_column,
       fkc.constraint_column_id,
       fk.delete_referential_action_desc, fk.update_referential_action_desc
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.tables pt ON pt.object_id = fk.parent_object_id
JOIN sys.schemas ps ON ps.schema_id = pt.schema_id
JOIN sys.columns pc ON pc.object_id = pt.object_id AND pc.column_id = fkc.parent_column_id
JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
JOIN sys.columns rc ON rc.object_id = rt.object_id AND rc.column_id = fkc.referenced_column_id
WHERE ps.name IN ({schemaList})
ORDER BY ps.name, pt.name, fk.name, fkc.constraint_column_id;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                rows.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                          r.GetString(4), r.GetString(5), r.GetString(6), r.GetInt32(7),
                          r.GetString(8), r.GetString(9)));
        }

        var grouped = rows.GroupBy(x => (x.FkName, x.FromSch, x.FromTbl, x.ToSch, x.ToTbl, x.DeleteAction, x.UpdateAction));
        var byTable = new Dictionary<(string, string), List<ForeignKey>>();
        foreach (var g in grouped)
        {
            var pairs = g.OrderBy(x => x.Ord)
                .Select(x => new ForeignKeyColumnPair(x.FromCol, x.ToCol))
                .ToList();
            var fk = new ForeignKey(
                Name: g.Key.FkName,
                FromSchema: g.Key.FromSch, FromTable: g.Key.FromTbl,
                ToSchema: g.Key.ToSch, ToTable: g.Key.ToTbl,
                ColumnPairs: pairs,
                OnDelete: MapAction(g.Key.DeleteAction),
                OnUpdate: MapAction(g.Key.UpdateAction));
            var tableKey = (g.Key.FromSch, g.Key.FromTbl);
            if (!byTable.TryGetValue(tableKey, out var list)) { list = new List<ForeignKey>(); byTable[tableKey] = list; }
            list.Add(fk);
        }
        return byTable.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<ForeignKey>)kv.Value.OrderBy(f => f.Name).ToList());
    }

    static ForeignKeyAction MapAction(string desc) => desc.ToUpperInvariant() switch
    {
        "CASCADE" => ForeignKeyAction.Cascade,
        "SET_NULL" => ForeignKeyAction.SetNull,
        "SET_DEFAULT" => ForeignKeyAction.SetDefault,
        _ => ForeignKeyAction.NoAction
    };
}
