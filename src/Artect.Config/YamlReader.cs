using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Artect.Config;

/// <summary>
/// V#17: parses <c>artect.yaml</c> via YamlDotNet so quoted strings, embedded
/// colons, comma-bearing values, and standard YAML escape rules just work.
/// Replaces the hand-rolled line-based parser whose limitations were documented
/// in the V#1 coordination contract. The on-disk format is unchanged: top-level
/// scalars, the <c>schemas</c> list, and three optional block maps for
/// <c>namingCorrections</c>, <c>tableClassifications</c>, and
/// <c>columnMetadata</c>. Existing artect.yaml files round-trip without
/// migration.
/// </summary>
public static class YamlReader
{
    public static ArtectConfig ReadFile(string path) => Read(File.ReadAllText(path));

    public static ArtectConfig Read(string content)
    {
        var root = LoadRoot(content);

        var knownKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "projectName","outputDirectory","targetFramework","dataAccess","emitRepositoriesAndAbstractions",
            "generatedByLabel","generateInitialMigration","crud","apiVersioning","auth",
            "includeTestsProject","includeDockerAssets","partitionStoredProceduresBySchema",
            "includeChildCollectionsInResponses","validateForeignKeyReferences","maxPageSize","enableDomainEvents","schemas","connectionString",
            "namingCorrections","tableClassifications","columnMetadata"
        };

        foreach (var pair in root.Children)
        {
            var key = ScalarKey(pair.Key);
            if (!knownKeys.Contains(key))
                throw Fail(pair.Key, $"Unknown key '{key}' in artect.yaml. Allowed keys: {string.Join(", ", knownKeys.OrderBy(k => k, StringComparer.Ordinal))}.");
        }

        var emitRepos = ParseBool(root, "emitRepositoriesAndAbstractions");
        if (!emitRepos)
        {
            var node = FindKey(root, "emitRepositoriesAndAbstractions");
            throw Fail(node, "emitRepositoriesAndAbstractions: false is no longer supported. Repositories are required for the Clean Architecture shape introduced in the compliance pass.");
        }

