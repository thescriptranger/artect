using System.Collections.Generic;
using Artect.Templating;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits <c>ApplicationErrorMappers.cs</c> into <c>src/&lt;Project&gt;.Api/Mapping/</c>.
/// Provides <c>.ToWire()</c> extension converting <c>ApplicationError</c> → <c>ValidationError</c>.
/// </summary>
public sealed class ApplicationErrorMappersEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var project = ctx.Config.ProjectName;
        var data = new
        {
            Namespace = CleanLayout.ApiMappingNamespace(project),
            ErrorsNamespace = CleanLayout.ApplicationErrorsNamespace(project),
            SharedErrorsNamespace = CleanLayout.SharedErrorsNamespace(project),
        };
        var template = TemplateParser.Parse(ctx.Templates.Load("ApplicationErrorMappers.cs.artect"));
        var rendered = Renderer.Render(template, data);
        return new[]
        {
            new EmittedFile(CleanLayout.ApiMapperPath(project, "ApplicationErrorMappers"), rendered),
        };
    }
}
