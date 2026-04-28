using System.Collections.Generic;
using Artect.Config;
using Artect.Templating;

namespace Artect.Generation.Emitters;

public sealed class EntityBehaviorEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var template = TemplateParser.Parse(ctx.Templates.Load("EntityBehaviorHook.cs.artect"));
        var list = new List<EmittedFile>();
        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.ShouldSkip(EntityClassification.AggregateRoot, EntityClassification.OwnedEntity)) continue;
            var data = new
            {
                Namespace = $"{CleanLayout.DomainNamespace(ctx.Config.ProjectName)}.Entities",
                EntityName = entity.EntityTypeName,
            };
            var rendered = Renderer.Render(template, data);
            var path = CleanLayout.EntityBehaviorPath(ctx.Config.ProjectName, entity.EntityTypeName);
            list.Add(new EmittedFile(path, rendered));
        }
        return list;
    }
}
