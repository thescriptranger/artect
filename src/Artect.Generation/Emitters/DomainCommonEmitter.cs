using System.Collections.Generic;
using Artect.Templating;

namespace Artect.Generation.Emitters;

public sealed class DomainCommonEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var ns = CleanLayout.DomainCommonNamespace(ctx.Config.ProjectName);
        var data = new { Namespace = ns };

        var result = TemplateParser.Parse(ctx.Templates.Load("Result.cs.artect"));
        var error = TemplateParser.Parse(ctx.Templates.Load("DomainError.cs.artect"));

        return new[]
        {
            new EmittedFile(CleanLayout.DomainCommonPath(ctx.Config.ProjectName, "Result"), Renderer.Render(result, data)),
            new EmittedFile(CleanLayout.DomainCommonPath(ctx.Config.ProjectName, "DomainError"), Renderer.Render(error, data)),
        };
    }
}
