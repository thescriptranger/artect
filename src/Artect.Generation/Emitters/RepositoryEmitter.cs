using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits per-entity &lt;Entity&gt;Repository EF Core implementations into
/// Infrastructure/Data/&lt;Plural&gt;/. Stages changes against the DbContext —
/// commit happens once per use case via EfUnitOfWork.
/// </summary>
public sealed class RepositoryEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        if (ctx.Config.DataAccess != DataAccessKind.EfCore) return System.Array.Empty<EmittedFile>();

        var crud = ctx.Config.Crud;
        var writeNeeded = (crud & (CrudOperation.Post | CrudOperation.Put | CrudOperation.Patch | CrudOperation.Delete)) != 0;
        var readNeeded  = (crud & CrudOperation.GetById) != 0;
        if (!writeNeeded && !readNeeded) return System.Array.Empty<EmittedFile>();

        var list = new List<EmittedFile>();
        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.ShouldSkip(EntityClassification.AggregateRoot)) continue;
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

        var entityNs    = $"{CleanLayout.DomainNamespace(project)}.Entities";
        var absNs       = CleanLayout.ApplicationFeatureAbstractionsNamespace(project, name);
        var infraDataNs = $"{CleanLayout.InfrastructureNamespace(project)}.Data";
        var classNs     = CleanLayout.InfrastructureDataEntityNamespace(project, name);

        var sb = new StringBuilder();
        sb.AppendLine($"using {entityNs};");
        sb.AppendLine($"using {absNs};");
        sb.AppendLine($"using {infraDataNs};");
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        sb.AppendLine();
        sb.AppendLine($"namespace {classNs};");
        sb.AppendLine();
        sb.AppendLine($"public sealed class {name}Repository({dbCtx} db) : I{name}Repository");
        sb.AppendLine("{");
        sb.AppendLine($"    public Task<{name}?> GetByIdAsync({pkType} id, CancellationToken ct) =>");
        sb.AppendLine($"        db.{dbset}.FirstOrDefaultAsync(e => e.{pkProp} == id, ct);");
        sb.AppendLine();
        sb.AppendLine($"    public Task<bool> ExistsAsync({pkType} id, CancellationToken ct) =>");
        sb.AppendLine($"        db.{dbset}.AnyAsync(e => e.{pkProp} == id, ct);");

        foreach (var (prop, type) in RepositoryInterfaceEmitter.SingleColumnUniques(entity.Table, corrections))
        {
            sb.AppendLine();
            sb.AppendLine($"    public Task<bool> ExistsBy{prop}Async({type} value, CancellationToken ct) =>");
            sb.AppendLine($"        db.{dbset}.AnyAsync(e => e.{prop} == value, ct);");
        }

        if ((crud & CrudOperation.Post) != 0)
        {
            sb.AppendLine();
            sb.AppendLine($"    public async Task AddAsync({name} entity, CancellationToken ct) =>");
            sb.AppendLine($"        await db.{dbset}.AddAsync(entity, ct).ConfigureAwait(false);");
        }
        if ((crud & (CrudOperation.Put | CrudOperation.Patch)) != 0)
        {
            sb.AppendLine();
            sb.AppendLine($"    public void ApplyChanges({name} existing, {name} replacement) =>");
            sb.AppendLine("        db.Entry(existing).CurrentValues.SetValues(replacement);");
        }
        if ((crud & CrudOperation.Delete) != 0)
        {
            sb.AppendLine();
            sb.AppendLine($"    public void Remove({name} entity) =>");
            sb.AppendLine($"        db.{dbset}.Remove(entity);");
        }

        sb.AppendLine("}");

        var path = CleanLayout.InfrastructureDataEntityPath(project, name, $"{name}Repository");
        return new EmittedFile(path, sb.ToString());
    }
}
