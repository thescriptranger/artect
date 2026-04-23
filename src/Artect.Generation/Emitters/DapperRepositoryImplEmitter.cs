using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits Dapper repository implementations.
/// Only runs when <c>cfg.DataAccess == Dapper</c> AND <c>cfg.EmitRepositoriesAndAbstractions == true</c>.
/// When <c>cfg.SplitRepositoriesByIntent == true</c> (default), emits two classes per entity:
///   <c>&lt;Entity&gt;ReadRepository  : I&lt;Entity&gt;ReadRepository</c>
///   <c>&lt;Entity&gt;WriteRepository : I&lt;Entity&gt;WriteRepository</c>
/// When false, emits the single monolithic <c>&lt;Entity&gt;Repository : I&lt;Entity&gt;Repository</c>.
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
        var split   = ctx.Config.SplitRepositoriesByIntent;

        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.IsJoinTable) continue;
            if (!entity.HasPrimaryKey) continue;

            if (split)
            {
                list.Add(new EmittedFile(
                    CleanLayout.RepositoryReadImplPath(project, entity.EntityTypeName),
                    BuildDapperReadRepository(ctx, entity, project)));
                list.Add(new EmittedFile(
                    CleanLayout.RepositoryWriteImplPath(project, entity.EntityTypeName),
                    BuildDapperWriteRepository(ctx, entity, project)));
            }
            else
            {
                list.Add(new EmittedFile(
                    CleanLayout.RepositoryImplPath(project, entity.EntityTypeName),
                    BuildDapperMonolithicRepository(ctx, entity, project)));
            }
        }

        return list;
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    static (string pkColName, string pkType, string pkProp, string fullTable,
            string selectCols, List<Column> insertCols, string insertColList,
            string insertParamList, string updateSet) BuildSqlParts(NamedEntity entity)
    {
        var table      = entity.Table;
        var pk         = table.PrimaryKey!;
        var pkColName  = pk.ColumnNames[0];
        var pkCol      = table.Columns.First(c =>
            string.Equals(c.Name, pkColName, System.StringComparison.OrdinalIgnoreCase));
        var pkType     = SqlTypeMap.ToCs(pkCol.ClrType);
        var pkProp     = EntityNaming.PropertyName(pkCol);
        var fullTable  = $"[{table.Schema}].[{table.Name}]";

        var selectCols = string.Join(", ",
            table.Columns.Select(c => $"[{c.Name}] AS [{EntityNaming.PropertyName(c)}]"));
        var insertCols = table.Columns.Where(c => !c.IsServerGenerated).ToList();
        var insertColList   = string.Join(", ", insertCols.Select(c => $"[{c.Name}]"));
        var insertParamList = string.Join(", ", insertCols.Select(c => $"@{EntityNaming.PropertyName(c)}"));
        var updateSetParts  = insertCols
            .Where(c => !string.Equals(c.Name, pkColName, System.StringComparison.OrdinalIgnoreCase))
            .Select(c => $"[{c.Name}] = @{EntityNaming.PropertyName(c)}");
        var updateSet = string.Join(", ", updateSetParts);

        return (pkColName, pkType, pkProp, fullTable, selectCols, insertCols, insertColList, insertParamList, updateSet);
    }

    static string ProjectionBody(string name, IReadOnlyList<Column> allCols, string srcVar, int indent)
    {
        var pad = new string(' ', indent);
        var sb2 = new StringBuilder();
        sb2.AppendLine($"new {name}Model");
        sb2.AppendLine($"{pad}{{");
        foreach (var col in allCols)
        {
            var prop = EntityNaming.PropertyName(col);
            sb2.AppendLine($"{pad}    {prop} = {srcVar}.{prop},");
        }
        sb2.Append($"{pad}}}");
        return sb2.ToString();
    }

    // ── Split: Read repository ────────────────────────────────────────────────

    static string BuildDapperReadRepository(EmitterContext ctx, NamedEntity entity, string project)
    {
        var name   = entity.EntityTypeName;
        var (pkColName, pkType, pkProp, fullTable, selectCols, _, _, _, _) = BuildSqlParts(entity);

        var ns        = $"{CleanLayout.InfrastructureNamespace(project)}.Repositories";
        var dataAbsNs = $"{CleanLayout.InfrastructureNamespace(project)}.Data";
        var repoAbsNs = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var commonNs  = CleanLayout.ApplicationCommonNamespace(project);
        var modelsNs  = CleanLayout.ApplicationModelsNamespace(project);

        var sb = new StringBuilder();
        sb.AppendLine("using Dapper;");
        sb.AppendLine("using System.Data;");
        sb.AppendLine($"using {dataAbsNs};");
        sb.AppendLine($"using {repoAbsNs};");
        sb.AppendLine($"using {commonNs};");
        sb.AppendLine($"using {modelsNs};");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"public sealed class {name}ReadRepository : I{name}ReadRepository");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly IDbConnectionFactory _connections;");
        sb.AppendLine();
        sb.AppendLine($"    public {name}ReadRepository(IDbConnectionFactory connections)");
        sb.AppendLine("    {");
        sb.AppendLine("        _connections = connections;");
        sb.AppendLine("    }");
        sb.AppendLine();

        // ListAsync
        sb.AppendLine($"    public async Task<PagedResult<{name}Model>> ListAsync(int page, int pageSize, CancellationToken ct)");
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
        sb.AppendLine($"        var rows = await conn.QueryAsync<{name}Model>(itemSql, new {{ skip, take = pageSize }}).ConfigureAwait(false);");
        sb.AppendLine($"        return new PagedResult<{name}Model>");
        sb.AppendLine("        {");
        sb.AppendLine("            Items = rows.AsList(),");
        sb.AppendLine("            Page = page,");
        sb.AppendLine("            PageSize = pageSize,");
        sb.AppendLine("            TotalCount = totalCount,");
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine();

        // GetByIdAsync
        sb.AppendLine($"    public async Task<{name}Model?> GetByIdAsync({pkType} id, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine("        using var conn = _connections.CreateOpenConnection();");
        sb.AppendLine($"        const string sql = \"SELECT {selectCols} FROM {fullTable} WHERE [{pkColName}] = @id\";");
        sb.AppendLine($"        return await conn.QuerySingleOrDefaultAsync<{name}Model>(sql, new {{ id }}).ConfigureAwait(false);");
        sb.AppendLine("    }");

        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── Split: Write repository ───────────────────────────────────────────────

    static string BuildDapperWriteRepository(EmitterContext ctx, NamedEntity entity, string project)
    {
        var name   = entity.EntityTypeName;
        var table  = entity.Table;
        var (pkColName, pkType, pkProp, fullTable, selectCols, insertCols, insertColList, insertParamList, updateSet) = BuildSqlParts(entity);
        var pkCol = table.Columns.First(c =>
            string.Equals(c.Name, pkColName, System.StringComparison.OrdinalIgnoreCase));

        var ns        = $"{CleanLayout.InfrastructureNamespace(project)}.Repositories";
        var dataAbsNs = $"{CleanLayout.InfrastructureNamespace(project)}.Data";
        var repoAbsNs = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var modelsNs  = CleanLayout.ApplicationModelsNamespace(project);
        var entityNs  = $"{CleanLayout.DomainNamespace(project)}.Entities";
        var allCols   = table.Columns.ToList();

        var sb = new StringBuilder();
        sb.AppendLine("using Dapper;");
        sb.AppendLine("using System.Data;");
        sb.AppendLine($"using {dataAbsNs};");
        sb.AppendLine($"using {repoAbsNs};");
        sb.AppendLine($"using {modelsNs};");
        sb.AppendLine($"using {entityNs};");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"public sealed class {name}WriteRepository : I{name}WriteRepository");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly IDbConnectionFactory _connections;");
        sb.AppendLine();
        sb.AppendLine($"    public {name}WriteRepository(IDbConnectionFactory connections)");
        sb.AppendLine("    {");
        sb.AppendLine("        _connections = connections;");
        sb.AppendLine("    }");
        sb.AppendLine();

        // CreateAsync
        sb.AppendLine($"    public async Task<{name}Model> CreateAsync({name} entity, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine("        using var conn = _connections.CreateOpenConnection();");
        if (pkCol.IsServerGenerated)
        {
            sb.AppendLine($"        const string sql =");
            sb.AppendLine($"            \"INSERT INTO {fullTable} ({insertColList})\" +");
            sb.AppendLine($"            \" OUTPUT INSERTED.[{pkColName}]\" +");
            sb.AppendLine($"            \" VALUES ({insertParamList})\";");
            sb.AppendLine($"        var newId = await conn.ExecuteScalarAsync<{pkType}>(sql, entity).ConfigureAwait(false);");
            sb.AppendLine($"        var model = {ProjectionBody(name, allCols, "entity", 8)};");
            sb.AppendLine($"        return model with {{ {pkProp} = newId }};");
        }
        else
        {
            sb.AppendLine($"        const string sql = \"INSERT INTO {fullTable} ({insertColList}) VALUES ({insertParamList})\";");
            sb.AppendLine("        await conn.ExecuteAsync(sql, entity).ConfigureAwait(false);");
            sb.AppendLine($"        return {ProjectionBody(name, allCols, "entity", 8)};");
        }
        sb.AppendLine("    }");
        sb.AppendLine();

        // UpdateAsync
        sb.AppendLine($"    public async Task UpdateAsync({name} entity, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine("        using var conn = _connections.CreateOpenConnection();");
        sb.AppendLine($"        const string sql = \"UPDATE {fullTable} SET {updateSet} WHERE [{pkColName}] = @{pkProp}\";");
        sb.AppendLine("        await conn.ExecuteAsync(sql, entity).ConfigureAwait(false);");
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

    // ── Monolithic (SplitRepositoriesByIntent == false) ───────────────────────

    static string BuildDapperMonolithicRepository(EmitterContext ctx, NamedEntity entity, string project)
    {
        var name   = entity.EntityTypeName;
        var table  = entity.Table;
        var (pkColName, pkType, pkProp, fullTable, selectCols, insertCols, insertColList, insertParamList, updateSet) = BuildSqlParts(entity);
        var pkCol = table.Columns.First(c =>
            string.Equals(c.Name, pkColName, System.StringComparison.OrdinalIgnoreCase));

        var ns        = $"{CleanLayout.InfrastructureNamespace(project)}.Repositories";
        var dataAbsNs = $"{CleanLayout.InfrastructureNamespace(project)}.Data";
        var repoAbsNs = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var commonNs  = CleanLayout.ApplicationCommonNamespace(project);
        var modelsNs  = CleanLayout.ApplicationModelsNamespace(project);
        var entityNs  = $"{CleanLayout.DomainNamespace(project)}.Entities";
        var allCols   = table.Columns.ToList();

        var sb = new StringBuilder();
        sb.AppendLine("using Dapper;");
        sb.AppendLine("using System.Data;");
        sb.AppendLine($"using {dataAbsNs};");
        sb.AppendLine($"using {repoAbsNs};");
        sb.AppendLine($"using {commonNs};");
        sb.AppendLine($"using {modelsNs};");
        sb.AppendLine($"using {entityNs};");
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

        // ListAsync
        sb.AppendLine($"    public async Task<PagedResult<{name}Model>> ListAsync(int page, int pageSize, CancellationToken ct)");
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
        sb.AppendLine($"        var rows = await conn.QueryAsync<{name}Model>(itemSql, new {{ skip, take = pageSize }}).ConfigureAwait(false);");
        sb.AppendLine($"        return new PagedResult<{name}Model>");
        sb.AppendLine("        {");
        sb.AppendLine("            Items = rows.AsList(),");
        sb.AppendLine("            Page = page,");
        sb.AppendLine("            PageSize = pageSize,");
        sb.AppendLine("            TotalCount = totalCount,");
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine();

        // GetByIdAsync
        sb.AppendLine($"    public async Task<{name}Model?> GetByIdAsync({pkType} id, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine("        using var conn = _connections.CreateOpenConnection();");
        sb.AppendLine($"        const string sql = \"SELECT {selectCols} FROM {fullTable} WHERE [{pkColName}] = @id\";");
        sb.AppendLine($"        return await conn.QuerySingleOrDefaultAsync<{name}Model>(sql, new {{ id }}).ConfigureAwait(false);");
        sb.AppendLine("    }");
        sb.AppendLine();

        // CreateAsync
        sb.AppendLine($"    public async Task<{name}Model> CreateAsync({name} entity, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine("        using var conn = _connections.CreateOpenConnection();");
        if (pkCol.IsServerGenerated)
        {
            sb.AppendLine($"        const string sql =");
            sb.AppendLine($"            \"INSERT INTO {fullTable} ({insertColList})\" +");
            sb.AppendLine($"            \" OUTPUT INSERTED.[{pkColName}]\" +");
            sb.AppendLine($"            \" VALUES ({insertParamList})\";");
            sb.AppendLine($"        var newId = await conn.ExecuteScalarAsync<{pkType}>(sql, entity).ConfigureAwait(false);");
            sb.AppendLine($"        var model = {ProjectionBody(name, allCols, "entity", 8)};");
            sb.AppendLine($"        return model with {{ {pkProp} = newId }};");
        }
        else
        {
            sb.AppendLine($"        const string sql = \"INSERT INTO {fullTable} ({insertColList}) VALUES ({insertParamList})\";");
            sb.AppendLine("        await conn.ExecuteAsync(sql, entity).ConfigureAwait(false);");
            sb.AppendLine($"        return {ProjectionBody(name, allCols, "entity", 8)};");
        }
        sb.AppendLine("    }");
        sb.AppendLine();

        // UpdateAsync
        sb.AppendLine($"    public async Task UpdateAsync({name} entity, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine("        using var conn = _connections.CreateOpenConnection();");
        sb.AppendLine($"        const string sql = \"UPDATE {fullTable} SET {updateSet} WHERE [{pkColName}] = @{pkProp}\";");
        sb.AppendLine("        await conn.ExecuteAsync(sql, entity).ConfigureAwait(false);");
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
