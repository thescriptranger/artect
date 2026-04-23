using System.Collections.Generic;
using Artect.Templating;

namespace Artect.Generation.Emitters;

public sealed class ValidationErrorEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var template = TemplateParser.Parse(ctx.Templates.Load("ValidationError.cs.artect"));
        var ns = $"{CleanLayout.SharedNamespace(ctx.Config.ProjectName)}.Errors";
        var data = new
        {
            Namespace = ns,
        };
        var rendered = Renderer.Render(template, data);
        var path = CleanLayout.SharedErrorPath(ctx.Config.ProjectName, "ValidationError");
        return new[] { new EmittedFile(path, rendered) };
    }
}
