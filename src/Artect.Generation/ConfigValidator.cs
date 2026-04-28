using System;
using System.Collections.Generic;
using System.Linq;
using Artect.Config;
using Artect.Core.Schema;

namespace Artect.Generation;

public static class ConfigValidator
{
    public static void Validate(ArtectConfig cfg, SchemaGraph graph)
    {
        var errors = new List<string>();

        var tableNames = graph.Tables
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);
        var tableByName = new Dictionary<string, Table>(StringComparer.Ordinal);
        foreach (var t in graph.Tables)
        {
            // Cross-schema collisions: last write wins, matching the existing
            // NamingCorrections convention. Validation still works; the user resolves
            // the collision by qualifying or renaming.
            tableByName[t.Name] = t;
        }

        foreach (var kv in cfg.TableClassifications)
        {
            if (!tableNames.Contains(kv.Key))
            {
                errors.Add($"tableClassifications: '{kv.Key}' does not match any introspected table.");
                continue;
            }
            var table = tableByName[kv.Key];
            if (kv.Value == EntityClassification.OwnedEntity)
            {
                var hasIncomingFk = graph.Tables.Any(t =>
                    t.ForeignKeys.Any(fk => fk.ToTable == table.Name && fk.ToSchema == table.Schema));
                if (!hasIncomingFk)
                    errors.Add($"tableClassifications: '{kv.Key}' = OwnedEntity, but no other table has a foreign key to it. EF cannot own a freestanding entity.");
            }
        }

        foreach (var t in cfg.ColumnMetadata)
        {
            if (!tableNames.Contains(t.Key))
            {
                errors.Add($"columnMetadata: table '{t.Key}' does not match any introspected table.");
                continue;
            }

            var classification = cfg.TableClassifications.TryGetValue(t.Key, out var cls)
                ? cls
                : EntityClassification.AggregateRoot;
            if (classification == EntityClassification.JoinTable || classification == EntityClassification.Ignored)
                errors.Add($"columnMetadata: '{t.Key}' is classified {classification}; column metadata is meaningless because no output is generated for it.");

            var table = tableByName[t.Key];
            var columnNames = table.Columns
                .Select(c => c.Name)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var c in t.Value)
            {
                if (!columnNames.Contains(c.Key))
                    errors.Add($"columnMetadata: '{t.Key}.{c.Key}' does not match any column on table '{t.Key}'.");
            }
        }

        if (errors.Count > 0)
            throw new ConfigValidationException(errors);
    }
}

public sealed class ConfigValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }
    public ConfigValidationException(IReadOnlyList<string> errors)
        : base("Configuration validation failed:" + Environment.NewLine + string.Join(Environment.NewLine, errors))
    {
        Errors = errors;
    }
}
