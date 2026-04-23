namespace Artect.Core.Schema;

public sealed record PrimaryKey(string Name, System.Collections.Generic.IReadOnlyList<string> ColumnNames);
