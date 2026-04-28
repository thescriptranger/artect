using System.Collections.Generic;
using System.Text;
using Artect.Templating;

namespace Artect.Generation.Emitters;

public sealed class DomainCommonEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var project = ctx.Config.ProjectName;
        var ns = CleanLayout.DomainCommonNamespace(project);
        var data = new { Namespace = ns };

        var result = TemplateParser.Parse(ctx.Templates.Load("Result.cs.artect"));
        var error  = TemplateParser.Parse(ctx.Templates.Load("DomainError.cs.artect"));

        var ex = new StringBuilder();
        ex.AppendLine($"namespace {ns};");
        ex.AppendLine();
        ex.AppendLine("/// <summary>");
        ex.AppendLine("/// Thrown by generated handlers when a domain factory (Entity.Create) fails.");
        ex.AppendLine("/// Translated to a 400 ProblemDetails response by GlobalExceptionHandler.");
        ex.AppendLine("/// </summary>");
        ex.AppendLine("public sealed class DomainValidationException(System.Collections.Generic.IReadOnlyList<DomainError> errors)");
        ex.AppendLine("    : System.Exception(\"Domain validation failed.\")");
        ex.AppendLine("{");
        ex.AppendLine("    public System.Collections.Generic.IReadOnlyList<DomainError> Errors { get; } = errors;");
        ex.AppendLine("}");

        var files = new List<EmittedFile>
        {
            new(CleanLayout.DomainCommonPath(project, "Result"), Renderer.Render(result, data)),
            new(CleanLayout.DomainCommonPath(project, "DomainError"), Renderer.Render(error, data)),
            new(CleanLayout.DomainCommonPath(project, "DomainValidationException"), ex.ToString()),
        };

        if (ctx.Config.EnableDomainEvents)
        {
            var ev = new StringBuilder();
            ev.AppendLine($"namespace {ns};");
            ev.AppendLine();
            ev.AppendLine("/// <summary>");
            ev.AppendLine("/// Marker for a domain event raised by an aggregate. Carries the moment the event occurred.");
            ev.AppendLine("/// </summary>");
            ev.AppendLine("public interface IDomainEvent");
            ev.AppendLine("{");
            ev.AppendLine("    System.DateTime OccurredAtUtc { get; }");
            ev.AppendLine("}");
            files.Add(new EmittedFile(CleanLayout.DomainCommonPath(project, "IDomainEvent"), ev.ToString()));

            var has = new StringBuilder();
            has.AppendLine($"namespace {ns};");
            has.AppendLine();
            has.AppendLine("/// <summary>");
            has.AppendLine("/// Implemented by aggregate roots that buffer domain events for dispatch via the SaveChanges interceptor.");
            has.AppendLine("/// </summary>");
            has.AppendLine("public interface IHasDomainEvents");
            has.AppendLine("{");
            has.AppendLine("    System.Collections.Generic.IReadOnlyCollection<IDomainEvent> DomainEvents { get; }");
            has.AppendLine("    void ClearDomainEvents();");
            has.AppendLine("}");
            files.Add(new EmittedFile(CleanLayout.DomainCommonPath(project, "IHasDomainEvents"), has.ToString()));
        }

        return files;
    }
}
