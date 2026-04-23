using System.Collections.Generic;
using Artect.Core.Schema;
using Microsoft.Data.SqlClient;

namespace Artect.Introspection.Readers;

public static class FunctionsReader
{
    public static IReadOnlyList<Function> Read(SqlConnection conn, IReadOnlyList<string> schemas)
    {
        var schemaList = "'" + string.Join("','", schemas) + "'";
        var list = new List<Function>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
SELECT sch.name, o.name, o.type_desc
FROM sys.objects o
JOIN sys.schemas sch ON sch.schema_id = o.schema_id
WHERE o.type IN ('FN','IF','TF') AND sch.name IN ({schemaList})
ORDER BY sch.name, o.name;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var schema = r.GetString(0);
            var name = r.GetString(1);
            var desc = r.GetString(2);
            var kind = desc switch
            {
                "SQL_SCALAR_FUNCTION" => FunctionReturnKind.Scalar,
                "SQL_INLINE_TABLE_VALUED_FUNCTION" => FunctionReturnKind.Inline,
                "SQL_TABLE_VALUED_FUNCTION" => FunctionReturnKind.Table,
                _ => FunctionReturnKind.Scalar
            };
            list.Add(new Function(schema, name, kind,
                ReturnSqlType: null, ReturnClrType: null,
                Parameters: new List<FunctionParameter>(),
                ResultColumns: new List<Column>()));
        }
        return list;
    }
}