        return new ArtectConfig(
            ProjectName: RequireScalar(root, "projectName"),
            OutputDirectory: RequireScalar(root, "outputDirectory"),
            TargetFramework: TargetFrameworkExtensions.FromMoniker(RequireScalar(root, "targetFramework")),
            DataAccess: ParseEnum<DataAccessKind>(root, "dataAccess"),
            EmitRepositoriesAndAbstractions: emitRepos,
            GeneratedByLabel: RequireScalar(root, "generatedByLabel"),
            GenerateInitialMigration: ParseBool(root, "generateInitialMigration"),
            Crud: ParseCrud(root),
            ApiVersioning: ParseEnum<ApiVersioningKind>(root, "apiVersioning"),
            Auth: ParseEnum<AuthKind>(root, "auth"),
            IncludeTestsProject: ParseBool(root, "includeTestsProject"),
            IncludeDockerAssets: ParseBool(root, "includeDockerAssets"),
            PartitionStoredProceduresBySchema: ParseBool(root, "partitionStoredProceduresBySchema"),
            IncludeChildCollectionsInResponses: ParseBool(root, "includeChildCollectionsInResponses"),
            ValidateForeignKeyReferences: ParseBool(root, "validateForeignKeyReferences"),
            MaxPageSize: ParseOptionalPositiveInt(root, "maxPageSize", 100),
            EnableDomainEvents: TryGetScalar(root, "enableDomainEvents") is { } edeNode ? ParseBoolNode(edeNode, "enableDomainEvents") : false,
            Schemas: ParseScalarList(root, "schemas"),
            NamingCorrections: ParseScalarMap(root, "namingCorrections"),
            TableClassifications: ParseClassifications(root),
            ColumnMetadata: ParseColumnMetadata(root));
    }

    // ── Top-level loading ─────────────────────────────────────────────────────

    static YamlMappingNode LoadRoot(string content)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(content));
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new YamlException($"YAML parse error at line {ex.Start.Line}, column {ex.Start.Column}: {ex.Message}", ex);
        }
        if (stream.Documents.Count == 0)
            throw new YamlException("artect.yaml is empty.");
        var root = stream.Documents[0].RootNode;
        if (root is not YamlMappingNode mapping)
            throw Fail(root, "Top-level YAML must be a mapping (key/value pairs).");
        return mapping;
    }

    // ── Scalar accessors ──────────────────────────────────────────────────────

    static YamlNode FindKey(YamlMappingNode m, string key)
    {
        foreach (var pair in m.Children)
            if (string.Equals(ScalarKey(pair.Key), key, StringComparison.Ordinal))
                return pair.Key;
        throw new YamlException($"Missing required key '{key}' in artect.yaml.");
    }

    static YamlNode? TryGetScalar(YamlMappingNode m, string key)
    {
        foreach (var pair in m.Children)
            if (string.Equals(ScalarKey(pair.Key), key, StringComparison.Ordinal))
                return pair.Value;
        return null;
    }

    static string RequireScalar(YamlMappingNode m, string key)
    {
        foreach (var pair in m.Children)
        {
            if (!string.Equals(ScalarKey(pair.Key), key, StringComparison.Ordinal)) continue;
            if (pair.Value is YamlScalarNode scalar) return scalar.Value?.Trim() ?? string.Empty;
            throw Fail(pair.Value, $"Expected a scalar value for '{key}'.");
        }
        throw new YamlException($"Missing required key '{key}' in artect.yaml.");
    }

    static string ScalarKey(YamlNode node) =>
        node is YamlScalarNode s ? s.Value ?? string.Empty
        : throw Fail(node, "Expected a scalar key.");

    // ── Typed parsers ─────────────────────────────────────────────────────────

    static bool ParseBool(YamlMappingNode m, string key)
    {
        var node = TryGetScalar(m, key) ?? throw new YamlException($"Missing required key '{key}' in artect.yaml.");
        return ParseBoolNode(node, key);
    }

    static bool ParseBoolNode(YamlNode node, string key)
    {
        if (node is not YamlScalarNode s)
            throw Fail(node, $"Expected a scalar bool for '{key}'.");
        var v = (s.Value ?? string.Empty).Trim().ToLowerInvariant();
        return v switch
        {
            "true" or "yes" or "1" => true,
            "false" or "no" or "0" => false,
            _ => throw Fail(node, $"Invalid bool value '{s.Value}' for '{key}'. Expected: true | false."),
        };
    }

    static T ParseEnum<T>(YamlMappingNode m, string key) where T : struct, Enum
    {
        var raw = RequireScalar(m, key);
        if (Enum.TryParse<T>(raw, ignoreCase: true, out var v))
            return v;
        var allowed = string.Join(", ", Enum.GetNames(typeof(T)));
        throw Fail(FindKey(m, key), $"Invalid value '{raw}' for '{key}'. Allowed: {allowed}.");
    }

    static int ParseOptionalPositiveInt(YamlMappingNode m, string key, int fallback)
    {
        var node = TryGetScalar(m, key);
        if (node is null) return fallback;
        if (node is YamlScalarNode s && int.TryParse((s.Value ?? string.Empty).Trim(), out var i) && i > 0)
            return i;
        throw Fail(node, $"Invalid '{key}': expected a positive integer, got '{(node as YamlScalarNode)?.Value}'.");
    }

    static CrudOperation ParseCrud(YamlMappingNode m)
    {
        var node = TryGetScalar(m, "crud") ?? throw new YamlException("Missing required key 'crud' in artect.yaml.");
        var items = node switch
        {
            YamlSequenceNode seq => seq.Children.Select(c => ScalarOrThrow(c, "crud")).ToList(),
            YamlScalarNode scalar => SplitFlow(scalar.Value, "crud"),
            _ => throw Fail(node, "Expected a list of CRUD operations for 'crud'."),
        };
        var result = CrudOperation.None;
        foreach (var raw in items)
        {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0) continue;
            if (!Enum.TryParse<CrudOperation>(trimmed, ignoreCase: true, out var flag))
                throw Fail(node, $"Invalid CRUD operation '{trimmed}'. Allowed: {string.Join(", ", Enum.GetNames(typeof(CrudOperation)))}.");
            result |= flag;
        }
        return result;
    }

    static IReadOnlyList<string> ParseScalarList(YamlMappingNode m, string key)
    {
        var node = TryGetScalar(m, key) ?? throw new YamlException($"Missing required key '{key}' in artect.yaml.");
        return node switch
        {
            YamlSequenceNode seq => seq.Children.Select(c => ScalarOrThrow(c, key)).ToList(),
            YamlScalarNode scalar => SplitFlow(scalar.Value, key),
            _ => throw Fail(node, $"Expected a list for '{key}'."),
        };
    }

    static IReadOnlyDictionary<string, string> ParseScalarMap(YamlMappingNode m, string key)
    {
        var node = TryGetScalar(m, key);
        if (node is null) return new Dictionary<string, string>(StringComparer.Ordinal);
        if (node is not YamlMappingNode map)
            throw Fail(node, $"Expected a mapping for '{key}'.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in map.Children)
        {
            var k = ScalarKey(pair.Key);
            var v = pair.Value is YamlScalarNode s
                ? s.Value ?? string.Empty
                : throw Fail(pair.Value, $"Expected a scalar value for '{key}.{k}'.");
            result[k] = v.Trim();
        }
        return result;
    }

    static IReadOnlyDictionary<string, EntityClassification> ParseClassifications(YamlMappingNode m)
    {
        var raw = ParseScalarMap(m, "tableClassifications");
        var result = new Dictionary<string, EntityClassification>(StringComparer.Ordinal);
        foreach (var kv in raw)
        {
            if (!Enum.TryParse<EntityClassification>(kv.Value, ignoreCase: true, out var cls))
                throw new YamlException(
                    $"Invalid classification '{kv.Value}' for table '{kv.Key}'. Allowed: AggregateRoot, OwnedEntity, ReadModel, LookupData, JoinTable, Ignored.");
            result[kv.Key] = cls;
        }
        return result;
    }

    /// <summary>
    /// Format: keys are <c>"Table.Column"</c>; values are comma-separated <see cref="ColumnMetadata"/> flags.
    /// Documented in <c>docs/violations-context.md</c> under the V#1 contract.
    /// </summary>
    static IReadOnlyDictionary<string, IReadOnlyDictionary<string, ColumnMetadata>> ParseColumnMetadata(YamlMappingNode m)
    {
        var raw = ParseScalarMap(m, "columnMetadata");
        var result = new Dictionary<string, Dictionary<string, ColumnMetadata>>(StringComparer.Ordinal);
        foreach (var kv in raw)
        {
            var dot = kv.Key.IndexOf('.');
            if (dot <= 0 || dot == kv.Key.Length - 1)
                throw new YamlException($"Invalid columnMetadata key '{kv.Key}'. Expected 'Table.Column'.");
            var table = kv.Key.Substring(0, dot).Trim();
            var column = kv.Key.Substring(dot + 1).Trim();
            if (!Enum.TryParse<ColumnMetadata>(kv.Value, ignoreCase: true, out var flags))
                throw new YamlException($"Invalid columnMetadata value '{kv.Value}' for '{kv.Key}'. Allowed flags (comma-separated): {string.Join(", ", Enum.GetNames(typeof(ColumnMetadata)))}.");
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    static string ScalarOrThrow(YamlNode node, string key) =>
        node is YamlScalarNode s ? s.Value ?? string.Empty
        : throw Fail(node, $"Expected scalar items in '{key}'.");

    /// <summary>
    /// Splits a flow-style scalar like <c>"[a, b, c]"</c> or <c>"a, b, c"</c> into items.
    /// Handles the bracketed form for backward compatibility with the old hand-rolled
    /// parser that stored lists in this representation.
    /// </summary>
    static List<string> SplitFlow(string? rawValue, string key)
    {
        var t = (rawValue ?? string.Empty).Trim();
        if (t.StartsWith("[", StringComparison.Ordinal) && t.EndsWith("]", StringComparison.Ordinal))
            t = t.Substring(1, t.Length - 2);
        if (t.Length == 0) return new List<string>();
        return t.Split(',').Select(x => x.Trim()).ToList();
    }

    static YamlException Fail(YamlNode node, string message)
    {
        var mark = node?.Start;
        if (mark.HasValue && mark.Value.Line > 0)
            return new YamlException($"{message} (line {mark.Value.Line}, column {mark.Value.Column})");
        return new YamlException(message);
    }
}
