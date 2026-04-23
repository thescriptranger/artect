using System.Collections.Generic;

namespace Artect.Core.Schema;

public sealed record View(string Schema, string Name, IReadOnlyList<Column> Columns);
