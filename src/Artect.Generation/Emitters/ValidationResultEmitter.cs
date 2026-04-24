using System.Collections.Generic;
using Artect.Templating;

namespace Artect.Generation.Emitters;

public sealed class ValidationResultEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var project = ctx.Config.ProjectName;
        var data = new { Namespace = CleanLayout.ApplicationValidatorsNamespace(project) };
        var template = TemplateParser.Parse(ctx.Templates.Load("ValidationResult.cs.artect"));
        var rendered = Renderer.Render(template, data);
        return new[] { new EmittedFile(CleanLayout.ApplicationValidatorsPath(project, "ValidationResult"), rendered) };
    }
}
