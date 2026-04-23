using System;

namespace Artect.Config;

[Flags]
public enum CrudOperation
{
    None     = 0,
    GetList  = 1 << 0,
    GetById  = 1 << 1,
    Post     = 1 << 2,
    Put      = 1 << 3,
    Patch    = 1 << 4,
    Delete   = 1 << 5,
    All      = GetList | GetById | Post | Put | Patch | Delete,
}
