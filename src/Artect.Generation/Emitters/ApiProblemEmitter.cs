using System.Collections.Generic;
using Artect.Templating;

namespace Artect.Generation.Emitters;

public sealed class ApiProblemEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var template = TemplateParser.Parse(ctx.Templates.Load("ApiProblem.cs.artect"));
        var errorsNs = $"{CleanLayout.SharedNamespace(ctx.Config.ProjectName)}.Errors";
        var data = new
        {
            ErrorsNamespace = errorsNs,
            Namespace = errorsNs,
        };
        var rendered = Renderer.Render(template, data);
        var path = CleanLayout.SharedErrorPath(ctx.Config.ProjectName, "ApiProblem");
        return new[] { new EmittedFile(path, rendered) };
    }
}
