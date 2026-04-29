using System;

namespace Artect.Naming;

/// <summary>
/// Returns a C#-safe reference to a generated entity type, escaping with a
/// <c>global::</c>-qualified name when the entity name collides with a segment
/// of the project namespace. Inside <c>&lt;Project&gt;.Domain.Entities</c> the
/// type is resolvable by short name, so collisions only matter for code that
/// references the entity from a different namespace (Application, Infrastructure,
/// Api, Tests).
/// </summary>
public static class EntityTypeRef
{
    public static string For(string entityTypeName, string projectName)
    {
        if (string.IsNullOrEmpty(projectName) || string.IsNullOrEmpty(entityTypeName))
            return entityTypeName;

        foreach (var segment in projectName.Split('.'))
        {
            if (string.Equals(segment, entityTypeName, StringComparison.Ordinal))
                return $"global::{projectName}.Domain.Entities.{entityTypeName}";
        }
        return entityTypeName;
    }

    public static bool Collides(string entityTypeName, string projectName)
    {
        if (string.IsNullOrEmpty(projectName) || string.IsNullOrEmpty(entityTypeName)) return false;
        foreach (var segment in projectName.Split('.'))
        {
            if (string.Equals(segment, entityTypeName, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
