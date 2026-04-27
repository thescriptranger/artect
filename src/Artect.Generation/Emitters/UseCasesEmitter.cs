using System.Collections.Generic;
using System.Text;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits the IRequestHandler&lt;TCommand, TResult&gt; interface into
/// Application/UseCases/. This is the extension-point pattern for hand-written
/// business-action use cases that span multiple aggregates (RegisterCustomer,
/// AssignCustomerToClub, …). Generated CRUD handlers do NOT implement this
/// interface — they are sealed partial classes so the user extends them via
/// partial methods. Use this interface only for cross-aggregate operations the
/// schema cannot infer.
/// </summary>
public sealed class UseCasesEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var project = ctx.Config.ProjectName;
        var ns      = CleanLayout.ApplicationUseCasesNamespace(project);

        var sb = new StringBuilder();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Implement this interface for hand-written cross-aggregate use cases.");
        sb.AppendLine("/// Register your handler in <c>AddApplication()</c>:");
        sb.AppendLine("/// <code>");
        sb.AppendLine("/// services.AddScoped&lt;IRequestHandler&lt;RegisterCustomerCommand, CustomerDto&gt;, RegisterCustomerHandler&gt;();");
        sb.AppendLine("/// </code>");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public interface IRequestHandler<TCommand, TResult>");
        sb.AppendLine("{");
        sb.AppendLine("    Task<TResult> HandleAsync(TCommand command, CancellationToken ct);");
        sb.AppendLine("}");

        return new[]
        {
            new EmittedFile(
                CleanLayout.ApplicationUseCasesPath(project, "IRequestHandler"),
                sb.ToString()),
        };
    }
}
