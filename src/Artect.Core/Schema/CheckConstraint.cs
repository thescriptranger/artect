namespace Artect.Core.Schema;

public sealed record CheckConstraint(string Name, string TableName, string TableSchema, string? ColumnName, string Expression);
