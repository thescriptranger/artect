using System;
using System.Collections.Generic;

namespace Artect.Naming;

public static class Pluralizer
{
    static readonly HashSet<string> Uncountable = new(StringComparer.OrdinalIgnoreCase)
    {
        "equipment","information","rice","money","species","series","fish","sheep","news","data"
    };
    static readonly Dictionary<string,string> Irregular = new(StringComparer.OrdinalIgnoreCase)
    {
        ["person"]="people",["man"]="men",["woman"]="women",["child"]="children",
        ["tooth"]="teeth",["foot"]="feet",["mouse"]="mice",["goose"]="geese"
    };

    // Singular endings that the trailing-s rule must NOT strip.
    // Status, Bus, Crisis, Atlas, Octopus, Bonus — the trailing 's' is part of the singular
    // form, not a plural marker. Without this guard, Singularize("Status") returns "Statu",
    // which propagates through every emitter as a malformed entity name.
    static readonly string[] AlreadySingularSuffixes = { "us", "is", "as", "os" };

    public static string Pluralize(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;
        if (Uncountable.Contains(word)) return word;
        if (Irregular.TryGetValue(word, out var irr)) return PreserveCase(word, irr);
        var lower = word.ToLowerInvariant();
        string plural =
            lower.EndsWith("s") || lower.EndsWith("x") || lower.EndsWith("z") || lower.EndsWith("ch") || lower.EndsWith("sh") ? word + "es" :
            (lower.EndsWith("y") && word.Length >= 2 && !"aeiou".Contains(word[^2])) ? word[..^1] + "ies" :
            lower.EndsWith("f") ? word[..^1] + "ves" :
            lower.EndsWith("fe") ? word[..^2] + "ves" :
            word + "s";
        return plural;
    }

    public static string Singularize(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;
        if (Uncountable.Contains(word)) return word;
        foreach (var pair in Irregular)
            if (string.Equals(pair.Value, word, StringComparison.OrdinalIgnoreCase))
                return PreserveCase(word, pair.Key);
        var lower = word.ToLowerInvariant();
        if (lower.EndsWith("ies") && word.Length > 3) return word[..^3] + "y";
        if (lower.EndsWith("ves")) return word[..^3] + "f";
        if (lower.EndsWith("ses") || lower.EndsWith("xes") || lower.EndsWith("zes") || lower.EndsWith("ches") || lower.EndsWith("shes"))
            return word[..^2];
        // Guard: words like "Status", "Bus", "Crisis", "Atlas" already are singular; the
        // generic trailing-s rule below would otherwise strip them to "Statu", "Bu", etc.
        foreach (var suffix in AlreadySingularSuffixes)
            if (lower.EndsWith(suffix)) return word;
        if (lower.EndsWith("s") && !lower.EndsWith("ss")) return word[..^1];
        return word;
    }

    static string PreserveCase(string source, string replacement) =>
        char.IsUpper(source[0]) ? char.ToUpperInvariant(replacement[0]) + replacement[1..] : replacement;
}
