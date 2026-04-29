using System;
using System.Collections.Generic;
using Artect.Config;
using Artect.Core.Schema;

namespace Artect.Naming;

/// <summary>
/// Infers default <see cref="ColumnMetadata"/> flags from column name + CLR type.
/// User-supplied overrides in <c>artect.yaml</c> always win — the heuristic only
/// fires for columns the user has not explicitly classified.
///
/// Detected patterns:
/// - <c>Created*</c> on a date-typed column → <c>Audit | ProtectedFromUpdate</c>.
/// - <c>Updated*</c> / <c>Modified*</c> on a date-typed column → <c>Audit</c>.
/// - <c>RowVersion</c> on a byte-array column → <c>ConcurrencyToken | ProtectedFromUpdate</c>.
/// </summary>
public static class ColumnHeuristic
{
    public static IReadOnlyDictionary<string, ColumnMetadata> Apply(
        Table table,
        IReadOnlyDictionary<string, ColumnMetadata>? userOverrides)
    {
        var result = new Dictionary<string, ColumnMetadata>(StringComparer.Ordinal);
        foreach (var col in table.Columns)
        {
            if (userOverrides is not null && userOverrides.TryGetValue(col.Name, out var explicitFlags))
            {
                result[col.Name] = explicitFlags;
                continue;
            }
            var inferred = Infer(col);
            if (inferred != ColumnMetadata.None)
                result[col.Name] = inferred;
        }
        return result;
    }

    static ColumnMetadata Infer(Column col)
    {
        var name = col.Name;
        if (IsDateLike(col.ClrType))
        {
            if (StartsWith(name, "Created"))
                return ColumnMetadata.Audit | ColumnMetadata.ProtectedFromUpdate;
            if (StartsWith(name, "Updated") || StartsWith(name, "Modified"))
                return ColumnMetadata.Audit;
        }
        if (string.Equals(name, "RowVersion", StringComparison.OrdinalIgnoreCase) && col.ClrType == ClrType.ByteArray)
            return ColumnMetadata.ConcurrencyToken | ColumnMetadata.ProtectedFromUpdate;
        return ColumnMetadata.None;
    }

    static bool IsDateLike(ClrType t) =>
        t == ClrType.DateTime || t == ClrType.DateTimeOffset || t == ClrType.DateOnly;

    static bool StartsWith(string s, string prefix) =>
        s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
}
