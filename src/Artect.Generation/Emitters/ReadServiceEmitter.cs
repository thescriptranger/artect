using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits per-entity &lt;Entity&gt;ReadService EF Core implementations into
/// Infrastructure/Data/&lt;Plural&gt;/. Reads via AsNoTracking and projects directly
/// to &lt;Entity&gt;Dto using inline LINQ.
///
/// V#11 production safeguards:
/// <list type="bullet">
/// <item><c>cfg.MaxPageSize</c> is enforced inside <c>GetPagedAsync</c> as a hard
///   clamp. Callers can pass any positive integer; the service caps it.</item>
/// <item>Default ordering is the entity's primary key for stable, deterministic
///   pagination (no duplicates, no skipped rows under concurrent writes).</item>
/// <item>The optional <c>sort</c> query parameter is parsed against a per-entity
///   allowlist (non-Ignored, non-Sensitive columns). Unknown fields throw
///   <c>QueryValidationException</c> which the GlobalExceptionHandler maps to a
///   400 ValidationProblemDetails.</item>
/// </list>
/// </summary>
public sealed class ReadServiceEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        if (ctx.Config.DataAccess != DataAccessKind.EfCore) return System.Array.Empty<EmittedFile>();

        var crud = ctx.Config.Crud;
        if ((crud & (CrudOperation.GetList | CrudOperation.GetById)) == 0)
            return System.Array.Empty<EmittedFile>();

        var list = new List<EmittedFile>();
        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.ShouldSkip(EntityClassification.AggregateRoot, EntityClassification.ReadModel)) continue;
            list.Add(Build(ctx, entity));
        }
        return list;
    }

    static EmittedFile Build(EmitterContext ctx, NamedEntity entity)
    {
        var project = ctx.Config.ProjectName;
        var crud    = ctx.Config.Crud;
        var name    = entity.EntityTypeName;
        var dbset   = entity.DbSetPropertyName;
        var dbCtx   = $"{project}DbContext";
        var corrections = ctx.NamingCorrections;

        var pk = entity.Table.PrimaryKey!;
        var pkColumns = pk.ColumnNames
            .Select(n => entity.Table.Columns.First(c =>
                string.Equals(c.Name, n, System.StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var pkProp = EntityNaming.PropertyName(pkColumns[0], corrections);
        var pkType = SqlTypeMap.ToCs(pkColumns[0].ClrType);

        var allCols = entity.Table.Columns.ToList();
        // V#11: sortable fields are the visible columns (non-Ignored, non-Sensitive).
        // Sorting on Sensitive columns leaks ordering info through pagination, so we
        // refuse it. Composition is fine: the user can flag a column Sensitive purely
        // to keep it out of sort URLs without affecting the Response (already handled
        // by V#1).
        var sortableCols = allCols
            .Where(c => !entity.ColumnHasFlag(c.Name, ColumnMetadata.Ignored))
            .Where(c => !entity.ColumnHasFlag(c.Name, ColumnMetadata.Sensitive))
            .ToList();

        var includeChildren = ctx.Config.IncludeChildCollectionsInResponses;
        var childProjections = includeChildren
            ? entity.CollectionNavigations
                .Select(nav => BuildChildProjection(ctx, nav, corrections))
                .Where(s => s is not null)
                .Select(s => s!)
                .ToList()
            : new List<string>();

        var entityNs    = $"{CleanLayout.DomainNamespace(project)}.Entities";
        var dtosNs      = CleanLayout.ApplicationDtosNamespace(project);
        var absNs       = CleanLayout.ApplicationFeatureAbstractionsNamespace(project, name);
        var appAbsNs    = CleanLayout.ApplicationAbstractionsNamespace(project);
        var infraDataNs = $"{CleanLayout.InfrastructureNamespace(project)}.Data";
        var classNs     = CleanLayout.InfrastructureDataEntityNamespace(project, name);

        var sb = new StringBuilder();
        sb.AppendLine($"using {entityNs};");
        sb.AppendLine($"using {dtosNs};");
        sb.AppendLine($"using {absNs};");
        sb.AppendLine($"using {appAbsNs};");
        sb.AppendLine($"using {infraDataNs};");
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        sb.AppendLine();
        sb.AppendLine($"namespace {classNs};");
        sb.AppendLine();
        sb.AppendLine($"public sealed class {name}ReadService({dbCtx} db) : I{name}ReadService");
        sb.AppendLine("{");

        if ((crud & CrudOperation.GetList) != 0)
        {
            EmitAllowedSortFields(sb, sortableCols, corrections);
            sb.AppendLine();
            EmitGetPaged(sb, name, dbset, allCols, pkProp, ctx.Config.MaxPageSize, corrections);
            EmitParseSort(sb);
            EmitApplyFirstSort(sb, name, sortableCols, corrections);
            EmitApplyChainedSort(sb, name, sortableCols, corrections);
            if ((crud & CrudOperation.GetById) != 0) sb.AppendLine();
        }
        if ((crud & CrudOperation.GetById) != 0)
            EmitGetById(sb, name, dbset, pkProp, pkType, allCols, corrections, childProjections);

        sb.AppendLine("}");

        var path = CleanLayout.InfrastructureDataEntityPath(project, name, $"{name}ReadService");
        return new EmittedFile(path, sb.ToString());
    }

    static void EmitAllowedSortFields(StringBuilder sb, IReadOnlyList<Column> sortableCols, IReadOnlyDictionary<string, string> corrections)
    {
        sb.AppendLine("    private static readonly System.Collections.Generic.IReadOnlyDictionary<string, string> _allowedSortFields =");
        sb.AppendLine("        new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)");
        sb.AppendLine("        {");
        foreach (var col in sortableCols)
        {
            var prop = EntityNaming.PropertyName(col, corrections);
            sb.AppendLine($"            [\"{prop}\"] = \"{prop}\",");
        }
        sb.AppendLine("        };");
    }

    static void EmitGetPaged(StringBuilder sb, string name, string dbset, IReadOnlyList<Column> allCols,
        string pkProp, int maxPageSize,
        IReadOnlyDictionary<string, string> corrections)
    {
        sb.AppendLine($"    public async Task<(IReadOnlyList<{name}Dto> Items, int TotalCount)> GetPagedAsync(int page, int pageSize, string? sort, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (page < 1) page = 1;");
        sb.AppendLine("        if (pageSize < 1) pageSize = 50;");
        sb.AppendLine($"        if (pageSize > {maxPageSize}) pageSize = {maxPageSize};");
        sb.AppendLine();
        sb.AppendLine("        var sortItems = ParseSort(sort);");
        sb.AppendLine();
        sb.AppendLine($"        IQueryable<{name}> query = db.{dbset}.AsNoTracking();");
        sb.AppendLine("        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);");
        sb.AppendLine();
        sb.AppendLine($"        IOrderedQueryable<{name}> ordered;");
        sb.AppendLine("        if (sortItems.Count == 0)");
        sb.AppendLine("        {");
        sb.AppendLine($"            ordered = query.OrderBy(e => e.{pkProp});");
        sb.AppendLine("        }");
        sb.AppendLine("        else");
        sb.AppendLine("        {");
        sb.AppendLine("            var (firstField, firstDesc) = sortItems[0];");
        sb.AppendLine("            ordered = ApplyFirstSort(query, firstField, firstDesc);");
        sb.AppendLine("            for (var i = 1; i < sortItems.Count; i++)");
        sb.AppendLine("            {");
        sb.AppendLine("                var (field, desc) = sortItems[i];");
        sb.AppendLine("                ordered = ApplyChainedSort(ordered, field, desc);");
        sb.AppendLine("            }");
        sb.AppendLine($"            ordered = ordered.ThenBy(e => e.{pkProp});");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        var items = await ordered");
        sb.AppendLine("            .Skip((page - 1) * pageSize)");
        sb.AppendLine("            .Take(pageSize)");
        sb.AppendLine($"            .Select(e => new {name}Dto");
        sb.AppendLine("            {");
        foreach (var col in allCols)
        {
            var prop = EntityNaming.PropertyName(col, corrections);
            sb.AppendLine($"                {prop} = e.{prop},");
        }
        sb.AppendLine("            })");
        sb.AppendLine("            .ToListAsync(ct)");
        sb.AppendLine("            .ConfigureAwait(false);");
        sb.AppendLine("        return (items, totalCount);");
        sb.AppendLine("    }");
    }

    static void EmitParseSort(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("    private static System.Collections.Generic.List<(string Field, bool Descending)> ParseSort(string? sort)");
        sb.AppendLine("    {");
        sb.AppendLine("        var items = new System.Collections.Generic.List<(string, bool)>();");
        sb.AppendLine("        if (string.IsNullOrWhiteSpace(sort)) return items;");
        sb.AppendLine("        foreach (var raw in sort.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries))");
        sb.AppendLine("        {");
        sb.AppendLine("            var desc = raw.StartsWith('-');");
        sb.AppendLine("            var field = desc ? raw[1..].Trim() : raw;");
        sb.AppendLine("            if (!_allowedSortFields.TryGetValue(field, out var canonical))");
        sb.AppendLine("                throw new QueryValidationException(\"sort\",");
        sb.AppendLine("                    $\"Unknown sort field: '{field}'. Allowed: {string.Join(\", \", _allowedSortFields.Values)}.\");");
        sb.AppendLine("            items.Add((canonical, desc));");
        sb.AppendLine("        }");
        sb.AppendLine("        return items;");
        sb.AppendLine("    }");
    }

    static void EmitApplyFirstSort(StringBuilder sb, string name, IReadOnlyList<Column> sortableCols, IReadOnlyDictionary<string, string> corrections)
    {
        sb.AppendLine();
        sb.AppendLine($"    private static IOrderedQueryable<{name}> ApplyFirstSort(IQueryable<{name}> q, string field, bool descending) =>");
        sb.AppendLine("        (field, descending) switch");
        sb.AppendLine("        {");
        foreach (var col in sortableCols)
        {
            var prop = EntityNaming.PropertyName(col, corrections);
            sb.AppendLine($"            (\"{prop}\", false) => q.OrderBy(e => e.{prop}),");
            sb.AppendLine($"            (\"{prop}\", true)  => q.OrderByDescending(e => e.{prop}),");
        }
        sb.AppendLine("            _ => throw new System.InvalidOperationException(\"Unreachable: sort field allowlist already validated.\"),");
        sb.AppendLine("        };");
    }

    static void EmitApplyChainedSort(StringBuilder sb, string name, IReadOnlyList<Column> sortableCols, IReadOnlyDictionary<string, string> corrections)
    {
        sb.AppendLine();
        sb.AppendLine($"    private static IOrderedQueryable<{name}> ApplyChainedSort(IOrderedQueryable<{name}> q, string field, bool descending) =>");
        sb.AppendLine("        (field, descending) switch");
        sb.AppendLine("        {");
        foreach (var col in sortableCols)
        {
            var prop = EntityNaming.PropertyName(col, corrections);
            sb.AppendLine($"            (\"{prop}\", false) => q.ThenBy(e => e.{prop}),");
            sb.AppendLine($"            (\"{prop}\", true)  => q.ThenByDescending(e => e.{prop}),");
        }
        sb.AppendLine("            _ => throw new System.InvalidOperationException(\"Unreachable: sort field allowlist already validated.\"),");
        sb.AppendLine("        };");
    }

    static void EmitGetById(StringBuilder sb, string name, string dbset, string pkProp, string pkType,
        IReadOnlyList<Column> allCols, IReadOnlyDictionary<string, string> corrections,
        IReadOnlyList<string> childProjections)
    {
        sb.AppendLine($"    public Task<{name}Dto?> GetByIdAsync({pkType} id, CancellationToken ct) =>");
        sb.AppendLine($"        db.{dbset}.AsNoTracking()");
        sb.AppendLine($"            .Where(e => e.{pkProp} == id)");
        sb.AppendLine($"            .Select(e => new {name}Dto");
        sb.AppendLine("            {");
        foreach (var col in allCols)
        {
            var prop = EntityNaming.PropertyName(col, corrections);
            sb.AppendLine($"                {prop} = e.{prop},");
        }
        foreach (var childLine in childProjections)
            sb.AppendLine($"                {childLine}");
        sb.AppendLine("            })");
        sb.AppendLine("            .FirstOrDefaultAsync(ct);");
    }

    static string? BuildChildProjection(EmitterContext ctx, NamedNavigation nav,
        IReadOnlyDictionary<string, string> corrections)
    {
        var childEntity = ctx.Model.Entities.FirstOrDefault(en =>
            string.Equals(en.EntityTypeName, nav.TargetEntityTypeName, System.StringComparison.Ordinal));
        if (childEntity is null) return null;

        var childCols = childEntity.Table.Columns
            .Select(c => $"{EntityNaming.PropertyName(c, corrections)} = c.{EntityNaming.PropertyName(c, corrections)}");
        var inits = string.Join(", ", childCols);
        return $"{nav.PropertyName} = e.{nav.PropertyName}.Select(c => new {nav.TargetEntityTypeName}Dto {{ {inits} }}).ToList(),";
    }
}
