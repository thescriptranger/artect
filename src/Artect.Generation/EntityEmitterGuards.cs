using System;
using System.Collections.Generic;
using System.Linq;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

public static class EntityEmitterGuards
{
    public static bool ShouldSkip(this NamedEntity e, params EntityClassification[] allowed) =>
        !e.HasPrimaryKey || !allowed.Contains(e.Classification);

    public static bool ColumnHasFlag(this NamedEntity e, string columnName, ColumnMetadata flag) =>
        e.ColumnMetadata.TryGetValue(columnName, out var meta) && (meta & flag) == flag;

    /// <summary>
    /// V#2 source of truth for which columns may be mutated by an entity Update method
    /// or referenced by an Update/Patch handler. Excludes Ignored, server-generated,
    /// primary-key, and ProtectedFromUpdate columns.
    /// </summary>
    public static IReadOnlyList<Column> UpdateableColumns(this NamedEntity e)
    {
        var pkCols = e.Table.PrimaryKey is { } pk
            ? pk.ColumnNames.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return e.Table.Columns
            .Where(c => !e.ColumnHasFlag(c.Name, ColumnMetadata.Ignored))
            .Where(c => !c.IsServerGenerated)
            .Where(c => !pkCols.Contains(c.Name))
            .Where(c => !e.ColumnHasFlag(c.Name, ColumnMetadata.ProtectedFromUpdate))
            .ToList();
    }

    public static bool EmitsBehavior(this NamedEntity e) =>
        e.Classification == EntityClassification.AggregateRoot ||
        e.Classification == EntityClassification.OwnedEntity;
}
