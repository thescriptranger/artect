using System.Collections.Generic;
using System.Linq;
using Artect.Core.Schema;
using Microsoft.Data.SqlClient;

namespace Artect.Introspection.Readers;

public static class StoredProceduresReader
{
    public static IReadOnlyList<StoredProcedure> Read(SqlConnection conn, IReadOnlyList<string> schemas)
    {
        var schemaList = "'" + string.Join("','", schemas) + "'";
        var procs = new List<(string Sch, string Nm)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
SELECT ROUTINE_SCHEMA, ROUTINE_NAME
FROM INFORMATION_SCHEMA.ROUTINES
WHERE ROUTINE_TYPE = 'PROCEDURE' AND ROUTINE_SCHEMA IN ({schemaList})
ORDER BY ROUTINE_SCHEMA, ROUTINE_NAME;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) procs.Add((r.GetString(0), r.GetString(1)));
        }
        var parms = ReadParameters(conn, schemaList);
        var result = new List<StoredProcedure>();
        foreach (var (sch, nm) in procs)
        {
            parms.TryGetValue((sch, nm), out var ps);
            var (cols, status) = DescribeFirstResultSet(conn, sch, nm);
            result.Add(new StoredProcedure(sch, nm, ps ?? new List<StoredProcedureParameter>(), cols, status));
        }
        return result;
    }

    static Dictionary<(string, string), IReadOnlyList<StoredProcedureParameter>> ReadParameters(SqlConnection conn, string schemaList)
    {
        var map = new Dictionary<(string, string), List<StoredProcedureParameter>>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
SELECT SPECIFIC_SCHEMA, SPECIFIC_NAME, PARAMETER_NAME, ORDINAL_POSITION, DATA_TYPE,
       PARAMETER_MODE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE
FROM INFORMATION_SCHEMA.PARAMETERS
WHERE SPECIFIC_SCHEMA IN ({schemaList})
ORDER BY SPECIFIC_SCHEMA, SPECIFIC_NAME, ORDINAL_POSITION;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var key = (r.GetString(0), r.GetString(1));
            if (!map.TryGetValue(key, out var list)) { list = new List<StoredProcedureParameter>(); map[key] = list; }
            var sqlType = r.GetString(4);
            var mode = r.GetString(5);
            list.Add(new StoredProcedureParameter(
                Name: r.IsDBNull(2) ? $"arg{r.GetInt32(3)}" : r.GetString(2),
                Ordinal: r.GetInt32(3),
                SqlType: sqlType, ClrType: SqlTypeMap.ToClr(sqlType),
                IsNullable: true,
                IsOutput: mode is "OUT" or "INOUT",
                MaxLength: r.IsDBNull(6) ? null : r.GetInt32(6),
                Precision: r.IsDBNull(7) ? null : (int?)r.GetByte(7),
                Scale: r.IsDBNull(8) ? null : (int?)r.GetInt32(8)));
        }
        return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<StoredProcedureParameter>)kv.Value);
    }

    static (IReadOnlyList<StoredProcedureResultColumn>, ResultInferenceStatus) DescribeFirstResultSet(SqlConnection conn, string schema, string name)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "sys.sp_describe_first_result_set";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.Add(new SqlParameter("@tsql", $"EXEC [{schema}].[{name}]"));
            using var r = cmd.ExecuteReader();
            var cols = new List<StoredProcedureResultColumn>();
            int ordinal = 0;
            while (r.Read())
            {
                var colName = r["name"] is System.DBNull ? $"col{ordinal}" : (string)r["name"];
                var typeName = (string)r["system_type_name"];
                var isNullable = (bool)r["is_nullable"];
                var sqlBaseType = typeName.Split('(')[0].ToLowerInvariant();
                cols.Add(new StoredProcedureResultColumn(colName, ordinal++, sqlBaseType, SqlTypeMap.ToClr(sqlBaseType), isNullable));
            }
            if (cols.Count == 0) return (cols, ResultInferenceStatus.Empty);
            return (cols, ResultInferenceStatus.Resolved);
        }
        catch (SqlException)
        {
            return (new List<StoredProcedureResultColumn>(), ResultInferenceStatus.Indeterminate);
        }
    }
}
