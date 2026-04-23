using System;
using System.Collections.Generic;
using System.Linq;

namespace Artect.Console;

public sealed class Prompt
{
    // ANSI control sequences. Relative cursor movement only — absolute
    // SetCursorPosition is fragile under buffer scrolling.
    const string CursorUp      = "[{0}A"; // format with N lines
    const string EraseLine     = "\r[2K";  // carriage return + erase entire line
    const string EraseToEnd    = "[0J";    // erase from cursor to end of screen

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
    //
    // Layout after Draw:
    //   line Q:   question           ← written before Draw
    //   line Q+1: instruction        ← written before Draw
    //   line Q+2: option 0
    //   ...
    //   line Q+1+N: option N-1
    //   line Q+2+N: cursor (1 below last option)
    //
    // To redraw: up N lines, rewrite options. Cursor returns to same position.
    // To collapse: up (N+1) lines (past options + instruction), erase to end, write summary.

    T InteractiveSingle<T>(string question, IReadOnlyList<T> options, T defaultValue, Func<T, string> label)
    {
        var comparer = EqualityComparer<T>.Default;
        int cursor = IndexOf(options, defaultValue, comparer);
        int count = options.Count;

        System.Console.WriteLine(Ansi.Colour(question, Ansi.Cyan));
        System.Console.WriteLine(Ansi.Colour("(↑/↓ to move, Enter to confirm)", Ansi.Dim));
        DrawSingle(options, cursor, defaultValue, label);

        while (true)
        {
            var key = System.Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.UpArrow)
                cursor = (cursor - 1 + count) % count;
            else if (key.Key == ConsoleKey.DownArrow)
                cursor = (cursor + 1) % count;
            else if (key.Key == ConsoleKey.Enter)
                break;
            else if (key.Key == ConsoleKey.Escape)
            { cursor = IndexOf(options, defaultValue, comparer); break; }
            else
                continue;

            System.Console.Write(string.Format(CursorUp, count));
            DrawSingle(options, cursor, defaultValue, label);
        }

        // Collapse menu: replace (instruction + N options) with single summary line.
        System.Console.Write(string.Format(CursorUp, count + 1));
        System.Console.Write(EraseToEnd);
        System.Console.WriteLine($"  → {Ansi.Colour(label(options[cursor]), Ansi.Green)}");
        return options[cursor];
    }

    static void DrawSingle<T>(IReadOnlyList<T> options, int cursor, T defaultValue, Func<T, string> label)
    {
        var comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < options.Count; i++)
        {
            System.Console.Write(EraseLine);
            bool isDefault = comparer.Equals(options[i], defaultValue);
            string marker = i == cursor ? "▶ " : "  ";
            string text = $"{marker}{label(options[i])}{(isDefault ? " (default)" : "")}";
            System.Console.WriteLine(i == cursor ? Ansi.Colour(text, Ansi.Cyan) : text);
        }
    }

    // ── Interactive (arrow-key) multi-select ─────────────────────────────────

    IReadOnlyList<T> InteractiveMulti<T>(string question, IReadOnlyList<T> options, IReadOnlyList<T> defaults, Func<T, string> label)
    {
        var selected = new HashSet<T>(defaults, EqualityComparer<T>.Default);
        int cursor = 0;
        int count = options.Count;

        System.Console.WriteLine(Ansi.Colour(question, Ansi.Cyan));
        System.Console.WriteLine(Ansi.Colour("(↑/↓ to move, Space to toggle, Enter to confirm)", Ansi.Dim));
        DrawMulti(options, cursor, selected, label);

        while (true)
        {
            var key = System.Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.UpArrow)
                cursor = (cursor - 1 + count) % count;
            else if (key.Key == ConsoleKey.DownArrow)
                cursor = (cursor + 1) % count;
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

            System.Console.Write(string.Format(CursorUp, count));
            DrawMulti(options, cursor, selected, label);
        }

        var picked = options.Where(o => selected.Contains(o)).ToArray();
        System.Console.Write(string.Format(CursorUp, count + 1));
        System.Console.Write(EraseToEnd);
        var summary = picked.Length == 0 ? "(none)" : string.Join(", ", picked.Select(label));
        System.Console.WriteLine($"  → {Ansi.Colour(summary, Ansi.Green)}");
        return picked;
    }

    static void DrawMulti<T>(IReadOnlyList<T> options, int cursor, HashSet<T> selected, Func<T, string> label)
    {
        for (int i = 0; i < options.Count; i++)
        {
            System.Console.Write(EraseLine);
            string marker = i == cursor ? "▶ " : "  ";
            string check = selected.Contains(options[i]) ? "[x]" : "[ ]";
            string text = $"{marker}{check} {label(options[i])}";
            System.Console.WriteLine(i == cursor ? Ansi.Colour(text, Ansi.Cyan) : text);
        }
    }

    static int IndexOf<T>(IReadOnlyList<T> options, T value, EqualityComparer<T> comparer)
    {
        for (int i = 0; i < options.Count; i++)
            if (comparer.Equals(options[i], value)) return i;
        return 0;
    }

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
