using Artect.Core.Schema;

namespace Artect.Naming;

public static class EntityNaming
{
    public static string EntityClassName(Table t) => CasingHelper.ToPascalCase(Pluralizer.Singularize(t.Name));
    public static string EntityPluralName(Table t) => CasingHelper.ToPascalCase(Pluralizer.Pluralize(Pluralizer.Singularize(t.Name)));
    public static string PropertyName(Column c)
    {
        var pascal = CasingHelper.ToPascalCase(c.Name);
        // strip trailing "Id" duplication like Foo_FooId -> Foo_FooId stays; but expose "Id" -> "Id"
        return pascal;
    }
    public static string NavigationPropertyName(string targetEntity, bool collection) =>
        collection ? CasingHelper.ToPascalCase(Pluralizer.Pluralize(targetEntity)) : CasingHelper.ToPascalCase(targetEntity);
}
