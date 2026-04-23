using System.Collections.Generic;

namespace Artect.Core.Schema;

public sealed record Index(
    string Name,
    bool IsUnique,
    bool IsClustered,
    IReadOnlyList<string> KeyColumns,
    IReadOnlyList<string> IncludedColumns);
