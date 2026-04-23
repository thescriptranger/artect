namespace Artect.Core.Schema;

public enum FunctionReturnKind { Scalar, Table, Inline }

public sealed record FunctionParameter(
    string Name, int Ordinal, string SqlType, ClrType ClrType, bool IsNullable);
