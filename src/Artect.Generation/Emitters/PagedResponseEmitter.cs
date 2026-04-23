using System.Collections.Generic;
using Artect.Templating;

namespace Artect.Generation.Emitters;

public sealed class PagedResponseEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var template = TemplateParser.Parse(ctx.Templates.Load("PagedResponse.cs.artect"));
        var ns = $"{CleanLayout.SharedNamespace(ctx.Config.ProjectName)}.Responses";
        var data = new
        {
            Namespace = ns,
        };
        var rendered = Renderer.Render(template, data);
        var path = CleanLayout.SharedPagedResponsePath(ctx.Config.ProjectName);
        return new[] { new EmittedFile(path, rendered) };
    }
}
