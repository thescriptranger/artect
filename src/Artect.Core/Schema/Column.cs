namespace Artect.Core.Schema;

public sealed record Column(
    string Name,
    int OrdinalPosition,
    string SqlType,
    ClrType ClrType,
    bool IsNullable,
    bool IsIdentity,
    bool IsComputed,
    bool IsServerGenerated,
    int? MaxLength,
    int? Precision,
    int? Scale,
    string? DefaultValue);
