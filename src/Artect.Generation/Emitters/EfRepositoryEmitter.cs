using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits <c>&lt;Entity&gt;Repository : I&lt;Entity&gt;Repository</c> using EF Core.
/// Only runs when <c>cfg.DataAccess == EfCore</c> AND <c>cfg.EmitRepositoriesAndAbstractions == true</c>.
/// Constructor injects <c>&lt;Project&gt;DbContext _db</c>; methods use <c>_db.&lt;DbSet&gt;</c> directly.
/// Projects entity → <c>&lt;Entity&gt;Model</c> inline via object-initializer <c>Select</c> so EF can translate.
/// </summary>
public sealed class EfRepositoryEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        if (ctx.Config.DataAccess != DataAccessKind.EfCore) return System.Array.Empty<EmittedFile>();
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
        var dbset    = entity.DbSetPropertyName;
        var dbCtx    = $"{project}DbContext";
        var pkType   = PkClrType(entity.Table);
        var pkProp   = EntityNaming.PropertyName(
            entity.Table.Columns.First(c =>
                string.Equals(c.Name, entity.Table.PrimaryKey!.ColumnNames[0],
                    System.StringComparison.OrdinalIgnoreCase)));

        var ns        = $"{CleanLayout.InfrastructureNamespace(project)}.Repositories";
        var dataAbsNs = $"{CleanLayout.InfrastructureNamespace(project)}.Data";
        var repoAbsNs = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var commonNs  = CleanLayout.ApplicationCommonNamespace(project);
        var modelsNs  = CleanLayout.ApplicationModelsNamespace(project);
        var entityNs  = $"{CleanLayout.DomainNamespace(project)}.Entities";

        var allCols = entity.Table.Columns.ToList();
        // Inline projection — EF-translatable.
        string ProjectionBody(string srcVar)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"new {name}Model");
            sb.AppendLine("            {");
            for (int i = 0; i < allCols.Count; i++)
            {
                var prop = EntityNaming.PropertyName(allCols[i]);
                sb.AppendLine($"                {prop} = {srcVar}.{prop},");
            }
            sb.Append("            }");
            return sb.ToString();
        }

        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
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
        sb.AppendLine($"    private readonly {dbCtx} _db;");
        sb.AppendLine();
        sb.AppendLine($"    public {name}Repository({dbCtx} db)");
        sb.AppendLine("    {");
        sb.AppendLine("        _db = db;");
        sb.AppendLine("    }");
        sb.AppendLine();

        // ListAsync
        sb.AppendLine($"    public async Task<PagedResult<{name}Model>> ListAsync(int page, int pageSize, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var query = _db.{dbset}.AsNoTracking();");
        sb.AppendLine("        // Extension point: customize the IQueryable/SQL here");
        sb.AppendLine("        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);");
        sb.AppendLine("        var items = await query");
        sb.AppendLine("            .Skip((page - 1) * pageSize)");
        sb.AppendLine("            .Take(pageSize)");
        sb.AppendLine($"            .Select(e => {ProjectionBody("e")})");
        sb.AppendLine("            .ToListAsync(ct).ConfigureAwait(false);");
        sb.AppendLine($"        return new PagedResult<{name}Model>");
        sb.AppendLine("        {");
        sb.AppendLine("            Items = items,");
        sb.AppendLine("            Page = page,");
        sb.AppendLine("            PageSize = pageSize,");
        sb.AppendLine("            TotalCount = totalCount,");
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine();

        // GetByIdAsync
        sb.AppendLine($"    public async Task<{name}Model?> GetByIdAsync({pkType} id, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine($"        return await _db.{dbset}.AsNoTracking()");
        sb.AppendLine($"            .Where(e => e.{pkProp} == id)");
        sb.AppendLine($"            .Select(e => {ProjectionBody("e")})");
        sb.AppendLine("            .FirstOrDefaultAsync(ct).ConfigureAwait(false);");
        sb.AppendLine("    }");
        sb.AppendLine();

        // CreateAsync
        sb.AppendLine($"    public async Task<{name}Model> CreateAsync({name} entity, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine($"        await _db.{dbset}.AddAsync(entity, ct).ConfigureAwait(false);");
        sb.AppendLine($"        return {ProjectionBody("entity")};");
        sb.AppendLine("    }");
        sb.AppendLine();

        // UpdateAsync
        sb.AppendLine($"    public Task UpdateAsync({name} entity, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine($"        _db.{dbset}.Update(entity);");
        sb.AppendLine("        return Task.CompletedTask;");
        sb.AppendLine("    }");
        sb.AppendLine();

        // DeleteAsync
        sb.AppendLine($"    public async Task DeleteAsync({pkType} id, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var entity = await _db.{dbset}");
        sb.AppendLine($"            .FirstOrDefaultAsync(e => e.{pkProp} == id, ct).ConfigureAwait(false);");
        sb.AppendLine("        if (entity is null) { return; }");
        sb.AppendLine($"        _db.{dbset}.Remove(entity);");
        sb.AppendLine("    }");

        sb.AppendLine("}");
        return sb.ToString();
    }

    static string PkClrType(Table table)
    {
        var pk = table.PrimaryKey!;
        if (pk.ColumnNames.Count == 1)
        {
            var col = table.Columns.First(c =>
                string.Equals(c.Name, pk.ColumnNames[0], System.StringComparison.OrdinalIgnoreCase));
            return SqlTypeMap.ToCs(col.ClrType);
        }
        var parts = pk.ColumnNames.Select(n =>
        {
            var c = table.Columns.First(col =>
                string.Equals(col.Name, n, System.StringComparison.OrdinalIgnoreCase));
            return SqlTypeMap.ToCs(c.ClrType);
        });
        return "(" + string.Join(", ", parts) + ")";
    }
}
