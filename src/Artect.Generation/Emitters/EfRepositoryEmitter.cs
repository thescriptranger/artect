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

        // When interactors are enabled the UnitOfWork owns the commit boundary,
        // so the repository must NOT call SaveChangesAsync (that would double-commit).
        // When interactors are disabled, endpoints call repos directly and there is
        // no UoW in the call chain, so the repo must save its own changes.
        var uowOwnsCommit = ctx.Config.EmitUseCaseInteractors;

        var ns          = $"{CleanLayout.InfrastructureNamespace(project)}.Repositories";
        var dataAbsNs   = $"{CleanLayout.InfrastructureNamespace(project)}.Data";
        var repoAbsNs   = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var dtoNs       = $"{CleanLayout.ApplicationNamespace(project)}.Dtos";
        var mappingsNs  = $"{CleanLayout.ApplicationNamespace(project)}.Mappings";
        var respNs      = $"{CleanLayout.SharedNamespace(project)}.Responses";

        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        sb.AppendLine($"using {dataAbsNs};");
        sb.AppendLine($"using {repoAbsNs};");
        sb.AppendLine($"using {dtoNs};");
        sb.AppendLine($"using {mappingsNs};");
        sb.AppendLine($"using {respNs};");
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
        sb.AppendLine($"    public async Task<PagedResponse<{name}Dto>> ListAsync(int page, int pageSize, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var query = _db.{dbset}.AsNoTracking();");
        sb.AppendLine("        // Extension point: customize the IQueryable/SQL here");
        sb.AppendLine("        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);");
        sb.AppendLine("        var items = await query");
        sb.AppendLine("            .Skip((page - 1) * pageSize)");
        sb.AppendLine("            .Take(pageSize)");
        sb.AppendLine($"            .Select(e => e.ToDto())");
        sb.AppendLine("            .ToListAsync(ct).ConfigureAwait(false);");
        sb.AppendLine($"        return new PagedResponse<{name}Dto>");
        sb.AppendLine("        {");
        sb.AppendLine("            Items = items,");
        sb.AppendLine("            Page = page,");
        sb.AppendLine("            PageSize = pageSize,");
        sb.AppendLine("            TotalCount = totalCount,");
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine();

        // GetByIdAsync
        sb.AppendLine($"    public async Task<{name}Dto?> GetByIdAsync({pkType} id, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var entity = await _db.{dbset}.AsNoTracking()");
        sb.AppendLine($"            .FirstOrDefaultAsync(e => e.{pkProp} == id, ct).ConfigureAwait(false);");
        sb.AppendLine("        return entity?.ToDto();");
        sb.AppendLine("    }");
        sb.AppendLine();

        // CreateAsync
        sb.AppendLine($"    public async Task<{name}Dto> CreateAsync({name}Dto dto, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine("        var entity = dto.ToEntity();");
        sb.AppendLine($"        _db.{dbset}.Add(entity);");
        if (!uowOwnsCommit)
            sb.AppendLine("        await _db.SaveChangesAsync(ct).ConfigureAwait(false);");
        sb.AppendLine("        return entity.ToDto();");
        sb.AppendLine("    }");
        sb.AppendLine();

        // UpdateAsync
        sb.AppendLine($"    public async Task UpdateAsync({name}Dto dto, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var entity = await _db.{dbset}");
        sb.AppendLine($"            .FirstOrDefaultAsync(e => e.{pkProp} == dto.{pkProp}, ct).ConfigureAwait(false);");
        sb.AppendLine("        if (entity is null) { return; }");
        sb.AppendLine("        entity.UpdateFromDto(dto);");
        if (!uowOwnsCommit)
            sb.AppendLine("        await _db.SaveChangesAsync(ct).ConfigureAwait(false);");
        sb.AppendLine("    }");
        sb.AppendLine();

        // DeleteAsync
        sb.AppendLine($"    public async Task DeleteAsync({pkType} id, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var entity = await _db.{dbset}");
        sb.AppendLine($"            .FirstOrDefaultAsync(e => e.{pkProp} == id, ct).ConfigureAwait(false);");
        sb.AppendLine("        if (entity is null) { return; }");
        sb.AppendLine($"        _db.{dbset}.Remove(entity);");
        if (!uowOwnsCommit)
            sb.AppendLine("        await _db.SaveChangesAsync(ct).ConfigureAwait(false);");
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
