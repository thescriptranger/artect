using System;
using System.Collections.Generic;
using System.Linq;

namespace Artect.Console;

public sealed class Prompt
{
    readonly IConsoleIO _io;
    public Prompt(IConsoleIO io) => _io = io;

    public string AskString(string question, string defaultValue)
    {
        _io.Write($"{Ansi.Colour(question, Ansi.Cyan)} [{defaultValue}]: ");
        var answer = _io.ReadLine().Trim();
        return string.IsNullOrEmpty(answer) ? defaultValue : answer;
    }

    public bool AskBool(string question, bool defaultValue)
    {
        var def = defaultValue ? "Y/n" : "y/N";
        _io.Write($"{Ansi.Colour(question, Ansi.Cyan)} [{def}]: ");
        var answer = _io.ReadLine().Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(answer)) return defaultValue;
        return answer is "y" or "yes" or "true" or "1";
    }

    public T AskEnum<T>(string question, T defaultValue) where T : struct, Enum
    {
        var names = Enum.GetNames<T>().OrderBy(n => n, StringComparer.Ordinal).ToList();
        _io.WriteLine(Ansi.Colour(question, Ansi.Cyan));
        for (int i = 0; i < names.Count; i++)
        {
            var marker = names[i] == defaultValue.ToString() ? " (default)" : string.Empty;
            _io.WriteLine($"  {i + 1}. {names[i]}{marker}");
        }
        _io.Write("Choice (number or name): ");
        var answer = _io.ReadLine().Trim();
        if (string.IsNullOrEmpty(answer)) return defaultValue;
        if (int.TryParse(answer, out var n) && n >= 1 && n <= names.Count)
            return Enum.Parse<T>(names[n - 1]);
        if (Enum.TryParse<T>(answer, ignoreCase: true, out var parsed)) return parsed;
        _io.WriteLine(Ansi.Colour("  Unrecognized — using default.", Ansi.Dim));
        return defaultValue;
    }

    public IReadOnlyList<T> AskMultiEnum<T>(string question, IReadOnlyList<T> defaults) where T : struct, Enum
    {
        var names = Enum.GetNames<T>()
            .Where(n => n != "None" && n != "All")
            .OrderBy(n => n, StringComparer.Ordinal).ToList();
        _io.WriteLine(Ansi.Colour(question, Ansi.Cyan));
        for (int i = 0; i < names.Count; i++) _io.WriteLine($"  {i + 1}. {names[i]}");
        _io.WriteLine($"(default: {string.Join(", ", defaults.Select(d => d.ToString()))})");
        _io.Write("Choices (comma-separated numbers or names, or blank for default): ");
        var answer = _io.ReadLine().Trim();
        if (string.IsNullOrEmpty(answer)) return defaults;
        var result = new List<T>();
        foreach (var tok in answer.Split(','))
        {
            var t = tok.Trim();
            if (int.TryParse(t, out var n) && n >= 1 && n <= names.Count)
                result.Add(Enum.Parse<T>(names[n - 1]));
            else if (Enum.TryParse<T>(t, ignoreCase: true, out var parsed))
                result.Add(parsed);
        }
        return result.Count == 0 ? defaults : result;
    }

    public IReadOnlyList<string> AskMultiString(string question, IReadOnlyList<string> options, IReadOnlyList<string> defaults)
    {
        _io.WriteLine(Ansi.Colour(question, Ansi.Cyan));
        for (int i = 0; i < options.Count; i++) _io.WriteLine($"  {i + 1}. {options[i]}");
        _io.WriteLine($"(default: {string.Join(", ", defaults)})");
        _io.Write("Choices (comma-separated numbers, or blank for default): ");
        var answer = _io.ReadLine().Trim();
        if (string.IsNullOrEmpty(answer)) return defaults;
        var result = new List<string>();
        foreach (var tok in answer.Split(','))
        {
            if (int.TryParse(tok.Trim(), out var n) && n >= 1 && n <= options.Count)
                result.Add(options[n - 1]);
        }
        return result.Count == 0 ? defaults : result;
    }
}
