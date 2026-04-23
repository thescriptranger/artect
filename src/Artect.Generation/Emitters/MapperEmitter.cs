using System.Collections.Generic;
using System.Linq;
using Artect.Core.Schema;
using Artect.Naming;
using Artect.Templating;

namespace Artect.Generation.Emitters;

public sealed class MapperEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var template = TemplateParser.Parse(ctx.Templates.Load("Mapper.cs.artect"));
        var list = new List<EmittedFile>();

        var dtoNs      = $"{CleanLayout.ApplicationNamespace(ctx.Config.ProjectName)}.Dtos";
        var entityNs   = $"{CleanLayout.DomainNamespace(ctx.Config.ProjectName)}.Entities";
        var requestsNs = $"{CleanLayout.SharedNamespace(ctx.Config.ProjectName)}.Requests";
        var responsesNs = $"{CleanLayout.SharedNamespace(ctx.Config.ProjectName)}.Responses";
        var mapperNs   = $"{CleanLayout.ApplicationNamespace(ctx.Config.ProjectName)}.Mappings";

        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.IsJoinTable) continue;
            if (!entity.HasPrimaryKey) continue;

            var pkCols = entity.Table.PrimaryKey!.ColumnNames
                .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

            // All columns (for ToDto, ToEntity, ToResponse).
            var allCols = entity.Table.Columns.Select(c => new
            {
                PropertyName = EntityNaming.PropertyName(c),
            }).ToList();

            // Non-PK, non-server-generated columns (for Create/Update mappers).
            var nonPkCols = entity.Table.Columns
                .Where(c => !(pkCols.Contains(c.Name) && c.IsServerGenerated))
                .Select(c => new
                {
                    PropertyName = EntityNaming.PropertyName(c),
                })
                .ToList();

            var data = new
            {
                DtoNamespace      = dtoNs,
                EntityNamespace   = entityNs,
                RequestsNamespace = requestsNs,
                ResponsesNamespace = responsesNs,
                Namespace         = mapperNs,
                EntityName        = entity.EntityTypeName,
                Columns           = allCols,
                NonPkColumns      = nonPkCols,
            };

            var rendered = Renderer.Render(template, data);
            var path = CleanLayout.MapperPath(ctx.Config.ProjectName, entity.EntityTypeName);
            list.Add(new EmittedFile(path, rendered));
        }

        return list;
    }
}
