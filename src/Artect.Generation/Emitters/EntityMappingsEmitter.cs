using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

public sealed class EntityMappingsEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var list = new List<EmittedFile>();
        var project = ctx.Config.ProjectName;
        var dtosNs = CleanLayout.ApplicationDtosNamespace(project);
        var sharedRespNs = CleanLayout.SharedResponsesNamespace(project);
        var apiMapNs = CleanLayout.ApiMappingsNamespace(project);
        var corrections = ctx.NamingCorrections;
        var includeChildren = ctx.Config.IncludeChildCollectionsInResponses;

        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.ShouldSkip(EntityClassification.AggregateRoot, EntityClassification.ReadModel)) continue;

            var name = entity.EntityTypeName;
            var sb = new StringBuilder();
            sb.AppendLine($"using {dtosNs};");
            sb.AppendLine($"using {sharedRespNs};");
            sb.AppendLine();
            sb.AppendLine($"namespace {apiMapNs};");
            sb.AppendLine();
            sb.AppendLine($"public static class {name}Mappings");
            sb.AppendLine("{");
            sb.AppendLine($"    public static {name}Response ToResponse(this {name}Dto dto) =>");
            sb.AppendLine("        new()");
            sb.AppendLine("        {");
            foreach (var col in entity.Table.Columns)
            {
                var prop = EntityNaming.PropertyName(col, corrections);
                sb.AppendLine($"            {prop} = dto.{prop},");
            }
            if (includeChildren)
            {
                foreach (var nav in entity.CollectionNavigations)
                {
                    sb.AppendLine($"            {nav.PropertyName} = dto.{nav.PropertyName}.Select(c => c.ToResponse()).ToArray(),");
                }
            }
            sb.AppendLine("        };");
            sb.AppendLine("}");

            list.Add(new EmittedFile(
                CleanLayout.ApiMappingsPath(project, $"{name}Mappings"),
                sb.ToString()));
        }
        return list;
    }
}
