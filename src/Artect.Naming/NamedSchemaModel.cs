using System.Collections.Generic;
using System.Linq;
using Artect.Config;
using Artect.Core.Schema;

namespace Artect.Naming;

public sealed class NamedSchemaModel
{
    public SchemaGraph Graph { get; }
    public IReadOnlyList<NamedEntity> Entities { get; }
    public DbSetNaming DbSets { get; }

    NamedSchemaModel(SchemaGraph graph, IReadOnlyList<NamedEntity> entities, DbSetNaming dbSets)
    {
        Graph = graph;
        Entities = entities;
        DbSets = dbSets;
    }

    public static NamedSchemaModel Build(SchemaGraph graph) => Build(graph, null, null);

    public static NamedSchemaModel Build(
        SchemaGraph graph,
        IReadOnlyDictionary<string, EntityClassification>? tableClassifications,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, ColumnMetadata>>? columnMetadata)
    {
        var dbSets = DbSetNaming.Build(graph);
        var entities = new List<NamedEntity>(graph.Tables.Count);
        foreach (var t in graph.Tables)
        {
            var typeName = dbSets.EntityTypeNames[(t.Schema, t.Name)];
            var dbSetName = dbSets.DbSetNames[(t.Schema, t.Name)];
            var classification = EntityClassifier.Classify(t, tableClassifications);
            var userOverrides = columnMetadata is not null && columnMetadata.TryGetValue(t.Name, out var m) ? m : null;
            var colMeta = ColumnHeuristic.Apply(t, userOverrides);
            entities.Add(new NamedEntity(
                Table: t,
                EntityTypeName: typeName,
                DbSetPropertyName: dbSetName,
                ReferenceNavigations: BuildReferenceNavigations(t, dbSets),
                CollectionNavigations: BuildCollectionNavigations(t, graph, dbSets),
                IsJoinTable: JoinTableDetector.IsJoinTable(t),
                HasPrimaryKey: t.PrimaryKey is not null,
                Classification: classification,
                ColumnMetadata: colMeta));
        }
        return new NamedSchemaModel(graph, entities, dbSets);
    }

    static IReadOnlyList<NamedNavigation> BuildReferenceNavigations(Table t, DbSetNaming dbSets)
    {
        var navs = new List<NamedNavigation>();
        var byTarget = t.ForeignKeys.GroupBy(fk => (fk.ToSchema, fk.ToTable));
        foreach (var group in byTarget)
        {
            var fks = group.OrderBy(fk => fk.Name).ToList();
            var targetName = dbSets.EntityTypeNames.TryGetValue((group.Key.ToSchema, group.Key.ToTable), out var n)
                ? n
                : CasingHelper.ToPascalCase(group.Key.ToTable);
            for (int i = 0; i < fks.Count; i++)
            {
                var baseName = EntityNaming.NavigationPropertyName(targetName, collection: false);
                var propName = fks.Count == 1 ? baseName : baseName + "_" + SanitizeColumns(fks[i].ColumnPairs);
                navs.Add(new NamedNavigation(propName, targetName, IsCollection: false,
                    SourceForeignKeyName: fks[i].Name, ColumnPairs: fks[i].ColumnPairs));
            }
        }
        return navs;
    }

    static IReadOnlyList<NamedNavigation> BuildCollectionNavigations(Table t, SchemaGraph graph, DbSetNaming dbSets)
    {
        var navs = new List<NamedNavigation>();
        foreach (var other in graph.Tables)
        {
            foreach (var fk in other.ForeignKeys.Where(f => f.ToSchema == t.Schema && f.ToTable == t.Name))
            {
                var targetName = dbSets.EntityTypeNames[(other.Schema, other.Name)];
                var baseName = EntityNaming.NavigationPropertyName(targetName, collection: true);
                var sameTargetCount = other.ForeignKeys.Count(f => f.ToSchema == t.Schema && f.ToTable == t.Name);
                var propName = sameTargetCount == 1 ? baseName : baseName + "_" + SanitizeColumns(fk.ColumnPairs);
                navs.Add(new NamedNavigation(propName, targetName, IsCollection: true,
                    SourceForeignKeyName: fk.Name, ColumnPairs: fk.ColumnPairs));
            }
        }
        return navs.OrderBy(n => n.PropertyName, System.StringComparer.Ordinal).ToList();
    }

    static string SanitizeColumns(IReadOnlyList<ForeignKeyColumnPair> pairs) =>
        string.Join("_", pairs.Select(p => CasingHelper.ToPascalCase(p.FromColumn)));
}
