using System.Collections.Generic;
using Artect.Templating;

namespace Artect.Generation.Emitters;

public sealed class ApplicationDiEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var project = ctx.Config.ProjectName;
        var data = new { Namespace = CleanLayout.ApplicationNamespace(project) };
        var template = TemplateParser.Parse(ctx.Templates.Load("ApplicationDependencyInjection.cs.artect"));
        var rendered = Renderer.Render(template, data);
        return new[] { new EmittedFile($"{CleanLayout.ApplicationDir(project)}/DependencyInjection.cs", rendered) };
    }
}
