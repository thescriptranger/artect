using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits <c>&lt;Entity&gt;Repository : I&lt;Entity&gt;Repository</c> using Dapper.
/// Only runs when <c>cfg.DataAccess == Dapper</c> AND <c>cfg.EmitRepositoriesAndAbstractions == true</c>.
/// Constructor injects <c>IDbConnectionFactory</c>; all SQL is parameterized.
/// </summary>
public sealed class DapperRepositoryImplEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        if (ctx.Config.DataAccess != DataAccessKind.Dapper) return System.Array.Empty<EmittedFile>();
        if (!ctx.Config.EmitRepositoriesAndAbstractions) return System.Array.Empty<EmittedFile>();

        var list    = new List<EmittedFile>();
        var project = ctx.Config.ProjectName;

        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.IsJoinTable) continue;
            if (!entity.HasPrimaryKey) continue;

            var content = BuildRepository(ctx, entity, project);
            var path    = CleanLayout.RepositoryImplPath(project, entity.EntityTypeName);
            list.Add(new EmittedFile(path, content));
        }

        return list;
    }

    static string BuildRepository(EmitterContext ctx, NamedEntity entity, string project)
    {
        var name     = entity.EntityTypeName;
        var table    = entity.Table;
        var pk       = table.PrimaryKey!;
        var pkColName = pk.ColumnNames[0];
        var pkCol    = table.Columns.First(c =>
            string.Equals(c.Name, pkColName, System.StringComparison.OrdinalIgnoreCase));
        var pkType   = SqlTypeMap.ToCs(pkCol.ClrType);
        var pkProp   = EntityNaming.PropertyName(pkCol);

        var fullTable = $"[{table.Schema}].[{table.Name}]";

        // Build column lists
        var selectCols = string.Join(", ",
            table.Columns.Select(c => $"[{c.Name}] AS [{EntityNaming.PropertyName(c)}]"));
        var insertCols = table.Columns.Where(c => !c.IsServerGenerated).ToList();
        var insertColList  = string.Join(", ", insertCols.Select(c => $"[{c.Name}]"));
        var insertParamList = string.Join(", ", insertCols.Select(c => $"@{EntityNaming.PropertyName(c)}"));
        var updateSetParts = insertCols
            .Where(c => !string.Equals(c.Name, pkColName, System.StringComparison.OrdinalIgnoreCase))
            .Select(c => $"[{c.Name}] = @{EntityNaming.PropertyName(c)}");
        var updateSet = string.Join(", ", updateSetParts);

        var ns          = $"{CleanLayout.InfrastructureNamespace(project)}.Repositories";
        var dataAbsNs   = $"{CleanLayout.InfrastructureNamespace(project)}.Data";
        var repoAbsNs   = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var dtoNs       = $"{CleanLayout.ApplicationNamespace(project)}.Dtos";
        var mappingsNs  = $"{CleanLayout.ApplicationNamespace(project)}.Mappings";
        var respNs      = $"{CleanLayout.SharedNamespace(project)}.Responses";

        var sb = new StringBuilder();
        sb.AppendLine("using Dapper;");
        sb.AppendLine("using System.Data;");
        sb.AppendLine($"using {dataAbsNs};");
        sb.AppendLine($"using {repoAbsNs};");
        sb.AppendLine($"using {dtoNs};");
        sb.AppendLine($"using {mappingsNs};");
        sb.AppendLine($"using {respNs};");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"public sealed class {name}Repository : I{name}Repository");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly IDbConnectionFactory _connections;");
        sb.AppendLine();
        sb.AppendLine($"    public {name}Repository(IDbConnectionFactory connections)");
        sb.AppendLine("    {");
        sb.AppendLine("        _connections = connections;");
        sb.AppendLine("    }");
        sb.AppendLine();

        // ListAsync (server-side paging with OFFSET/FETCH)
        sb.AppendLine($"    public async Task<PagedResponse<{name}Dto>> ListAsync(int page, int pageSize, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine("        using var conn = _connections.CreateOpenConnection();");
        sb.AppendLine($"        const string countSql = \"SELECT COUNT(*) FROM {fullTable}\";");
        sb.AppendLine("        // Extension point: customize the IQueryable/SQL here");
        sb.AppendLine($"        const string itemSql =");
        sb.AppendLine($"            \"SELECT {selectCols} FROM {fullTable}\" +");
        sb.AppendLine($"            \" ORDER BY [{pkColName}]\" +");
        sb.AppendLine($"            \" OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY\";");
        sb.AppendLine("        var skip = (page - 1) * pageSize;");
        sb.AppendLine("        var totalCount = await conn.ExecuteScalarAsync<int>(countSql).ConfigureAwait(false);");
        sb.AppendLine($"        var rows = await conn.QueryAsync<{name}Dto>(itemSql, new {{ skip, take = pageSize }}).ConfigureAwait(false);");
        sb.AppendLine($"        return new PagedResponse<{name}Dto>");
        sb.AppendLine("        {");
        sb.AppendLine("            Items = rows.AsList(),");
        sb.AppendLine("            Page = page,");
        sb.AppendLine("            PageSize = pageSize,");
        sb.AppendLine("            TotalCount = totalCount,");
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine();

        // GetByIdAsync
        sb.AppendLine($"    public async Task<{name}Dto?> GetByIdAsync({pkType} id, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine("        using var conn = _connections.CreateOpenConnection();");
        sb.AppendLine($"        const string sql = \"SELECT {selectCols} FROM {fullTable} WHERE [{pkColName}] = @id\";");
        sb.AppendLine($"        return await conn.QuerySingleOrDefaultAsync<{name}Dto>(sql, new {{ id }}).ConfigureAwait(false);");
        sb.AppendLine("    }");
        sb.AppendLine();

        // CreateAsync
        sb.AppendLine($"    public async Task<{name}Dto> CreateAsync({name}Dto dto, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine("        using var conn = _connections.CreateOpenConnection();");
        if (pkCol.IsServerGenerated)
        {
            sb.AppendLine($"        const string sql =");
            sb.AppendLine($"            \"INSERT INTO {fullTable} ({insertColList})\" +");
            sb.AppendLine($"            \" OUTPUT INSERTED.[{pkColName}]\" +");
            sb.AppendLine($"            \" VALUES ({insertParamList})\";");
            sb.AppendLine($"        var newId = await conn.ExecuteScalarAsync<{pkType}>(sql, dto).ConfigureAwait(false);");
            sb.AppendLine($"        dto = dto with {{ {pkProp} = newId }};");
        }
        else
        {
            sb.AppendLine($"        const string sql = \"INSERT INTO {fullTable} ({insertColList}) VALUES ({insertParamList})\";");
            sb.AppendLine("        await conn.ExecuteAsync(sql, dto).ConfigureAwait(false);");
        }
        sb.AppendLine("        return dto;");
        sb.AppendLine("    }");
        sb.AppendLine();

        // UpdateAsync
        sb.AppendLine($"    public async Task UpdateAsync({name}Dto dto, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine("        using var conn = _connections.CreateOpenConnection();");
        sb.AppendLine($"        const string sql = \"UPDATE {fullTable} SET {updateSet} WHERE [{pkColName}] = @{pkProp}\";");
        sb.AppendLine("        await conn.ExecuteAsync(sql, dto).ConfigureAwait(false);");
        sb.AppendLine("    }");
        sb.AppendLine();

        // DeleteAsync
        sb.AppendLine($"    public async Task DeleteAsync({pkType} id, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine("        using var conn = _connections.CreateOpenConnection();");
        sb.AppendLine($"        const string sql = \"DELETE FROM {fullTable} WHERE [{pkColName}] = @id\";");
        sb.AppendLine("        await conn.ExecuteAsync(sql, new { id }).ConfigureAwait(false);");
        sb.AppendLine("    }");

        sb.AppendLine("}");
        return sb.ToString();
    }
}
