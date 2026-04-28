using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Artect.Config;

public static class YamlReader
{
    public static ArtectConfig ReadFile(string path) => Read(File.ReadAllText(path));

    public static ArtectConfig Read(string content)
    {
        var values = Parse(content);
        string Require(string key) => values.TryGetValue(key, out var v)
            ? v
            : throw new YamlException($"Missing required key '{key}' in artect.yaml.");

        var knownKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "projectName","outputDirectory","targetFramework","dataAccess","emitRepositoriesAndAbstractions",
            "generatedByLabel","generateInitialMigration","crud","apiVersioning","auth",
            "includeTestsProject","includeDockerAssets","partitionStoredProceduresBySchema",
            "includeChildCollectionsInResponses","validateForeignKeyReferences","maxPageSize","enableDomainEvents","schemas","connectionString",
            "namingCorrections","tableClassifications","columnMetadata"
        };
        foreach (var k in values.Keys)
            if (!knownKeys.Contains(k)) throw new YamlException($"Unknown key '{k}' in artect.yaml.");

        var emitRepos = ParseBool(Require("emitRepositoriesAndAbstractions"));
        if (!emitRepos)
            throw new YamlException("emitRepositoriesAndAbstractions: false is no longer supported. Repositories are required for the Clean Architecture shape introduced in the compliance pass.");

