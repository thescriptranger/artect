using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Artect.Naming;

public static class CasingHelper
{
    public static string ToPascalCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var words = SplitWords(input);
        var sb = new StringBuilder(input.Length);
        foreach (var w in words) sb.Append(Capitalize(w));
        return sb.ToString();
    }

    public static string ToCamelCase(string input)
    {
        var pascal = ToPascalCase(input);
        if (pascal.Length == 0) return pascal;
        return char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }

    public static string ToKebabCase(string input) => JoinWords(SplitWords(input), '-');
    public static string ToSnakeCase(string input) => JoinWords(SplitWords(input), '_');

    static IReadOnlyList<string> SplitWords(string input)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c == '_' || c == '-' || c == ' ')
            {
                if (current.Length > 0) { words.Add(current.ToString()); current.Clear(); }
                continue;
            }
            if (i > 0 && char.IsUpper(c))
            {
                char prev = input[i - 1];
                bool prevLower = char.IsLower(prev);
                bool prevDigit = char.IsDigit(prev);
                bool nextLower = i + 1 < input.Length && char.IsLower(input[i + 1]);
                bool prevUpper = char.IsUpper(prev);
                if (prevLower || prevDigit || (prevUpper && nextLower))
                {
                    if (current.Length > 0) { words.Add(current.ToString()); current.Clear(); }
                }
            }
            if (i > 0 && char.IsDigit(c) && !char.IsDigit(input[i - 1]))
            {
                if (current.Length > 0) { words.Add(current.ToString()); current.Clear(); }
            }
            current.Append(c);
        }
        if (current.Length > 0) words.Add(current.ToString());
        return words;
    }

    static string JoinWords(IReadOnlyList<string> words, char sep)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < words.Count; i++)
        {
            if (i > 0) sb.Append(sep);
            sb.Append(words[i].ToLowerInvariant());
        }
        return sb.ToString();
    }

    static string Capitalize(string w) =>
        w.Length == 0 ? w :
        w.Length == 1 ? w.ToUpperInvariant() :
        char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant();
}
