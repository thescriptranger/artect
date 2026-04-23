using System.Collections.Generic;
using System.Text;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits <c>UseCaseResult.cs</c> into <c>src/&lt;Project&gt;.Application/UseCases/</c>.
/// </summary>
public sealed class UseCaseResultEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var project = ctx.Config.ProjectName;
        var ns      = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";
        var errNs   = $"{CleanLayout.SharedNamespace(project)}.Errors";

        var sb = new StringBuilder();
        sb.AppendLine($"using System.Collections.Generic;");
        sb.AppendLine($"using {errNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("public abstract record UseCaseResult");
        sb.AppendLine("{");
        sb.AppendLine("    public sealed record Success : UseCaseResult;");
        sb.AppendLine("    public sealed record NotFound(string EntityType, string Id) : UseCaseResult;");
        sb.AppendLine("    public sealed record ValidationFailed(IReadOnlyList<ValidationError> Errors) : UseCaseResult;");
        sb.AppendLine("    public sealed record Conflict(string Message) : UseCaseResult;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("public abstract record UseCaseResult<T>");
        sb.AppendLine("{");
        sb.AppendLine("    public sealed record Success(T Value) : UseCaseResult<T>;");
        sb.AppendLine("    public sealed record NotFound(string EntityType, string Id) : UseCaseResult<T>;");
        sb.AppendLine("    public sealed record ValidationFailed(IReadOnlyList<ValidationError> Errors) : UseCaseResult<T>;");
        sb.AppendLine("    public sealed record Conflict(string Message) : UseCaseResult<T>;");
        sb.AppendLine("}");

        var path = CleanLayout.UseCaseResultPath(project);
        return new[] { new EmittedFile(path, sb.ToString()) };
    }
}
