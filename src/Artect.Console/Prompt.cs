using System;
using System.Collections.Generic;
using System.Linq;

namespace Artect.Console;

public sealed class Prompt
{
    readonly IConsoleIO _io;
    readonly bool _interactive;

    public Prompt(IConsoleIO io)
    {
        _io = io;
        _interactive = !System.Console.IsInputRedirected;
    }

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

    public T AskEnum<T>(string question, T defaultValue, Func<T, string>? label = null) where T : struct, Enum
    {
        var values = Enum.GetValues<T>()
            .OrderBy(v => v.ToString(), StringComparer.Ordinal)
            .ToArray();
        var displayLabel = label ?? (v => v.ToString()!);
        return _interactive
            ? InteractiveSingle(question, values, defaultValue, displayLabel)
            : NumberedSingle(question, values, defaultValue, displayLabel);
    }

    public IReadOnlyList<T> AskMultiEnum<T>(string question, IReadOnlyList<T> defaults, Func<T, string>? label = null) where T : struct, Enum
    {
        var values = Enum.GetValues<T>()
            .Where(v => v.ToString() is not "None" and not "All")
            .OrderBy(v => v.ToString(), StringComparer.Ordinal)
            .ToArray();
        var displayLabel = label ?? (v => v.ToString()!);
        return _interactive
            ? InteractiveMulti(question, values, defaults, displayLabel)
            : NumberedMulti(question, values, defaults, displayLabel);
    }

    public IReadOnlyList<string> AskMultiString(string question, IReadOnlyList<string> options, IReadOnlyList<string> defaults)
    {
        var arr = options.ToArray();
        return _interactive
            ? InteractiveMulti(question, arr, defaults, s => s)
            : NumberedMulti(question, arr, defaults, s => s);
    }

    // ── Interactive (arrow-key) single-select ────────────────────────────────

    T InteractiveSingle<T>(string question, IReadOnlyList<T> options, T defaultValue, Func<T, string> label)
    {
        var comparer = EqualityComparer<T>.Default;
        int cursor = 0;
        for (int i = 0; i < options.Count; i++)
            if (comparer.Equals(options[i], defaultValue)) { cursor = i; break; }

        System.Console.WriteLine(Ansi.Colour(question, Ansi.Cyan));
        System.Console.WriteLine(Ansi.Colour("(↑/↓ to move, Enter to confirm)", Ansi.Dim));
        int startLine = System.Console.CursorTop;
        // Reserve space by printing blank lines, then we'll overwrite.
        for (int i = 0; i < options.Count; i++) System.Console.WriteLine();
        DrawSingle(options, cursor, defaultValue, label, startLine);

        while (true)
        {
            var key = System.Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.UpArrow)
                cursor = (cursor - 1 + options.Count) % options.Count;
            else if (key.Key == ConsoleKey.DownArrow)
                cursor = (cursor + 1) % options.Count;
            else if (key.Key == ConsoleKey.Enter)
                break;
            else if (key.Key == ConsoleKey.Escape)
            { cursor = IndexOf(options, defaultValue, comparer); break; }
            else
                continue;
            DrawSingle(options, cursor, defaultValue, label, startLine);
        }

