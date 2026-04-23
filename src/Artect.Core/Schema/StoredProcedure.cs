using System.Collections.Generic;

namespace Artect.Core.Schema;

public sealed record StoredProcedure(
    string Schema,
    string Name,
    IReadOnlyList<StoredProcedureParameter> Parameters,
    IReadOnlyList<StoredProcedureResultColumn> ResultColumns,
    ResultInferenceStatus ResultInference);
