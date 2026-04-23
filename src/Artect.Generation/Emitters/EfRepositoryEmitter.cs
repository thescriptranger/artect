using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits EF Core repository implementations.
/// Only runs when <c>cfg.DataAccess == EfCore</c> AND <c>cfg.EmitRepositoriesAndAbstractions == true</c>.
/// When <c>cfg.SplitRepositoriesByIntent == true</c> (default), emits two classes per entity:
///   <c>&lt;Entity&gt;ReadRepository  : I&lt;Entity&gt;ReadRepository</c>
///   <c>&lt;Entity&gt;WriteRepository : I&lt;Entity&gt;WriteRepository</c>
/// When false, emits the single monolithic <c>&lt;Entity&gt;Repository : I&lt;Entity&gt;Repository</c>.
/// </summary>
public sealed class EfRepositoryEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        if (ctx.Config.DataAccess != DataAccessKind.EfCore) return System.Array.Empty<EmittedFile>();
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
                    BuildReadRepository(ctx, entity, project)));
                list.Add(new EmittedFile(
                    CleanLayout.RepositoryWriteImplPath(project, entity.EntityTypeName),
                    BuildWriteRepository(ctx, entity, project)));
            }
            else
            {
                list.Add(new EmittedFile(
                    CleanLayout.RepositoryImplPath(project, entity.EntityTypeName),
                    BuildMonolithicRepository(ctx, entity, project)));
            }
        }

        return list;
    }

    // ── Split: Read repository ────────────────────────────────────────────────

    static string BuildReadRepository(EmitterContext ctx, NamedEntity entity, string project)
    {
        var corrections = ctx.NamingCorrections;
        var name     = entity.EntityTypeName;
        var dbset    = entity.DbSetPropertyName;
        var dbCtx    = $"{project}DbContext";
        var pkType   = PkClrType(entity.Table);
        var pkProp   = EntityNaming.PropertyName(
            entity.Table.Columns.First(c =>
                string.Equals(c.Name, entity.Table.PrimaryKey!.ColumnNames[0],
                    System.StringComparison.OrdinalIgnoreCase)),
            corrections);

        var ns        = $"{CleanLayout.InfrastructureNamespace(project)}.Repositories";
        var dataAbsNs = $"{CleanLayout.InfrastructureNamespace(project)}.Data";
        var repoAbsNs = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var commonNs  = CleanLayout.ApplicationCommonNamespace(project);
        var modelsNs  = CleanLayout.ApplicationModelsNamespace(project);

        var allCols = entity.Table.Columns.ToList();
        var entitiesByName = ctx.Model.Entities
            .Where(e => !e.IsJoinTable)
            .ToDictionary(e => e.EntityTypeName, e => e);
        var readChildNavs = ctx.Config.IncludeChildCollectionsInResponses
            ? entity.CollectionNavigations
            : (IReadOnlyList<NamedNavigation>)System.Array.Empty<NamedNavigation>();

        string ProjectionBody(string srcVar)
            => BuildProjection(srcVar, name, allCols, readChildNavs, entitiesByName, corrections, outerIndent: "            ");

        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        sb.AppendLine($"using {dataAbsNs};");
        sb.AppendLine($"using {repoAbsNs};");
        sb.AppendLine($"using {commonNs};");
        sb.AppendLine($"using {modelsNs};");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"public sealed class {name}ReadRepository : I{name}ReadRepository");
        sb.AppendLine("{");
        sb.AppendLine($"    private readonly {dbCtx} _db;");
        sb.AppendLine();
        sb.AppendLine($"    public {name}ReadRepository({dbCtx} db)");
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

        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── Split: Write repository ───────────────────────────────────────────────

    static string BuildWriteRepository(EmitterContext ctx, NamedEntity entity, string project)
    {
        var corrections = ctx.NamingCorrections;
        var name     = entity.EntityTypeName;
        var dbset    = entity.DbSetPropertyName;
        var dbCtx    = $"{project}DbContext";
        var pkType   = PkClrType(entity.Table);
        var pkProp   = EntityNaming.PropertyName(
            entity.Table.Columns.First(c =>
                string.Equals(c.Name, entity.Table.PrimaryKey!.ColumnNames[0],
                    System.StringComparison.OrdinalIgnoreCase)),
            corrections);

        var ns        = $"{CleanLayout.InfrastructureNamespace(project)}.Repositories";
        var dataAbsNs = $"{CleanLayout.InfrastructureNamespace(project)}.Data";
        var repoAbsNs = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var modelsNs  = CleanLayout.ApplicationModelsNamespace(project);
        var entityNs  = $"{CleanLayout.DomainNamespace(project)}.Entities";

        var allCols = entity.Table.Columns.ToList();
        var entitiesByName = ctx.Model.Entities
            .Where(e => !e.IsJoinTable)
            .ToDictionary(e => e.EntityTypeName, e => e);

        // Write path (CreateAsync return) never projects children — the newly-created
        // entity has no attached children. Pass an empty nav list regardless of flag.
        string ProjectionBody(string srcVar)
            => BuildProjection(srcVar, name, allCols,
                (IReadOnlyList<NamedNavigation>)System.Array.Empty<NamedNavigation>(),
                entitiesByName, corrections, outerIndent: "        ");

        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
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
        sb.AppendLine($"    private readonly {dbCtx} _db;");
        sb.AppendLine();
        sb.AppendLine($"    public {name}WriteRepository({dbCtx} db)");
        sb.AppendLine("    {");
        sb.AppendLine("        _db = db;");
        sb.AppendLine("    }");
        sb.AppendLine();

        // CreateAsync
        sb.AppendLine($"    // NOTE: Create calls SaveChangesAsync inline to materialize the server-generated id.");
        sb.AppendLine($"    // Update/Delete defer to IUnitOfWork.CommitAsync.");
        sb.AppendLine($"    public async Task<{name}Model> CreateAsync({name} entity, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine($"        await _db.{dbset}.AddAsync(entity, ct).ConfigureAwait(false);");
        sb.AppendLine("        await _db.SaveChangesAsync(ct).ConfigureAwait(false);");
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

    // ── Monolithic (SplitRepositoriesByIntent == false) ───────────────────────

    static string BuildMonolithicRepository(EmitterContext ctx, NamedEntity entity, string project)
    {
        var corrections = ctx.NamingCorrections;
        var name     = entity.EntityTypeName;
        var dbset    = entity.DbSetPropertyName;
        var dbCtx    = $"{project}DbContext";
        var pkType   = PkClrType(entity.Table);
        var pkProp   = EntityNaming.PropertyName(
            entity.Table.Columns.First(c =>
                string.Equals(c.Name, entity.Table.PrimaryKey!.ColumnNames[0],
                    System.StringComparison.OrdinalIgnoreCase)),
            corrections);

        var ns        = $"{CleanLayout.InfrastructureNamespace(project)}.Repositories";
        var dataAbsNs = $"{CleanLayout.InfrastructureNamespace(project)}.Data";
        var repoAbsNs = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var commonNs  = CleanLayout.ApplicationCommonNamespace(project);
        var modelsNs  = CleanLayout.ApplicationModelsNamespace(project);
        var entityNs  = $"{CleanLayout.DomainNamespace(project)}.Entities";

        var allCols = entity.Table.Columns.ToList();
        var entitiesByName = ctx.Model.Entities
            .Where(e => !e.IsJoinTable)
            .ToDictionary(e => e.EntityTypeName, e => e);
        var readChildNavs = ctx.Config.IncludeChildCollectionsInResponses
            ? entity.CollectionNavigations
            : (IReadOnlyList<NamedNavigation>)System.Array.Empty<NamedNavigation>();

        // Read projection (List + GetById) — includes child collections when flag is on.
        string ReadProjectionBody(string srcVar)
            => BuildProjection(srcVar, name, allCols, readChildNavs, entitiesByName, corrections, outerIndent: "            ");

        // Write projection (Create return) — never includes child collections;
        // the newly-created entity has no child rows yet.
        string WriteProjectionBody(string srcVar)
            => BuildProjection(srcVar, name, allCols,
                (IReadOnlyList<NamedNavigation>)System.Array.Empty<NamedNavigation>(),
                entitiesByName, corrections, outerIndent: "        ");

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
        sb.AppendLine($"            .Select(e => {ReadProjectionBody("e")})");
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
        sb.AppendLine($"            .Select(e => {ReadProjectionBody("e")})");
        sb.AppendLine("            .FirstOrDefaultAsync(ct).ConfigureAwait(false);");
        sb.AppendLine("    }");
        sb.AppendLine();

        // CreateAsync
        sb.AppendLine($"    public async Task<{name}Model> CreateAsync({name} entity, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine($"        await _db.{dbset}.AddAsync(entity, ct).ConfigureAwait(false);");
        sb.AppendLine($"        return {WriteProjectionBody("entity")};");
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

    /// <summary>
    /// Emits a <c>new {EntityName}Model { ... }</c> object-initializer projection.
    /// When <paramref name="childNavs"/> is non-empty, each collection navigation is
    /// projected inline using the child's scalar columns only — depth-1 expansion per
    /// PRD §4.2 prompt #14. Nested children default to empty via the Model record's
    /// <c>= System.Array.Empty&lt;&gt;()</c> initializer.
    /// </summary>
    static string BuildProjection(
        string srcVar,
        string entityTypeName,
        IReadOnlyList<Column> scalarCols,
        IReadOnlyList<NamedNavigation> childNavs,
        IReadOnlyDictionary<string, NamedEntity> entitiesByName,
        IReadOnlyDictionary<string, string> corrections,
        string outerIndent)
    {
        var innerIndent = outerIndent + "    ";
        var sb = new StringBuilder();
        sb.AppendLine($"new {entityTypeName}Model");
        sb.AppendLine($"{outerIndent}{{");
        foreach (var col in scalarCols)
        {
            var prop = EntityNaming.PropertyName(col, corrections);
            sb.AppendLine($"{innerIndent}{prop} = {srcVar}.{prop},");
        }
        foreach (var nav in childNavs)
        {
            if (!entitiesByName.TryGetValue(nav.TargetEntityTypeName, out var childEntity)) continue;
            sb.AppendLine($"{innerIndent}{nav.PropertyName} = {srcVar}.{nav.PropertyName}.Select(c => new {nav.TargetEntityTypeName}Model");
            sb.AppendLine($"{innerIndent}{{");
            foreach (var childCol in childEntity.Table.Columns)
            {
                var childProp = EntityNaming.PropertyName(childCol, corrections);
                sb.AppendLine($"{innerIndent}    {childProp} = c.{childProp},");
            }
            sb.AppendLine($"{innerIndent}}}).ToArray(),");
        }
        sb.Append($"{outerIndent}}}");
        return sb.ToString();
    }
}
