using System.Collections.Generic;
using Artect.Templating;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits <c>ValidationResult.cs</c> into <c>src/&lt;Project&gt;.Application/Validators/</c>.
/// The file bundles <c>ValidationError</c>, <c>ValidationResult</c>, and <c>IValidator&lt;T&gt;</c>
/// in the same namespace so the Application layer does not depend on Shared for validation primitives.
/// </summary>
public sealed class ValidationResultEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var template = TemplateParser.Parse(ctx.Templates.Load("ValidationResult.cs.artect"));
        var appNs = $"{CleanLayout.ApplicationNamespace(ctx.Config.ProjectName)}.Validators";
        var data = new
        {
            Namespace = appNs,
        };
        var rendered = Renderer.Render(template, data);
        var path = CleanLayout.ValidatorPath(ctx.Config.ProjectName, "ValidationResult");
        return new[] { new EmittedFile(path, rendered) };
    }
}
