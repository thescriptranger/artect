using System.Linq;
using Artect.Config;
using Artect.Naming;

namespace Artect.Generation.Emitters;

public static class EntityEmitterGuards
{
    public static bool ShouldSkip(this NamedEntity e, params EntityClassification[] allowed) =>
        !e.HasPrimaryKey || !allowed.Contains(e.Classification);

    public static bool ColumnHasFlag(this NamedEntity e, string columnName, ColumnMetadata flag) =>
        e.ColumnMetadata.TryGetValue(columnName, out var meta) && (meta & flag) == flag;
}
