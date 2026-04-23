using System.Collections.Generic;

namespace Artect.Core.Schema;

public sealed record UniqueConstraint(string Name, IReadOnlyList<string> ColumnNames);
