using System.Collections.Generic;
using System.Linq;
using Artect.Core.Schema;
using Artect.Introspection.Readers;
using Index = Artect.Core.Schema.Index;

namespace Artect.Introspection;

public sealed class SqlServerSchemaReader
{
    readonly SqlConnectionFactory _factory;
    public SqlServerSchemaReader(SqlConnectionFactory factory) => _factory = factory;

    public SchemaGraph Read(IReadOnlyList<string> schemas)
    {
        using var conn = _factory.OpenConnection();
        var rawTables = TablesReader.Read(conn, schemas);
        var fks = ForeignKeysReader.Read(conn, schemas);
        var uniques = UniqueConstraintsReader.Read(conn, schemas);
        var indexes = IndexesReader.Read(conn, schemas);
        var checks = CheckConstraintsReader.Read(conn, schemas);
        var views = ViewsReader.Read(conn, schemas);
        var sequences = SequencesReader.Read(conn, schemas);
        var procs = StoredProceduresReader.Read(conn, schemas);
        var functions = FunctionsReader.Read(conn, schemas);

        var tables = rawTables.Select(t => t with
        {
            ForeignKeys = fks.TryGetValue((t.Schema, t.Name), out var fkList) ? fkList : new List<ForeignKey>(),
            UniqueConstraints = uniques.TryGetValue((t.Schema, t.Name), out var uqs) ? uqs : new List<UniqueConstraint>(),
            Indexes = indexes.TryGetValue((t.Schema, t.Name), out var ixs) ? ixs : new List<Index>(),
            CheckConstraints = checks.TryGetValue((t.Schema, t.Name), out var cks) ? cks : new List<CheckConstraint>(),
        }).ToList();

        return new SchemaGraph(
            Schemas: schemas.OrderBy(s => s, System.StringComparer.Ordinal).ToList(),
            Tables: tables.OrderBy(t => t.Schema, System.StringComparer.Ordinal).ThenBy(t => t.Name, System.StringComparer.Ordinal).ToList(),
            Views: views,
            Sequences: sequences,
            StoredProcedures: procs,
            Functions: functions);
    }
}
