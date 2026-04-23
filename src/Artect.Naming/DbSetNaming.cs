using System.Collections.Generic;
using System.Linq;
using Artect.Core.Schema;

namespace Artect.Naming;

public sealed class DbSetNaming
{
    readonly Dictionary<(string Schema, string Name), string> _dbSetByTable = new();
    readonly Dictionary<(string Schema, string Name), string> _entityByTable = new();
    public IReadOnlyDictionary<(string, string), string> DbSetNames => _dbSetByTable;
    public IReadOnlyDictionary<(string, string), string> EntityTypeNames => _entityByTable;

    public static DbSetNaming Build(SchemaGraph graph)
    {
        var result = new DbSetNaming();
        var baseEntity = graph.Tables.ToDictionary(t => (t.Schema, t.Name), t => EntityNaming.EntityClassName(t));
        var collisions = baseEntity.Values.GroupBy(v => v).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        foreach (var t in graph.Tables)
        {
            var basePluralSet = CasingHelper.ToPascalCase(Pluralizer.Pluralize(baseEntity[(t.Schema, t.Name)]));
            var entityName = baseEntity[(t.Schema, t.Name)];
            if (collisions.Contains(entityName))
            {
                var schemaPrefix = CasingHelper.ToPascalCase(t.Schema);
                result._dbSetByTable[(t.Schema, t.Name)] = schemaPrefix + basePluralSet;
                result._entityByTable[(t.Schema, t.Name)] = schemaPrefix + entityName;
            }
            else
            {
                result._dbSetByTable[(t.Schema, t.Name)] = basePluralSet;
                result._entityByTable[(t.Schema, t.Name)] = entityName;
            }
        }
        return result;
    }
}
