using System.Collections.Generic;
using Artect.Templating;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits DtoMapper.cs into <c>&lt;Project&gt;.Application.Mappings</c>.
/// Phase 2 moved this from Infrastructure/Mapping to Application/Mappings so
/// generated handlers in the Application layer can call it without a downward
/// layer reference.
/// </summary>
public sealed class DtoMapperEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var project = ctx.Config.ProjectName;
        var data = new { Namespace = CleanLayout.ApplicationMappingsNamespace(project) };
        var template = TemplateParser.Parse(ctx.Templates.Load("DtoMapper.cs.artect"));
        var rendered = Renderer.Render(template, data);
        return new[] { new EmittedFile(CleanLayout.ApplicationMappingsPath(project, "DtoMapper"), rendered) };
    }
}
