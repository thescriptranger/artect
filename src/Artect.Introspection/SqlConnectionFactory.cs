using Microsoft.Data.SqlClient;

namespace Artect.Introspection;

public sealed class SqlConnectionFactory
{
    readonly string _connectionString;
    public SqlConnectionFactory(string connectionString) => _connectionString = connectionString;

    public SqlConnection OpenConnection()
    {
        var conn = new SqlConnection(_connectionString);
        conn.Open();
        return conn;
    }
}
