using System;

namespace Artect.Core.Schema;

public static class SqlTypeMap
{
    public static ClrType ToClr(string sqlType) => sqlType.ToLowerInvariant() switch
    {
        "bigint" => ClrType.Int64,
        "int" => ClrType.Int32,
        "smallint" => ClrType.Int16,
        "tinyint" => ClrType.Byte,
        "bit" => ClrType.Boolean,
        "decimal" or "numeric" or "money" or "smallmoney" => ClrType.Decimal,
        "float" => ClrType.Double,
        "real" => ClrType.Single,
        "datetime" or "datetime2" or "smalldatetime" => ClrType.DateTime,
        "datetimeoffset" => ClrType.DateTimeOffset,
        "date" => ClrType.DateOnly,
        "time" => ClrType.TimeOnly,
        "uniqueidentifier" => ClrType.Guid,
        "binary" or "varbinary" or "image" or "rowversion" or "timestamp" => ClrType.ByteArray,
        "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext" or "xml" => ClrType.String,
        _ => ClrType.Object,
    };

    public static string ToCs(ClrType clr) => clr switch
    {
        ClrType.String => "string",
        ClrType.Int16 => "short",
        ClrType.Int32 => "int",
        ClrType.Int64 => "long",
        ClrType.Byte => "byte",
        ClrType.Boolean => "bool",
        ClrType.Decimal => "decimal",
        ClrType.Double => "double",
        ClrType.Single => "float",
        ClrType.DateTime => "System.DateTime",
        ClrType.DateTimeOffset => "System.DateTimeOffset",
        ClrType.DateOnly => "System.DateOnly",
        ClrType.TimeOnly => "System.TimeOnly",
        ClrType.Guid => "System.Guid",
        ClrType.ByteArray => "byte[]",
        ClrType.Object or _ => "object",
    };

    public static bool IsValueType(ClrType clr) => clr is not (ClrType.String or ClrType.ByteArray or ClrType.Object);
}
