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
/// No EF Core or Dapper types — BCL + app DTOs only (PRD §4.7 FR-23).
/// </summary>
public sealed class RepositoryInterfaceEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        if (!ctx.Config.EmitRepositoriesAndAbstractions)
            return System.Array.Empty<EmittedFile>();

        var list    = new List<EmittedFile>();
        var project = ctx.Config.ProjectName;
        var ns      = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var dtoNs   = $"{CleanLayout.ApplicationNamespace(project)}.Dtos";
        var respNs  = $"{CleanLayout.SharedNamespace(project)}.Responses";

        // Tables (via NamedEntity)
        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.IsJoinTable) continue;

            var content = entity.HasPrimaryKey
                ? BuildFullInterface(entity, ns, dtoNs, respNs)
                : BuildListOnlyInterface(entity.EntityTypeName, ns, dtoNs, respNs);

            var path = CleanLayout.RepositoryInterfacePath(project, entity.EntityTypeName);
            list.Add(new EmittedFile(path, content));
        }

        // Views — list-only
        foreach (var view in ctx.Graph.Views)
        {
            var typeName = CasingHelper.ToPascalCase(Pluralizer.Singularize(view.Name));
            var content  = BuildListOnlyInterface(typeName, ns, dtoNs, respNs);
            var path     = CleanLayout.RepositoryInterfacePath(project, typeName);
            list.Add(new EmittedFile(path, content));
        }

        return list;
    }

    static string BuildFullInterface(NamedEntity entity, string ns, string dtoNs, string respNs)
    {
        var name   = entity.EntityTypeName;
        var pkType = PkClrType(entity.Table);

        var sb = new StringBuilder();
        sb.AppendLine($"using {dtoNs};");
        sb.AppendLine($"using {respNs};");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"public interface I{name}Repository");
        sb.AppendLine("{");
        sb.AppendLine($"    Task<PagedResponse<{name}Dto>> ListAsync(int page, int pageSize, CancellationToken ct);");
        sb.AppendLine($"    Task<{name}Dto?> GetByIdAsync({pkType} id, CancellationToken ct);");
        sb.AppendLine($"    Task<{name}Dto> CreateAsync({name}Dto dto, CancellationToken ct);");
        sb.AppendLine($"    Task UpdateAsync({name}Dto dto, CancellationToken ct);");
        sb.AppendLine($"    Task DeleteAsync({pkType} id, CancellationToken ct);");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string BuildListOnlyInterface(string typeName, string ns, string dtoNs, string respNs)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"using {dtoNs};");
        sb.AppendLine($"using {respNs};");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"// No primary key — list-only.");
        sb.AppendLine($"public interface I{typeName}Repository");
        sb.AppendLine("{");
        sb.AppendLine($"    Task<PagedResponse<{typeName}Dto>> ListAsync(int page, int pageSize, CancellationToken ct);");
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
