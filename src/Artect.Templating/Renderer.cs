using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Artect.Templating.Ast;

namespace Artect.Templating;

public static class Renderer
{
    public static string Render(TemplateDocument doc, object context)
    {
        var sb = new StringBuilder();
        var scope = new Dictionary<string, object?>(StringComparer.Ordinal);
        RenderNodes(doc.Nodes, context, scope, sb);
        return sb.ToString();
    }

    static void RenderNodes(IReadOnlyList<TemplateNode> nodes, object context, Dictionary<string, object?> scope, StringBuilder sb)
    {
        foreach (var node in nodes) RenderNode(node, context, scope, sb);
    }

    static void RenderNode(TemplateNode node, object context, Dictionary<string, object?> scope, StringBuilder sb)
    {
        switch (node)
        {
            case TextNode tx:
                sb.Append(tx.Text);
                break;
            case VariableNode v:
                var value = Resolve(v.Path, context, scope);
                foreach (var f in v.Filters) value = Filters.Apply(value, f);
                sb.Append(value?.ToString() ?? string.Empty);
                break;
            case IfNode i:
                bool rendered = false;
                foreach (var (cond, body) in i.Branches)
                {
                    if (IsTruthy(Resolve(cond, context, scope)))
                    {
                        RenderNodes(body, context, scope, sb);
                        rendered = true;
                        break;
                    }
                }
                if (!rendered && i.ElseBody is not null) RenderNodes(i.ElseBody, context, scope, sb);
                break;
            case ForNode f:
                var collection = Resolve(f.CollectionPath, context, scope) as IEnumerable;
                if (collection is null) break;
                foreach (var item in collection)
                {
                    scope[f.ItemName] = item;
                    RenderNodes(f.Body, context, scope, sb);
                }
                scope.Remove(f.ItemName);
                break;
        }
    }

    static bool IsTruthy(object? v) => v switch
    {
        null => false,
        bool b => b,
        string s => !string.IsNullOrEmpty(s),
        IEnumerable e => e.GetEnumerator().MoveNext(),
        _ => true,
    };

    public static object? Resolve(string path, object context, IReadOnlyDictionary<string, object?> scope)
    {
        var parts = path.Split('.');
        object? current = scope.TryGetValue(parts[0], out var scoped) ? scoped : GetMember(context, parts[0]);
        for (int i = 1; i < parts.Length; i++)
        {
            if (current is null) return null;
            current = GetMember(current, parts[i]);
        }
        return current;
    }

    static object? GetMember(object target, string name)
    {
        if (target is IDictionary<string, object?> dict)
            return dict.TryGetValue(name, out var v) ? v : null;
        var t = target.GetType();
        var prop = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (prop is not null) return prop.GetValue(target);
        var field = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
        return field?.GetValue(target);
    }
}
