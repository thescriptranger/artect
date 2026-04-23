using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits <c>I&lt;Entity&gt;Repository</c> interfaces in <c>&lt;Project&gt;.Application.Abstractions.Repositories</c>.
/// Only runs when <c>cfg.EmitRepositoriesAndAbstractions == true</c>.
/// Entities with a PK get the full five-method contract; pk-less tables and views get list-only.
/// Uses Application-internal <c>&lt;Entity&gt;Model</c> and <c>PagedResult&lt;T&gt;</c> — no types from <c>&lt;Project&gt;.Shared.*</c>.
/// Create/Update take a Domain entity; everything else returns or takes a Model.
/// </summary>
public sealed class RepositoryInterfaceEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        if (!ctx.Config.EmitRepositoriesAndAbstractions)
            return System.Array.Empty<EmittedFile>();

        var list     = new List<EmittedFile>();
        var project  = ctx.Config.ProjectName;
        var ns       = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var commonNs = CleanLayout.ApplicationCommonNamespace(project);
        var modelsNs = CleanLayout.ApplicationModelsNamespace(project);
        var entityNs = $"{CleanLayout.DomainNamespace(project)}.Entities";

        // Tables (via NamedEntity)
        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.IsJoinTable) continue;

            var content = entity.HasPrimaryKey
                ? BuildFullInterface(entity, ns, commonNs, modelsNs, entityNs)
                : BuildListOnlyInterface(entity.EntityTypeName, ns, commonNs, modelsNs);

            var path = CleanLayout.RepositoryInterfacePath(project, entity.EntityTypeName);
            list.Add(new EmittedFile(path, content));
        }

        // Views — list-only
        foreach (var view in ctx.Graph.Views)
        {
            var typeName = CasingHelper.ToPascalCase(Pluralizer.Singularize(view.Name));
            var content  = BuildListOnlyInterface(typeName, ns, commonNs, modelsNs);
            var path     = CleanLayout.RepositoryInterfacePath(project, typeName);
            list.Add(new EmittedFile(path, content));
        }

        return list;
    }

    static string BuildFullInterface(NamedEntity entity, string ns, string commonNs, string modelsNs, string entityNs)
    {
        var name   = entity.EntityTypeName;
        var pkType = PkClrType(entity.Table);

        var sb = new StringBuilder();
        sb.AppendLine($"using {commonNs};");
        sb.AppendLine($"using {modelsNs};");
        sb.AppendLine($"using {entityNs};");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"public interface I{name}Repository");
        sb.AppendLine("{");
        sb.AppendLine($"    Task<PagedResult<{name}Model>> ListAsync(int page, int pageSize, CancellationToken ct);");
        sb.AppendLine($"    Task<{name}Model?> GetByIdAsync({pkType} id, CancellationToken ct);");
        sb.AppendLine($"    Task<{name}Model> CreateAsync({name} entity, CancellationToken ct);");
        sb.AppendLine($"    Task UpdateAsync({name} entity, CancellationToken ct);");
        sb.AppendLine($"    Task DeleteAsync({pkType} id, CancellationToken ct);");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string BuildListOnlyInterface(string typeName, string ns, string commonNs, string modelsNs)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"using {commonNs};");
        sb.AppendLine($"using {modelsNs};");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"// No primary key — list-only.");
        sb.AppendLine($"public interface I{typeName}Repository");
        sb.AppendLine("{");
        sb.AppendLine($"    Task<PagedResult<{typeName}Model>> ListAsync(int page, int pageSize, CancellationToken ct);");
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
