namespace Artect.Core.Schema;

public sealed record StoredProcedureResultColumn(
    string Name, int Ordinal, string SqlType, ClrType ClrType, bool IsNullable);
