using System.Collections.Generic;
using System.Linq;
using Artect.Core.Schema;
using Microsoft.Data.SqlClient;
using Index = Artect.Core.Schema.Index;

namespace Artect.Introspection.Readers;

public static class TablesReader
{
    public static IReadOnlyList<Table> Read(SqlConnection conn, IReadOnlyList<string> schemas)
    {
        var schemaList = "'" + string.Join("','", schemas) + "'";
        var tables = new List<(string Schema, string Name)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
SELECT TABLE_SCHEMA, TABLE_NAME
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_SCHEMA IN ({schemaList})
ORDER BY TABLE_SCHEMA, TABLE_NAME;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) tables.Add((r.GetString(0), r.GetString(1)));
        }

        var columnsByTable = ReadColumns(conn, schemaList);
        var pksByTable = ReadPrimaryKeys(conn, schemaList);

        // FKs, Unique, Indexes, Checks: set empty here; ConstraintsReaders fills later.
        var result = new List<Table>(tables.Count);
        foreach (var (sch, nm) in tables)
        {
            columnsByTable.TryGetValue((sch, nm), out var cols);
            pksByTable.TryGetValue((sch, nm), out var pk);
            result.Add(new Table(
                Schema: sch, Name: nm,
                Columns: cols ?? new List<Column>(),
                PrimaryKey: pk,
                ForeignKeys: new List<ForeignKey>(),
                UniqueConstraints: new List<UniqueConstraint>(),
                Indexes: new List<Index>(),
                CheckConstraints: new List<CheckConstraint>()));
        }
        return result;
    }

    static Dictionary<(string, string), IReadOnlyList<Column>> ReadColumns(SqlConnection conn, string schemaList)
    {
        var map = new Dictionary<(string, string), List<Column>>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
SELECT
  c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME, c.ORDINAL_POSITION,
  c.DATA_TYPE, c.IS_NULLABLE,
  COLUMNPROPERTY(OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME), c.COLUMN_NAME, 'IsIdentity') AS IsIdentity,
  COLUMNPROPERTY(OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME), c.COLUMN_NAME, 'IsComputed') AS IsComputed,
  c.CHARACTER_MAXIMUM_LENGTH, c.NUMERIC_PRECISION, c.NUMERIC_SCALE, c.COLUMN_DEFAULT
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_SCHEMA IN ({schemaList})
ORDER BY c.TABLE_SCHEMA, c.TABLE_NAME, c.ORDINAL_POSITION;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var key = (r.GetString(0), r.GetString(1));
            if (!map.TryGetValue(key, out var list)) { list = new List<Column>(); map[key] = list; }
            var sqlType = r.GetString(4);
            var clr = SqlTypeMap.ToClr(sqlType);
            var isIdentity = !r.IsDBNull(6) && r.GetInt32(6) == 1;
            var isComputed = !r.IsDBNull(7) && r.GetInt32(7) == 1;
            var def = r.IsDBNull(11) ? null : r.GetString(11);
            var serverGen = isIdentity || isComputed || DefaultLooksServerGenerated(def);
            list.Add(new Column(
                Name: r.GetString(2),
                OrdinalPosition: r.GetInt32(3),
                SqlType: sqlType,
                ClrType: clr,
                IsNullable: r.GetString(5) == "YES",
                IsIdentity: isIdentity,
                IsComputed: isComputed,
                IsServerGenerated: serverGen,
                MaxLength: r.IsDBNull(8) ? null : r.GetInt32(8),
                Precision: r.IsDBNull(9) ? null : (int?)r.GetByte(9),
                Scale: r.IsDBNull(10) ? null : (int?)r.GetInt32(10),
                DefaultValue: def));
        }
        return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Column>)kv.Value);
    }

    static bool DefaultLooksServerGenerated(string? def)
    {
        if (def is null) return false;
        var lower = def.ToLowerInvariant();
        return lower.Contains("newid()") || lower.Contains("newsequentialid()") || lower.Contains("next value for ");
    }

    static Dictionary<(string, string), PrimaryKey> ReadPrimaryKeys(SqlConnection conn, string schemaList)
    {
        var map = new Dictionary<(string, string), List<(string ConstraintName, string ColumnName, int Ordinal)>>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
SELECT tc.TABLE_SCHEMA, tc.TABLE_NAME, tc.CONSTRAINT_NAME, kcu.COLUMN_NAME, kcu.ORDINAL_POSITION
FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
  ON tc.CONSTRAINT_SCHEMA = kcu.CONSTRAINT_SCHEMA AND tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY' AND tc.TABLE_SCHEMA IN ({schemaList})
ORDER BY tc.TABLE_SCHEMA, tc.TABLE_NAME, kcu.ORDINAL_POSITION;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var key = (r.GetString(0), r.GetString(1));
            if (!map.TryGetValue(key, out var list)) { list = new List<(string, string, int)>(); map[key] = list; }
            list.Add((r.GetString(2), r.GetString(3), r.GetInt32(4)));
        }
        return map.ToDictionary(
            kv => kv.Key,
            kv => new PrimaryKey(
                Name: kv.Value[0].ConstraintName,
                ColumnNames: kv.Value.OrderBy(x => x.Ordinal).Select(x => x.ColumnName).ToList()));
    }
}
