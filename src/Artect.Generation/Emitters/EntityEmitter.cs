using System;
using System.Collections.Generic;
using System.Linq;
using Artect.Core.Schema;
using Artect.Naming;
using Artect.Templating;

namespace Artect.Generation.Emitters;

public sealed class EntityEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var template = TemplateParser.Parse(ctx.Templates.Load("Entity.cs.artect"));
        var list = new List<EmittedFile>();
        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.IsJoinTable) continue;
            if (!entity.HasPrimaryKey) continue;
            var data = BuildData(ctx, entity);
            var rendered = Renderer.Render(template, data);
            var path = CleanLayout.EntityPath(ctx.Config.ProjectName, entity.EntityTypeName);
            list.Add(new EmittedFile(path, rendered));
        }
        return list;
    }

    static object BuildData(EmitterContext ctx, NamedEntity entity)
    {
        var corrections = ctx.NamingCorrections;
        var factoryArgs = entity.Table.Columns
            .Where(c => !c.IsServerGenerated)
            .ToList();

        var invariantLines = new List<string>();
        foreach (var col in factoryArgs)
        {
            var paramName = Artect.Naming.CasingHelper.ToCamelCase(col.Name, corrections);
            if (!col.IsNullable && col.ClrType == ClrType.String)
            {
                invariantLines.Add($"if (string.IsNullOrWhiteSpace({paramName})) errors.Add(new {CleanLayout.DomainCommonNamespace(ctx.Config.ProjectName)}.DomainError(\"{paramName}\", \"required\", \"{col.Name} is required.\"));");
            }
            if (col.ClrType == ClrType.String && col.MaxLength is int max && max > 0)
            {
                invariantLines.Add($"if ({paramName} is {{ Length: > {max} }}) errors.Add(new {CleanLayout.DomainCommonNamespace(ctx.Config.ProjectName)}.DomainError(\"{paramName}\", \"maxLength\", \"{col.Name} must be at most {max} characters.\"));");
            }
        }

        var createArgs = factoryArgs.Select((c, i) => new
        {
            ClrTypeWithNullability = ClrTypeString(c),
            ParamName = Artect.Naming.CasingHelper.ToCamelCase(c.Name, corrections),
            PropertyName = Artect.Naming.EntityNaming.PropertyName(c, corrections),
            Comma = i < factoryArgs.Count - 1 ? "," : "",
        }).ToList();

        return new
        {
            HasUsingNamespaces = false,
            UsingNamespaces = Array.Empty<string>(),
            Namespace = $"{CleanLayout.DomainNamespace(ctx.Config.ProjectName)}.Entities",
            DomainCommonNamespace = CleanLayout.DomainCommonNamespace(ctx.Config.ProjectName),
            EntityName = entity.EntityTypeName,
            Columns = entity.Table.Columns.Select(c => new
            {
                ClrTypeWithNullability = ClrTypeString(c),
                PropertyName = Artect.Naming.EntityNaming.PropertyName(c, corrections),
                Initializer = c.ClrType == ClrType.String && !c.IsNullable ? " = default!;" : string.Empty,
            }).ToList(),
            HasReferenceNavigations = entity.ReferenceNavigations.Count > 0,
            ReferenceNavigations = entity.ReferenceNavigations.Select(n => new
            {
                TypeName = n.TargetEntityTypeName,
                PropertyName = n.PropertyName,
            }).ToList(),
            HasCollectionNavigations = entity.CollectionNavigations.Count > 0,
            CollectionNavigations = entity.CollectionNavigations.Select(n => new
            {
                TypeName = n.TargetEntityTypeName,
                PropertyName = n.PropertyName,
            }).ToList(),
            CreateArgs = createArgs,
            Invariants = invariantLines.Select(l => new { Line = l }).ToList(),
        };
    }

    static string ClrTypeString(Column c)
    {
        var cs = SqlTypeMap.ToCs(c.ClrType);
        if (c.IsNullable && SqlTypeMap.IsValueType(c.ClrType)) return cs + "?";
        if (c.IsNullable && c.ClrType == ClrType.String) return cs + "?";
        return cs;
    }
}
