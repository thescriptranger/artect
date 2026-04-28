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
    /// True when any column on this entity has the given metadata flag set. Useful for
    /// "does this entity participate in audit / tenancy / soft-delete?" queries.
    /// </summary>
    public static bool AnyColumnHasFlag(this NamedEntity e, ColumnMetadata flag) =>
        e.Table.Columns.Any(c => e.ColumnHasFlag(c.Name, flag));

    /// <summary>
    /// Returns the first column on this entity carrying the given metadata flag, or null
    /// when no column has it. V#12 uses this to find the SoftDeleteFlag / TenantId column
    /// per entity for query-filter generation.
    /// </summary>
    public static Column? FirstColumnWithFlag(this NamedEntity e, ColumnMetadata flag) =>
        e.Table.Columns.FirstOrDefault(c => e.ColumnHasFlag(c.Name, flag));

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
