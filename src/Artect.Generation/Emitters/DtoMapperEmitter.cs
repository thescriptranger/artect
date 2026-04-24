using System.Collections.Generic;
using Artect.Templating;

namespace Artect.Generation.Emitters;

public sealed class DtoMapperEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var project = ctx.Config.ProjectName;
        var data = new { Namespace = CleanLayout.InfrastructureMappingNamespace(project) };
        var template = TemplateParser.Parse(ctx.Templates.Load("DtoMapper.cs.artect"));
        var rendered = Renderer.Render(template, data);
        return new[] { new EmittedFile(CleanLayout.InfrastructureMappingPath(project, "DtoMapper"), rendered) };
    }
}
