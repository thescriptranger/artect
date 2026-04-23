using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Artect.Naming;
using Artect.Templating;

namespace Artect.Generation.Emitters;

public sealed class EnumEmitter : IEmitter
{
    // Matches: [colName] IN (...) or colName IN (...)
    // Values may be quoted with single quotes, optionally prefixed with N.
    static readonly Regex InListPattern = new(
        @"^\s*\[?(?<col>[^\]\s]+)\]?\s+IN\s*\((?<vals>[^)]+)\)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex ValuePattern = new(
        @"N?'(?<val>[^']*)'",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var template = TemplateParser.Parse(ctx.Templates.Load("Enum.cs.artect"));
        var list = new List<EmittedFile>();
        var emitted = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var entity in ctx.Model.Entities)
        {
            foreach (var ck in entity.Table.CheckConstraints)
            {
                var parsed = TryParseInList(ck.Expression);
                if (parsed is null) continue;

                // Verify the column exists in this table
                var col = entity.Table.Columns.FirstOrDefault(c =>
                    string.Equals(c.Name, parsed.Value.Column, System.StringComparison.OrdinalIgnoreCase));
                if (col is null) continue;

                var enumName = CasingHelper.ToPascalCase(entity.EntityTypeName, ctx.NamingCorrections)
                    + CasingHelper.ToPascalCase(parsed.Value.Column, ctx.NamingCorrections)
                    + "Enum";

                // Deduplicate across all entities
                if (!emitted.Add(enumName)) continue;

                var ns = $"{CleanLayout.SharedNamespace(ctx.Config.ProjectName)}.Enums";
                var data = new
                {
                    Namespace = ns,
                    EnumName = enumName,
                    Values = parsed.Value.Values
                        .Select(v => CasingHelper.ToPascalCase(v, ctx.NamingCorrections))
                        .ToList(),
                };
                var rendered = Renderer.Render(template, data);
                var path = CleanLayout.SharedEnumPath(ctx.Config.ProjectName, enumName);
                list.Add(new EmittedFile(path, rendered));
            }
        }
        return list;
    }

    static (string Column, IReadOnlyList<string> Values)? TryParseInList(string expression)
    {
        var m = InListPattern.Match(expression);
        if (!m.Success) return null;

        var col = m.Groups["col"].Value;
        var valsPart = m.Groups["vals"].Value;

        var values = new List<string>();
        foreach (Match vm in ValuePattern.Matches(valsPart))
        {
            var v = vm.Groups["val"].Value;
            if (!string.IsNullOrEmpty(v)) values.Add(v);
        }

        if (values.Count == 0) return null;
        return (col, values);
    }
}
