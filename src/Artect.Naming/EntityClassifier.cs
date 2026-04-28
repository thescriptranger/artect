using System.Collections.Generic;
using Artect.Config;
using Artect.Core.Schema;

namespace Artect.Naming;

public static class EntityClassifier
{
    public static EntityClassification Classify(
        Table t,
        IReadOnlyDictionary<string, EntityClassification>? overrides = null)
    {
        if (overrides is not null && overrides.TryGetValue(t.Name, out var explicitCls))
            return explicitCls;
        return Heuristic(t);
    }

    public static EntityClassification Heuristic(Table t)
    {
        if (t.PrimaryKey is null) return EntityClassification.Ignored;
        if (JoinTableDetector.IsJoinTable(t)) return EntityClassification.JoinTable;
        return EntityClassification.AggregateRoot;
    }
}
