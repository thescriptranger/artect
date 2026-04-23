namespace Artect.Core.Schema;

public sealed record StoredProcedureParameter(
    string Name, int Ordinal, string SqlType, ClrType ClrType, bool IsNullable,
    bool IsOutput, int? MaxLength, int? Precision, int? Scale);
