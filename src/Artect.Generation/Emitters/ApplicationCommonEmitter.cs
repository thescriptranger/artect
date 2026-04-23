using System.Collections.Generic;
using Artect.Templating;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits Application-internal marker types and shared primitives:
/// <c>ICommand</c>, <c>IQuery</c>, <c>IUseCase</c>, <c>Unit</c>, <c>PagedResult</c> (under <c>.Common</c>)
/// plus <c>ApplicationError</c> (under <c>.Errors</c>).
/// </summary>
public sealed class ApplicationCommonEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var commonNs = CleanLayout.ApplicationCommonNamespace(ctx.Config.ProjectName);
        var errorsNs = CleanLayout.ApplicationErrorsNamespace(ctx.Config.ProjectName);

        EmittedFile RenderCommon(string templateName, string className) =>
            new EmittedFile(
                CleanLayout.ApplicationCommonPath(ctx.Config.ProjectName, className),
                Renderer.Render(TemplateParser.Parse(ctx.Templates.Load(templateName)), new { Namespace = commonNs }));

        return new[]
        {
            RenderCommon("ICommand.cs.artect", "ICommand"),
            RenderCommon("IQuery.cs.artect", "IQuery"),
            RenderCommon("IUseCase.cs.artect", "IUseCase"),
            RenderCommon("IPipelineBehavior.cs.artect", "IPipelineBehavior"),
            RenderCommon("Unit.cs.artect", "Unit"),
            RenderCommon("PagedResult.cs.artect", "PagedResult"),
            new EmittedFile(
                CleanLayout.ApplicationErrorsPath(ctx.Config.ProjectName, "ApplicationError"),
                Renderer.Render(TemplateParser.Parse(ctx.Templates.Load("ApplicationError.cs.artect")), new { Namespace = errorsNs })),
        };
    }
}
