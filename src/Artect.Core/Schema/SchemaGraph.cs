using System.Collections.Generic;

namespace Artect.Core.Schema;

public sealed record SchemaGraph(
    IReadOnlyList<string> Schemas,
    IReadOnlyList<Table> Tables,
    IReadOnlyList<View> Views,
    IReadOnlyList<Sequence> Sequences,
    IReadOnlyList<StoredProcedure> StoredProcedures,
    IReadOnlyList<Function> Functions);
