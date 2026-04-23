using System.Collections.Generic;

namespace Artect.Core.Schema;

public sealed record Table(
    string Schema,
    string Name,
    IReadOnlyList<Column> Columns,
    PrimaryKey? PrimaryKey,
    IReadOnlyList<ForeignKey> ForeignKeys,
    IReadOnlyList<UniqueConstraint> UniqueConstraints,
    IReadOnlyList<Index> Indexes,
    IReadOnlyList<CheckConstraint> CheckConstraints)
{
    public string QualifiedName => $"{Schema}.{Name}";
}
