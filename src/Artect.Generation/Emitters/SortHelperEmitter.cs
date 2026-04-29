using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

public sealed class SortHelperEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        if (ctx.Config.DataAccess != DataAccessKind.EfCore) return System.Array.Empty<EmittedFile>();
        if ((ctx.Config.Crud & CrudOperation.GetList) == 0) return System.Array.Empty<EmittedFile>();

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
        var name = entity.EntityTypeName;
        var typeRef = EntityTypeRef.For(name, project);
        var corrections = ctx.NamingCorrections;

        var sortableCols = entity.Table.Columns
            .Where(c => !entity.ColumnHasFlag(c.Name, ColumnMetadata.Ignored))
            .Where(c => !entity.ColumnHasFlag(c.Name, ColumnMetadata.Sensitive))
            .ToList();

        var entityNs = $"{CleanLayout.DomainNamespace(project)}.Entities";
        var appAbsNs = CleanLayout.ApplicationAbstractionsNamespace(project);
        var classNs  = CleanLayout.InfrastructureDataEntityNamespace(project, name);

        var sb = new StringBuilder();
        sb.AppendLine($"using {entityNs};");
        sb.AppendLine($"using {appAbsNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {classNs};");
        sb.AppendLine();
        sb.AppendLine($"internal static class {name}SortHelper");
        sb.AppendLine("{");
        sb.AppendLine("    private static readonly System.Collections.Generic.IReadOnlyDictionary<string, string> AllowedFields =");
        sb.AppendLine("        new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)");
        sb.AppendLine("        {");
        foreach (var col in sortableCols)
        {
            var prop = EntityNaming.PropertyName(col, corrections);
            sb.AppendLine($"            [\"{prop}\"] = \"{prop}\",");
        }
        sb.AppendLine("        };");
        sb.AppendLine();
        sb.AppendLine("    public static System.Collections.Generic.List<(string Field, bool Descending)> Parse(string? sort)");
        sb.AppendLine("    {");
        sb.AppendLine("        var items = new System.Collections.Generic.List<(string, bool)>();");
        sb.AppendLine("        if (string.IsNullOrWhiteSpace(sort)) return items;");
        sb.AppendLine("        foreach (var raw in sort.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries))");
        sb.AppendLine("        {");
        sb.AppendLine("            var desc = raw.StartsWith('-');");
        sb.AppendLine("            var field = desc ? raw[1..].Trim() : raw;");
        sb.AppendLine("            if (!AllowedFields.TryGetValue(field, out var canonical))");
        sb.AppendLine("                throw new QueryValidationException(\"sort\",");
        sb.AppendLine("                    $\"Unknown sort field: '{field}'. Allowed: {string.Join(\", \", AllowedFields.Values)}.\");");
        sb.AppendLine("            items.Add((canonical, desc));");
        sb.AppendLine("        }");
        sb.AppendLine("        return items;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    public static System.Linq.IOrderedQueryable<{typeRef}> ApplyFirst(System.Linq.IQueryable<{typeRef}> q, string field, bool descending) =>");
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
        sb.AppendLine();
        sb.AppendLine($"    public static System.Linq.IOrderedQueryable<{typeRef}> ApplyChained(System.Linq.IOrderedQueryable<{typeRef}> q, string field, bool descending) =>");
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
        sb.AppendLine("}");

        var path = CleanLayout.InfrastructureDataEntityPath(project, name, $"{name}SortHelper");
        return new EmittedFile(path, sb.ToString());
    }
}
