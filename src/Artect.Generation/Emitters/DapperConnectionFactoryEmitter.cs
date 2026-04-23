using System.Collections.Generic;
using System.Text;
using Artect.Config;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits <c>IDbConnectionFactory</c> and <c>SqlDbConnectionFactory</c> into
/// <c>&lt;Project&gt;.Infrastructure.Data</c>.
/// Only runs when <c>cfg.DataAccess == Dapper</c>.
/// </summary>
public sealed class DapperConnectionFactoryEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        if (ctx.Config.DataAccess != DataAccessKind.Dapper)
            return System.Array.Empty<EmittedFile>();

        var project = ctx.Config.ProjectName;
        var ns      = $"{CleanLayout.InfrastructureNamespace(project)}.Data";

        var sb = new StringBuilder();
        sb.AppendLine("using System.Data;");
        sb.AppendLine("using Microsoft.Data.SqlClient;");
        sb.AppendLine("using Microsoft.Extensions.Configuration;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>Abstracts connection creation so repositories stay testable.</summary>");
        sb.AppendLine("public interface IDbConnectionFactory");
        sb.AppendLine("{");
        sb.AppendLine("    IDbConnection CreateOpenConnection();");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("/// <summary>Production implementation backed by SQL Server.</summary>");
        sb.AppendLine("public sealed class SqlDbConnectionFactory : IDbConnectionFactory");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly string _connectionString;");
        sb.AppendLine();
        sb.AppendLine("    public SqlDbConnectionFactory(IConfiguration configuration)");
        sb.AppendLine("    {");
        sb.AppendLine("        _connectionString = configuration.GetConnectionString(\"DefaultConnection\")");
        sb.AppendLine("            ?? throw new System.InvalidOperationException(");
        sb.AppendLine("                \"Missing connection string 'DefaultConnection' in configuration.\");");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public IDbConnection CreateOpenConnection()");
        sb.AppendLine("    {");
        sb.AppendLine("        var conn = new SqlConnection(_connectionString);");
        sb.AppendLine("        conn.Open();");
        sb.AppendLine("        return conn;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        var path    = CleanLayout.ConnectionFactoryPath(project);
        var content = sb.ToString();
        return new[] { new EmittedFile(path, content) };
    }
}