        return new ArtectConfig(
            ProjectName: Require("projectName").Trim(),
            OutputDirectory: Require("outputDirectory").Trim(),
            TargetFramework: TargetFrameworkExtensions.FromMoniker(Require("targetFramework").Trim()),
            DataAccess: ParseEnum<DataAccessKind>(Require("dataAccess")),
            EmitRepositoriesAndAbstractions: emitRepos,
            GeneratedByLabel: TrimQuotes(Require("generatedByLabel")),
            GenerateInitialMigration: ParseBool(Require("generateInitialMigration")),
            Crud: ParseCrud(Require("crud")),
            ApiVersioning: ParseEnum<ApiVersioningKind>(Require("apiVersioning")),
            Auth: ParseEnum<AuthKind>(Require("auth")),
            IncludeTestsProject: ParseBool(Require("includeTestsProject")),
            IncludeDockerAssets: ParseBool(Require("includeDockerAssets")),
            PartitionStoredProceduresBySchema: ParseBool(Require("partitionStoredProceduresBySchema")),
            IncludeChildCollectionsInResponses: ParseBool(Require("includeChildCollectionsInResponses")),
            ValidateForeignKeyReferences: ParseBool(Require("validateForeignKeyReferences")),
            MaxPageSize: values.TryGetValue("maxPageSize", out var mps) && int.TryParse(mps.Trim(), out var mpsParsed) && mpsParsed > 0 ? mpsParsed : 100,
            EnableDomainEvents: values.TryGetValue("enableDomainEvents", out var ede) ? ParseBool(ede) : false,
            Schemas: ParseStringList(Require("schemas")),
            NamingCorrections: values.TryGetValue("namingCorrections", out var nc) ? ParseMap(nc) : new Dictionary<string, string>(),
            TableClassifications: values.TryGetValue("tableClassifications", out var tc) ? ParseClassifications(tc) : new Dictionary<string, EntityClassification>(),
            ColumnMetadata: values.TryGetValue("columnMetadata", out var cm) ? ParseColumnMetadata(cm) : new Dictionary<string, IReadOnlyDictionary<string, ColumnMetadata>>());
    }

    static IReadOnlyDictionary<string, EntityClassification> ParseClassifications(string s)
    {
        var raw = ParseMap(s);
        var result = new Dictionary<string, EntityClassification>(StringComparer.Ordinal);
        foreach (var kv in raw)
        {
            if (!Enum.TryParse<EntityClassification>(kv.Value.Trim(), ignoreCase: true, out var cls))
                throw new YamlException($"Invalid classification '{kv.Value}' for table '{kv.Key}'. Allowed: AggregateRoot, OwnedEntity, ReadModel, LookupData, JoinTable, Ignored.");
            result[kv.Key] = cls;
        }
        return result;
    }

    static IReadOnlyDictionary<string, IReadOnlyDictionary<string, ColumnMetadata>> ParseColumnMetadata(string s)
    {
        // Format: keys are "Table.Column"; values are comma-separated ColumnMetadata flags.
        // The hand-rolled parser only handles one indent level, so column-metadata uses dotted keys
        // instead of nested maps. (Documented in docs/violations-context.md.)
        var raw = ParseMap(s);
        var result = new Dictionary<string, Dictionary<string, ColumnMetadata>>(StringComparer.Ordinal);
        foreach (var kv in raw)
        {
            var dot = kv.Key.IndexOf('.');
            if (dot <= 0 || dot == kv.Key.Length - 1)
                throw new YamlException($"Invalid columnMetadata key '{kv.Key}'. Expected 'Table.Column'.");
            var table = kv.Key.Substring(0, dot).Trim();
            var column = kv.Key.Substring(dot + 1).Trim();
            if (!Enum.TryParse<ColumnMetadata>(kv.Value.Trim(), ignoreCase: true, out var flags))
                throw new YamlException($"Invalid columnMetadata value '{kv.Value}' for '{kv.Key}'. Allowed flags: Ignored, ProtectedFromUpdate, ConcurrencyToken, Audit, Sensitive (comma-separated).");
            if (!result.TryGetValue(table, out var inner))
            {
                inner = new Dictionary<string, ColumnMetadata>(StringComparer.Ordinal);
                result[table] = inner;
            }
            inner[column] = flags;
        }
        return result.ToDictionary(
            x => x.Key,
            x => (IReadOnlyDictionary<string, ColumnMetadata>)x.Value,
            StringComparer.Ordinal);
    }

    static Dictionary<string, string> Parse(string content)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = content.Replace("\r\n", "\n").Split('\n');
        string? currentKey = null;
        var listAccum = new List<string>();
        var mapAccum = new List<string>(); // accumulates "subkey: subvalue" pairs for block-map keys
        bool inMap = false;

        void FlushCurrent()
        {
            if (currentKey is null) return;
            if (inMap)
                values[currentKey] = "{" + string.Join("|", mapAccum) + "}";
            else
                values[currentKey] = "[" + string.Join(",", listAccum) + "]";
            currentKey = null;
            listAccum.Clear();
            mapAccum.Clear();
            inMap = false;
        }

        foreach (var rawLine in lines)
        {
            var line = StripComment(rawLine);
            if (string.IsNullOrWhiteSpace(line))
            {
                FlushCurrent();
                continue;
            }
            // List item
            if (line.StartsWith("  - ", StringComparison.Ordinal))
            {
                if (currentKey is null) throw new YamlException($"Stray list item: '{line}'");
                listAccum.Add(line.Substring(4).Trim());
                inMap = false;
                continue;
            }
            // Indented map entry (two-space indent, not a list item)
            if (line.StartsWith("  ", StringComparison.Ordinal) && !line.StartsWith("  - ", StringComparison.Ordinal))
            {
                if (currentKey is null) throw new YamlException($"Stray indented line: '{line}'");
                inMap = true;
                mapAccum.Add(line.Trim());
                continue;
            }
            // Non-indented line — flush any accumulated block
            FlushCurrent();
            var colon = line.IndexOf(':');
            if (colon < 0) throw new YamlException($"Invalid line (no colon): '{line}'");
            var key = line.Substring(0, colon).Trim();
            var value = line.Substring(colon + 1).Trim();
            if (value.Length == 0)
            {
                currentKey = key;
                continue;
            }
            values[key] = value;
        }
        FlushCurrent();
        return values;
    }

    static IReadOnlyDictionary<string, string> ParseMap(string s)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var t = s.Trim();
        if (t.Length == 0 || t == "{}") return result;
        // Strip outer braces if present
        if (t.StartsWith("{") && t.EndsWith("}"))
            t = t.Substring(1, t.Length - 2);
        // Entries are separated by '|'; each entry is "key: value"
        var entries = t.Split('|');
        foreach (var entry in entries)
        {
            var e = entry.Trim();
            if (e.Length == 0) continue;
            var colon = e.IndexOf(':');
            if (colon < 0) throw new YamlException($"Invalid namingCorrections entry: '{e}'");
            var k = e.Substring(0, colon).Trim();
            var v = e.Substring(colon + 1).Trim();
            result[k] = v;
        }
        return result;
    }

    static string StripComment(string line)
    {
        var hash = line.IndexOf('#');
        if (hash < 0) return line;
        // keep hash if inside quotes
        bool inQuote = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') inQuote = !inQuote;
            if (line[i] == '#' && !inQuote) return line.Substring(0, i);
        }
        return line;
    }

    static bool ParseBool(string s) => s.Trim().ToLowerInvariant() switch
    {
        "true" => true,
        "false" => false,
        _ => throw new YamlException($"Invalid bool value '{s}'.")
    };

    static T ParseEnum<T>(string s) where T : struct, Enum
    {
        if (Enum.TryParse<T>(s.Trim(), ignoreCase: true, out var v)) return v;
        throw new YamlException($"Invalid value '{s}' for enum {typeof(T).Name}.");
    }

    static CrudOperation ParseCrud(string s)
    {
        var items = ParseStringList(s);
        var result = CrudOperation.None;
        foreach (var raw in items)
        {
            var trimmed = raw.Trim();
            if (!Enum.TryParse<CrudOperation>(trimmed, ignoreCase: true, out var flag))
                throw new YamlException($"Invalid CRUD operation '{trimmed}'.");
            result |= flag;
        }
        return result;
    }

    static IReadOnlyList<string> ParseStringList(string s)
    {
        var t = s.Trim();
        if (t.StartsWith("[") && t.EndsWith("]"))
        {
            var inner = t.Substring(1, t.Length - 2);
            if (inner.Length == 0) return Array.Empty<string>();
            return inner.Split(',').Select(x => TrimQuotes(x.Trim())).ToArray();
        }
        return new[] { TrimQuotes(t) };
    }

    static string TrimQuotes(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') return s.Substring(1, s.Length - 2);
        return s;
    }
}
