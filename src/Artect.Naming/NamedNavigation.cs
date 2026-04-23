using System.Collections.Generic;
using Artect.Core.Schema;

namespace Artect.Naming;

public sealed record NamedNavigation(
    string PropertyName,
    string TargetEntityTypeName,
    bool IsCollection,
    string SourceForeignKeyName,
    IReadOnlyList<ForeignKeyColumnPair> ColumnPairs);
