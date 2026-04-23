using System.Collections.Generic;
using Artect.Templating;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits pipeline behavior decorator classes into
/// <c>src/&lt;Project&gt;.Application/Common/Behaviors/</c>:
/// <list type="bullet">
///   <item><c>ValidationBehavior&lt;TRequest, TPayload&gt;</c> — runs <c>IValidator&lt;TRequest&gt;</c> (if any) and short-circuits with <c>UseCaseResult&lt;TPayload&gt;.ValidationFailed</c>.</item>
///   <item><c>LoggingBehavior&lt;TRequest, TResult&gt;</c> — structured logging around execution.</item>
///   <item><c>TransactionBehavior&lt;TRequest, TPayload&gt;</c> — commits <c>IUnitOfWork</c> on success; constrained to <c>ICommand</c>.</item>
/// </list>
/// Each behavior implements <c>IUseCase&lt;TRequest, TResult&gt;</c> so they can slot into a
/// decorator chain composed by the Application service installer.
/// </summary>
public sealed class PipelineBehaviorsEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var project = ctx.Config.ProjectName;
        var data = new
        {
            Namespace = $"{CleanLayout.ApplicationCommonNamespace(project)}.Behaviors",
            ValidatorsNamespace = $"{project}.Application.Validators",
            ErrorsNamespace = CleanLayout.ApplicationErrorsNamespace(project),
            UseCasesNamespace = $"{project}.Application.UseCases",
            CommonNamespace = CleanLayout.ApplicationCommonNamespace(project),
            PortsNamespace = CleanLayout.PortsNamespace(project),
        };
        var behaviorDir = $"src/{project}.Application/Common/Behaviors";
        return new[]
        {
            new EmittedFile(
                $"{behaviorDir}/ValidationBehavior.cs",
                Renderer.Render(TemplateParser.Parse(ctx.Templates.Load("ValidationBehavior.cs.artect")), data)),
            new EmittedFile(
                $"{behaviorDir}/LoggingBehavior.cs",
                Renderer.Render(TemplateParser.Parse(ctx.Templates.Load("LoggingBehavior.cs.artect")), data)),
            new EmittedFile(
                $"{behaviorDir}/TransactionBehavior.cs",
                Renderer.Render(TemplateParser.Parse(ctx.Templates.Load("TransactionBehavior.cs.artect")), data)),
        };
    }
}