        // Collapse the menu: overwrite each option line with a blank line, then print summary.
        System.Console.SetCursorPosition(0, startLine);
        for (int i = 0; i < options.Count; i++) System.Console.WriteLine(ClearLine());
        System.Console.SetCursorPosition(0, startLine);
        System.Console.WriteLine($"  → {Ansi.Colour(label(options[cursor]), Ansi.Green)}");
        return options[cursor];
    }

    static int IndexOf<T>(IReadOnlyList<T> options, T value, EqualityComparer<T> comparer)
    {
        for (int i = 0; i < options.Count; i++) if (comparer.Equals(options[i], value)) return i;
        return 0;
    }

    static void DrawSingle<T>(IReadOnlyList<T> options, int cursor, T defaultValue, Func<T, string> label, int startLine)
    {
        var comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < options.Count; i++)
        {
            System.Console.SetCursorPosition(0, startLine + i);
            bool isDefault = comparer.Equals(options[i], defaultValue);
            string marker = i == cursor ? "▶ " : "  ";
            string text = $"{marker}{label(options[i])}{(isDefault ? " (default)" : "")}";
            string coloured = i == cursor ? Ansi.Colour(text, Ansi.Cyan) : text;
            System.Console.Write(coloured + ClearLine());
        }
    }

    // ── Interactive (arrow-key) multi-select ─────────────────────────────────

    IReadOnlyList<T> InteractiveMulti<T>(string question, IReadOnlyList<T> options, IReadOnlyList<T> defaults, Func<T, string> label)
    {
        var selected = new HashSet<T>(defaults, EqualityComparer<T>.Default);
        int cursor = 0;

        System.Console.WriteLine(Ansi.Colour(question, Ansi.Cyan));
        System.Console.WriteLine(Ansi.Colour("(↑/↓ to move, Space to toggle, Enter to confirm)", Ansi.Dim));
        int startLine = System.Console.CursorTop;
        for (int i = 0; i < options.Count; i++) System.Console.WriteLine();
        DrawMulti(options, cursor, selected, label, startLine);

        while (true)
        {
            var key = System.Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.UpArrow)
                cursor = (cursor - 1 + options.Count) % options.Count;
            else if (key.Key == ConsoleKey.DownArrow)
                cursor = (cursor + 1) % options.Count;
            else if (key.Key == ConsoleKey.Spacebar)
            {
                if (!selected.Add(options[cursor])) selected.Remove(options[cursor]);
            }
            else if (key.Key == ConsoleKey.Enter)
                break;
            else if (key.Key == ConsoleKey.Escape)
            { selected = new HashSet<T>(defaults, EqualityComparer<T>.Default); break; }
            else
                continue;
            DrawMulti(options, cursor, selected, label, startLine);
        }

        var picked = options.Where(o => selected.Contains(o)).ToArray();
        System.Console.SetCursorPosition(0, startLine);
        for (int i = 0; i < options.Count; i++) System.Console.WriteLine(ClearLine());
        System.Console.SetCursorPosition(0, startLine);
        var summary = picked.Length == 0 ? "(none)" : string.Join(", ", picked.Select(label));
        System.Console.WriteLine($"  → {Ansi.Colour(summary, Ansi.Green)}");
        return picked;
    }

    static void DrawMulti<T>(IReadOnlyList<T> options, int cursor, HashSet<T> selected, Func<T, string> label, int startLine)
    {
        for (int i = 0; i < options.Count; i++)
        {
            System.Console.SetCursorPosition(0, startLine + i);
            string marker = i == cursor ? "▶ " : "  ";
            string check = selected.Contains(options[i]) ? "[x]" : "[ ]";
            string text = $"{marker}{check} {label(options[i])}";
            string coloured = i == cursor ? Ansi.Colour(text, Ansi.Cyan) : text;
            System.Console.Write(coloured + ClearLine());
        }
    }

    // Clear rest of the current line; works on any VT-capable terminal.
    static string ClearLine() => "[K";

    // ── Numbered fallback (non-TTY / redirected input) ────────────────────────

    T NumberedSingle<T>(string question, IReadOnlyList<T> options, T defaultValue, Func<T, string> label)
    {
        _io.WriteLine(Ansi.Colour(question, Ansi.Cyan));
        var comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < options.Count; i++)
        {
            bool isDefault = comparer.Equals(options[i], defaultValue);
            _io.WriteLine($"  {i + 1}. {label(options[i])}{(isDefault ? " (default)" : "")}");
        }
        _io.Write("Choice (number or name): ");
        var answer = _io.ReadLine().Trim();
        if (string.IsNullOrEmpty(answer)) return defaultValue;
        if (int.TryParse(answer, out var n) && n >= 1 && n <= options.Count)
            return options[n - 1];
        for (int i = 0; i < options.Count; i++)
            if (string.Equals(label(options[i]), answer, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(options[i]!.ToString(), answer, StringComparison.OrdinalIgnoreCase))
                return options[i];
        _io.WriteLine(Ansi.Colour("  Unrecognized — using default.", Ansi.Dim));
        return defaultValue;
    }

    IReadOnlyList<T> NumberedMulti<T>(string question, IReadOnlyList<T> options, IReadOnlyList<T> defaults, Func<T, string> label)
    {
        _io.WriteLine(Ansi.Colour(question, Ansi.Cyan));
        for (int i = 0; i < options.Count; i++) _io.WriteLine($"  {i + 1}. {label(options[i])}");
        _io.WriteLine($"(default: {string.Join(", ", defaults.Select(label))})");
        _io.Write("Choices (comma-separated numbers or names, or blank for default): ");
        var answer = _io.ReadLine().Trim();
        if (string.IsNullOrEmpty(answer)) return defaults;
        var result = new List<T>();
        foreach (var tok in answer.Split(','))
        {
            var t = tok.Trim();
            if (int.TryParse(t, out var n) && n >= 1 && n <= options.Count)
            { result.Add(options[n - 1]); continue; }
            for (int i = 0; i < options.Count; i++)
                if (string.Equals(label(options[i]), t, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(options[i]!.ToString(), t, StringComparison.OrdinalIgnoreCase))
                { result.Add(options[i]); break; }
        }
        return result.Count == 0 ? defaults : result;
    }
}
