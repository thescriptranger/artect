using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits one &lt;Entity&gt;DataAccess class per entity into Infrastructure/Data/&lt;Plural&gt;/.
/// Implements both I&lt;Entity&gt;Queries and I&lt;Entity&gt;Commands. Uses the DbContext
/// directly; calls _db.SaveChangesAsync() inside command methods.
/// </summary>
public sealed class DataAccessEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        if (ctx.Config.DataAccess != DataAccessKind.EfCore) return System.Array.Empty<EmittedFile>();

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
        var project   = ctx.Config.ProjectName;
        var name      = entity.EntityTypeName;
        var dbset     = entity.DbSetPropertyName;
        var dbCtx     = $"{project}DbContext";
        var corrections = ctx.NamingCorrections;

        var crud      = ctx.Config.Crud;
        var dtosNs    = CleanLayout.ApplicationDtosNamespace(project);
        var featureNs = CleanLayout.ApplicationFeatureNamespace(project, name);
        var absNs     = CleanLayout.ApplicationFeatureAbstractionsNamespace(project, name);
        var entityNs  = $"{CleanLayout.DomainNamespace(project)}.Entities";
        var infraDataNs = $"{CleanLayout.InfrastructureNamespace(project)}.Data";
        var infraMapNs  = CleanLayout.InfrastructureMappingNamespace(project);
        var classNs   = CleanLayout.InfrastructureDataEntityNamespace(project, name);

        var pk = entity.Table.PrimaryKey!;
        var pkColName = pk.ColumnNames[0];
        var pkCol = entity.Table.Columns.First(c =>
            string.Equals(c.Name, pkColName, System.StringComparison.OrdinalIgnoreCase));
        var pkProp = EntityNaming.PropertyName(pkCol, corrections);
        var pkType = SqlTypeMap.ToCs(pkCol.ClrType);

        var allCols = entity.Table.Columns.ToList();
        var nonServerGen = allCols.Where(c => !c.IsServerGenerated).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"using {dtosNs};");
        sb.AppendLine($"using {featureNs};");
        sb.AppendLine($"using {absNs};");
        sb.AppendLine($"using {entityNs};");
        sb.AppendLine($"using {infraDataNs};");
        sb.AppendLine($"using {infraMapNs};");
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        sb.AppendLine();
        sb.AppendLine($"namespace {classNs};");
        sb.AppendLine();

        var ifaceList = new List<string>();
        if ((crud & (CrudOperation.GetList | CrudOperation.GetById)) != 0) ifaceList.Add($"I{name}Queries");
        if ((crud & (CrudOperation.Post | CrudOperation.Put | CrudOperation.Patch | CrudOperation.Delete)) != 0) ifaceList.Add($"I{name}Commands");
        var implements = ifaceList.Count == 0 ? string.Empty : $" : {string.Join(", ", ifaceList)}";

        sb.AppendLine($"public sealed class {name}DataAccess{implements}");
        sb.AppendLine("{");
        sb.AppendLine($"    private readonly {dbCtx} _db;");
        sb.AppendLine();
        sb.AppendLine($"    public {name}DataAccess({dbCtx} db) => _db = db;");
        sb.AppendLine();

        if ((crud & CrudOperation.GetList) != 0)
            EmitGetPaged(sb, name, dbset, allCols, corrections);
        if ((crud & CrudOperation.GetById) != 0)
            EmitGetById(sb, name, dbset, pkProp, pkType);
        if ((crud & CrudOperation.Post) != 0)
            EmitCreate(sb, name, dbset, nonServerGen, corrections);
        if ((crud & CrudOperation.Put) != 0)
            EmitUpdate(sb, name, dbset, pkProp, pkType, pkColName, nonServerGen, corrections);
        if ((crud & CrudOperation.Patch) != 0)
            EmitPatch(sb, name, dbset, pkProp, pkType, pkColName, nonServerGen, corrections);
        if ((crud & CrudOperation.Delete) != 0)
            EmitDelete(sb, name, dbset, pkProp, pkType);

        sb.AppendLine("}");

        var path = CleanLayout.InfrastructureDataEntityPath(project, name, $"{name}DataAccess");
        return new EmittedFile(path, sb.ToString());
    }

    static void EmitGetPaged(StringBuilder sb, string name, string dbset, IReadOnlyList<Column> allCols, System.Collections.Generic.IReadOnlyDictionary<string, string> corrections)
    {
        sb.AppendLine($"    public async Task<(IReadOnlyList<{name}Dto> Items, int TotalCount)> GetPagedAsync(int page, int pageSize, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine($"        IQueryable<{name}> query = _db.{dbset}.AsNoTracking();");
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
        sb.AppendLine("            })");
        sb.AppendLine("            .ToListAsync(ct)");
        sb.AppendLine("            .ConfigureAwait(false);");
        sb.AppendLine("        return (items, totalCount);");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    static void EmitGetById(StringBuilder sb, string name, string dbset, string pkProp, string pkType)
    {
        sb.AppendLine($"    public async Task<{name}Dto?> GetByIdAsync({pkType} id, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var entity = await _db.{dbset}.AsNoTracking()");
        sb.AppendLine($"            .FirstOrDefaultAsync(e => e.{pkProp} == id, ct)");
        sb.AppendLine("            .ConfigureAwait(false);");
        sb.AppendLine($"        return entity is null ? null : DtoMapper.Map<{name}, {name}Dto>(entity);");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    static void EmitCreate(StringBuilder sb, string name, string dbset, IReadOnlyList<Column> nonServerGen, System.Collections.Generic.IReadOnlyDictionary<string, string> corrections)
    {
        sb.AppendLine($"    public async Task<{name}Dto> CreateAsync(Create{name}Command command, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var entity = new {name}");
        sb.AppendLine("        {");
        foreach (var col in nonServerGen)
        {
            var prop = EntityNaming.PropertyName(col, corrections);
            sb.AppendLine($"            {prop} = command.{prop},");
        }
        sb.AppendLine("        };");
        sb.AppendLine($"        _db.{dbset}.Add(entity);");
        sb.AppendLine("        await _db.SaveChangesAsync(ct).ConfigureAwait(false);");
        sb.AppendLine($"        return DtoMapper.Map<{name}, {name}Dto>(entity);");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    static void EmitUpdate(StringBuilder sb, string name, string dbset, string pkProp, string pkType, string pkColName, IReadOnlyList<Column> nonServerGen, System.Collections.Generic.IReadOnlyDictionary<string, string> corrections)
    {
        sb.AppendLine($"    public async Task<{name}Dto?> UpdateAsync(Update{name}Command command, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var entity = await _db.{dbset}");
        sb.AppendLine($"            .FirstOrDefaultAsync(e => e.{pkProp} == command.{pkProp}, ct)");
        sb.AppendLine("            .ConfigureAwait(false);");
        sb.AppendLine("        if (entity is null) return null;");
        sb.AppendLine($"        var replacement = new {name}");
        sb.AppendLine("        {");
        sb.AppendLine($"            {pkProp} = command.{pkProp},");
        foreach (var col in nonServerGen.Where(c => !string.Equals(c.Name, pkColName, System.StringComparison.OrdinalIgnoreCase)))
        {
            var prop = EntityNaming.PropertyName(col, corrections);
            sb.AppendLine($"            {prop} = command.{prop},");
        }
        sb.AppendLine("        };");
        sb.AppendLine($"        _db.Entry(entity).CurrentValues.SetValues(replacement);");
        sb.AppendLine("        await _db.SaveChangesAsync(ct).ConfigureAwait(false);");
        sb.AppendLine($"        return DtoMapper.Map<{name}, {name}Dto>(entity);");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    static void EmitPatch(StringBuilder sb, string name, string dbset, string pkProp, string pkType, string pkColName, IReadOnlyList<Column> nonServerGen, System.Collections.Generic.IReadOnlyDictionary<string, string> corrections)
    {
        sb.AppendLine($"    public async Task<{name}Dto?> PatchAsync(Patch{name}Command command, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var entity = await _db.{dbset}");
        sb.AppendLine($"            .FirstOrDefaultAsync(e => e.{pkProp} == command.{pkProp}, ct)");
        sb.AppendLine("            .ConfigureAwait(false);");
        sb.AppendLine("        if (entity is null) return null;");
        sb.AppendLine($"        var replacement = new {name}");
        sb.AppendLine("        {");
        sb.AppendLine($"            {pkProp} = command.{pkProp},");
        foreach (var col in nonServerGen.Where(c => !string.Equals(c.Name, pkColName, System.StringComparison.OrdinalIgnoreCase)))
        {
            var prop = EntityNaming.PropertyName(col, corrections);
            sb.AppendLine($"            {prop} = command.{prop},");
        }
        sb.AppendLine("        };");
        sb.AppendLine($"        _db.Entry(entity).CurrentValues.SetValues(replacement);");
        sb.AppendLine("        await _db.SaveChangesAsync(ct).ConfigureAwait(false);");
        sb.AppendLine($"        return DtoMapper.Map<{name}, {name}Dto>(entity);");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    static void EmitDelete(StringBuilder sb, string name, string dbset, string pkProp, string pkType)
    {
        sb.AppendLine($"    public async Task<bool> DeleteAsync({pkType} id, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var entity = await _db.{dbset}");
        sb.AppendLine($"            .FirstOrDefaultAsync(e => e.{pkProp} == id, ct)");
        sb.AppendLine("            .ConfigureAwait(false);");
        sb.AppendLine("        if (entity is null) return false;");
        sb.AppendLine($"        _db.{dbset}.Remove(entity);");
        sb.AppendLine("        await _db.SaveChangesAsync(ct).ConfigureAwait(false);");
        sb.AppendLine("        return true;");
        sb.AppendLine("    }");
    }
}
