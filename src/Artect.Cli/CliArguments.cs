using System.Collections.Generic;

namespace Artect.Cli;

public sealed class CliArguments
{
    readonly Dictionary<string, string> _values;
    readonly HashSet<string> _flags;

    CliArguments(Dictionary<string, string> values, HashSet<string> flags)
    {
        _values = values;
        _flags = flags;
    }

    public static CliArguments Parse(string[] args)
    {
        var values = new Dictionary<string, string>(System.StringComparer.Ordinal);
        var flags = new HashSet<string>(System.StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--")) continue;
            var key = a.Substring(2);
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
            {
                values[key] = args[i + 1];
                i++;
            }
            else
            {
                flags.Add(key);
            }
        }
        return new CliArguments(values, flags);
    }

    public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;
    public bool Has(string key) => _values.ContainsKey(key) || _flags.Contains(key);
    public string Command => _values.TryGetValue("_command", out var c) ? c : string.Empty;
}
