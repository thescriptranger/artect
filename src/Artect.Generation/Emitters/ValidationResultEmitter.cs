using System.Collections.Generic;
using Artect.Templating;

namespace Artect.Generation.Emitters;

public sealed class ValidationResultEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var template = TemplateParser.Parse(ctx.Templates.Load("ValidationResult.cs.artect"));
        var errorsNs = $"{CleanLayout.SharedNamespace(ctx.Config.ProjectName)}.Errors";
        var appNs = $"{CleanLayout.ApplicationNamespace(ctx.Config.ProjectName)}.Validators";
        var data = new
        {
            ErrorsNamespace = errorsNs,
            Namespace = appNs,
        };
        var rendered = Renderer.Render(template, data);
        var path = CleanLayout.ValidatorPath(ctx.Config.ProjectName, "ValidationResult");
        return new[] { new EmittedFile(path, rendered) };
    }
}
