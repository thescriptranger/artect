using System.Collections.Generic;

namespace Artect.Core.Schema;

public sealed record ForeignKey(
    string Name,
    string FromSchema,
    string FromTable,
    string ToSchema,
    string ToTable,
    IReadOnlyList<ForeignKeyColumnPair> ColumnPairs,
    ForeignKeyAction OnDelete,
    ForeignKeyAction OnUpdate);
