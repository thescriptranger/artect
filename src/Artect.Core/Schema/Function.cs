using System.Collections.Generic;

namespace Artect.Core.Schema;

public sealed record Function(
    string Schema,
    string Name,
    FunctionReturnKind ReturnKind,
    string? ReturnSqlType,
    ClrType? ReturnClrType,
    IReadOnlyList<FunctionParameter> Parameters,
    IReadOnlyList<Column> ResultColumns);
