using System.Collections.Generic;
using Artect.Core.Schema;

namespace Artect.Naming;

public sealed record NamedEntity(
    Table Table,
    string EntityTypeName,
    string DbSetPropertyName,
    IReadOnlyList<NamedNavigation> ReferenceNavigations,
    IReadOnlyList<NamedNavigation> CollectionNavigations,
    bool IsJoinTable,
    bool HasPrimaryKey);
