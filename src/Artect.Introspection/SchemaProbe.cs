using System.Collections.Generic;
using Microsoft.Data.SqlClient;

namespace Artect.Introspection;

public sealed class SchemaProbe
{
    readonly SqlConnectionFactory _factory;
    public SchemaProbe(SqlConnectionFactory factory) => _factory = factory;

    public IReadOnlyList<string> ListSchemas()
    {
        using var conn = _factory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT SCHEMA_NAME
FROM INFORMATION_SCHEMA.SCHEMATA
WHERE SCHEMA_NAME NOT IN ('sys','INFORMATION_SCHEMA','db_owner','db_accessadmin','db_securityadmin',
 'db_ddladmin','db_backupoperator','db_datareader','db_datawriter','db_denydatareader','db_denydatawriter','guest')
ORDER BY SCHEMA_NAME;";
        using var reader = cmd.ExecuteReader();
        var schemas = new List<string>();
        while (reader.Read()) schemas.Add(reader.GetString(0));
        return schemas;
    }
}
