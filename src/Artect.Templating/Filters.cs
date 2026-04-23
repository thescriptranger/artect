using System;
using System.Collections.Generic;
using System.Globalization;

namespace Artect.Templating;

public static class Filters
{
    static readonly Dictionary<string, Func<object?, string?, string>> Registry = new(StringComparer.Ordinal)
    {
        ["Humanize"] = (v, _) => Humanize(AsString(v)),
        ["ToPascalCase"] = (v, _) => Artect.Naming.CasingHelper.ToPascalCase(AsString(v)),
        ["ToCamelCase"] = (v, _) => Artect.Naming.CasingHelper.ToCamelCase(AsString(v)),
        ["ToKebabCase"] = (v, _) => Artect.Naming.CasingHelper.ToKebabCase(AsString(v)),
        ["ToSnakeCase"] = (v, _) => Artect.Naming.CasingHelper.ToSnakeCase(AsString(v)),
        ["Pluralize"] = (v, _) => Artect.Naming.Pluralizer.Pluralize(AsString(v)),
        ["Singularize"] = (v, _) => Artect.Naming.Pluralizer.Singularize(AsString(v)),
        ["Lower"] = (v, _) => AsString(v).ToLowerInvariant(),
        ["Upper"] = (v, _) => AsString(v).ToUpperInvariant(),
        ["Indent"] = (v, arg) => IndentText(AsString(v), int.Parse(arg ?? "4", CultureInfo.InvariantCulture)),
    };

    public static string Apply(object? value, string filterExpr)
    {
        var parenIdx = filterExpr.IndexOf('(');
        string name = parenIdx < 0 ? filterExpr : filterExpr[..parenIdx];
        string? arg = parenIdx < 0 ? null : filterExpr[(parenIdx + 1)..].TrimEnd(')');
        if (!Registry.TryGetValue(name, out var fn))
            throw new TemplateException($"Unknown filter '{name}'");
        return fn(value, arg);
    }

    static string AsString(object? v) => v?.ToString() ?? string.Empty;

    static string Humanize(string s)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (i > 0 && char.IsUpper(s[i]) && !char.IsUpper(s[i - 1])) sb.Append(' ');
            sb.Append(s[i]);
        }
        var r = sb.ToString();
        return r.Length == 0 ? r : char.ToUpperInvariant(r[0]) + r[1..].ToLowerInvariant();
    }

    static string IndentText(string text, int spaces)
    {
        var prefix = new string(' ', spaces);
        return prefix + text.Replace("\n", "\n" + prefix);
    }
}
