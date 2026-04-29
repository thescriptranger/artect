using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;
using Artect.Templating;

namespace Artect.Generation.Emitters;

public sealed class ResponseEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var template = TemplateParser.Parse(ctx.Templates.Load("Response.cs.artect"));
        var list = new List<EmittedFile>();
        var project = ctx.Config.ProjectName;
        var ns = $"{CleanLayout.SharedNamespace(project)}.Responses";
        var includeChildren = ctx.Config.IncludeChildCollectionsInResponses;

        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.ShouldSkip(EntityClassification.AggregateRoot)) continue;

            var props = BuildPropertyMetadata(entity, ctx.NamingCorrections);

            var childCollections = includeChildren
                ? entity.CollectionNavigations
                    .Select(n => new
                    {
                        TypeName = n.TargetEntityTypeName,
                        PropertyName = n.PropertyName,
                    })
                    .ToList()
                : new();

            var data = new
            {
                Namespace = ns,
                EntityName = entity.EntityTypeName,
                Properties = props,
                HasChildCollections = includeChildren && childCollections.Count > 0,
                ChildCollections = childCollections,
            };
            var rendered = Renderer.Render(template, data);
            list.Add(new EmittedFile(
                CleanLayout.SharedResponsePath(project, entity.EntityTypeName),
                rendered));

            list.Add(new EmittedFile(
                CleanLayout.SharedSummaryResponsePath(project, entity.EntityTypeName),
                BuildSummary(ns, entity.EntityTypeName, props)));
        }

        return list;
    }

    static IReadOnlyList<ResponseProperty> BuildPropertyMetadata(NamedEntity entity, IReadOnlyDictionary<string, string> corrections) =>
        entity.Table.Columns
            .Where(c => !entity.ColumnHasFlag(c.Name, ColumnMetadata.Ignored)
                     && !entity.ColumnHasFlag(c.Name, ColumnMetadata.Sensitive))
            .Select(c => new ResponseProperty(
                PropertyName: EntityNaming.PropertyName(c, corrections),
                ClrTypeWithNullability: ClrTypeString(c),
                Initializer: !c.IsNullable && c.ClrType == ClrType.String ? " = string.Empty;" : string.Empty,
                IsDeprecated: entity.ColumnHasFlag(c.Name, ColumnMetadata.Deprecated)))
            .ToList();

    static string ClrTypeString(Column c)
    {
        var cs = SqlTypeMap.ToCs(c.ClrType);
        if (c.IsNullable && SqlTypeMap.IsValueType(c.ClrType)) return cs + "?";
        if (c.IsNullable && c.ClrType == ClrType.String) return cs + "?";
        return cs;
    }

    static string BuildSummary(string ns, string entityName, IReadOnlyList<ResponseProperty> props)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"public sealed record {entityName}SummaryResponse");
        sb.AppendLine("{");
        foreach (var p in props)
        {
            if (p.IsDeprecated)
                sb.AppendLine("    [System.Obsolete(\"Deprecated; do not use in new code.\")]");
            sb.AppendLine($"    public {p.ClrTypeWithNullability} {p.PropertyName} {{ get; init; }}{p.Initializer}");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    sealed record ResponseProperty(string PropertyName, string ClrTypeWithNullability, string Initializer, bool IsDeprecated);
}
