using System.Collections.Generic;
using Artect.Core.Schema;
using Microsoft.Data.SqlClient;

namespace Artect.Introspection.Readers;

public static class SequencesReader
{
    public static IReadOnlyList<Sequence> Read(SqlConnection conn, IReadOnlyList<string> schemas)
    {
        var schemaList = "'" + string.Join("','", schemas) + "'";
        var list = new List<Sequence>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
SELECT sch.name AS schema_name, s.name AS sequence_name, tp.name AS data_type,
       CAST(s.start_value AS bigint), CAST(s.increment AS bigint),
       CAST(s.minimum_value AS bigint), CAST(s.maximum_value AS bigint), s.is_cycling
FROM sys.sequences s
JOIN sys.schemas sch ON sch.schema_id = s.schema_id
JOIN sys.types tp ON tp.user_type_id = s.user_type_id
WHERE sch.name IN ({schemaList})
ORDER BY sch.name, s.name;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Sequence(
                Schema: r.GetString(0), Name: r.GetString(1),
                SqlType: r.GetString(2),
                StartValue: r.GetInt64(3), Increment: r.GetInt64(4),
                MinValue: r.IsDBNull(5) ? null : r.GetInt64(5),
                MaxValue: r.IsDBNull(6) ? null : r.GetInt64(6),
                IsCycling: r.GetBoolean(7)));
        }
        return list;
    }
}
