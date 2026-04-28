using System;

namespace Artect.Config;

[Flags]
public enum ColumnMetadata
{
    None                = 0,
    Ignored             = 1 << 0,
    ProtectedFromUpdate = 1 << 1,
    ConcurrencyToken    = 1 << 2,
    Audit               = 1 << 3,
    Sensitive           = 1 << 4,
    Deprecated          = 1 << 5,
    SoftDeleteFlag      = 1 << 6,
    TenantId            = 1 << 7,
}
