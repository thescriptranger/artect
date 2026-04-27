using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits per-entity &lt;Entity&gt;ReadService EF Core implementations into
/// Infrastructure/Data/&lt;Plural&gt;/. Reads via AsNoTracking and projects
/// directly to &lt;Entity&gt;Dto using inline LINQ.
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
            if (entity.IsJoinTable) continue;
            if (!entity.HasPrimaryKey) continue;
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
        var pkColName = pk.ColumnNames[0];
        var pkCol = entity.Table.Columns.First(c =>
            string.Equals(c.Name, pkColName, System.StringComparison.OrdinalIgnoreCase));
        var pkProp = EntityNaming.PropertyName(pkCol, corrections);
        var pkType = SqlTypeMap.ToCs(pkCol.ClrType);

        var allCols = entity.Table.Columns.ToList();

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
        var infraDataNs = $"{CleanLayout.InfrastructureNamespace(project)}.Data";
        var classNs     = CleanLayout.InfrastructureDataEntityNamespace(project, name);

        var sb = new StringBuilder();
        sb.AppendLine($"using {entityNs};");
        sb.AppendLine($"using {dtosNs};");
        sb.AppendLine($"using {absNs};");
        sb.AppendLine($"using {infraDataNs};");
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        sb.AppendLine();
        sb.AppendLine($"namespace {classNs};");
        sb.AppendLine();
        sb.AppendLine($"public sealed class {name}ReadService({dbCtx} db) : I{name}ReadService");
        sb.AppendLine("{");

        if ((crud & CrudOperation.GetList) != 0)
        {
            EmitGetPaged(sb, name, dbset, allCols, corrections, childProjections);
            if ((crud & CrudOperation.GetById) != 0) sb.AppendLine();
        }
        if ((crud & CrudOperation.GetById) != 0)
            EmitGetById(sb, name, dbset, pkProp, pkType, allCols, corrections, childProjections);

        sb.AppendLine("}");

        var path = CleanLayout.InfrastructureDataEntityPath(project, name, $"{name}ReadService");
        return new EmittedFile(path, sb.ToString());
    }

    static void EmitGetPaged(StringBuilder sb, string name, string dbset, IReadOnlyList<Column> allCols,
        IReadOnlyDictionary<string, string> corrections, IReadOnlyList<string> childProjections)
    {
        sb.AppendLine($"    public async Task<(IReadOnlyList<{name}Dto> Items, int TotalCount)> GetPagedAsync(int page, int pageSize, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine($"        IQueryable<{name}> query = db.{dbset}.AsNoTracking();");
        sb.AppendLine("        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);");
        sb.AppendLine("        var items = await query");
        sb.AppendLine("            .OrderBy(e => e." + EntityNaming.PropertyName(allCols[0], corrections) + ")");
        sb.AppendLine("            .Skip((page - 1) * pageSize)");
        sb.AppendLine("            .Take(pageSize)");
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
        sb.AppendLine("            .ToListAsync(ct)");
        sb.AppendLine("            .ConfigureAwait(false);");
        sb.AppendLine("        return (items, totalCount);");
        sb.AppendLine("    }");
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
