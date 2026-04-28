using System.Collections.Generic;
using System.Text;

namespace Artect.Generation.Emitters;

/// <summary>
/// V#6: emits the global Application use-case abstractions —
/// <c>ICommandHandler&lt;TCommand, TResult&gt;</c> and
/// <c>IQueryHandler&lt;TQuery, TResult&gt;</c> — into Application/Abstractions/,
/// alongside the existing <c>IUnitOfWork</c>. Adds a README to Application/UseCases/
/// documenting the extension pattern: business workflows that span multiple
/// aggregates implement these interfaces and auto-register via assembly scanning
/// in <c>AddApplication()</c>.
///
/// Generated CRUD handlers (Create/Update/Patch) implement
/// <c>ICommandHandler&lt;TCmd, TResult&gt;</c> via the <see cref="HandlerEmitter"/>
/// so user-defined decorators can wrap both generated and hand-written handlers
/// uniformly.
/// </summary>
public sealed class UseCasesEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var project = ctx.Config.ProjectName;
        var ns = CleanLayout.ApplicationAbstractionsNamespace(project);

        return new[]
        {
            new EmittedFile(
                CleanLayout.ApplicationAbstractionsPath(project, "ICommandHandler"),
                BuildCommandHandler(ns)),
            new EmittedFile(
                CleanLayout.ApplicationAbstractionsPath(project, "IQueryHandler"),
                BuildQueryHandler(ns)),
            new EmittedFile(
                CleanLayout.ApplicationAbstractionsPath(project, "QueryValidationException"),
                BuildQueryValidationException(ns)),
            new EmittedFile(
                $"{CleanLayout.ApplicationDir(project)}/UseCases/README.md",
                BuildReadme(project)),
        };
    }

    static string BuildQueryValidationException(string ns)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// V#11: thrown by read services when an HTTP query parameter is invalid (unknown");
        sb.AppendLine("/// sort field, out-of-range page size, malformed filter, etc.). The");
        sb.AppendLine("/// GlobalExceptionHandler maps this to HTTP 400 ValidationProblemDetails so");
        sb.AppendLine("/// callers see a clear 'why' rather than a 500.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public sealed class QueryValidationException(string parameter, string message) : Exception(message)");
        sb.AppendLine("{");
        sb.AppendLine("    public string Parameter { get; } = parameter;");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string BuildCommandHandler(string ns)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Write-side use case: command in, result out. Implementations are auto-registered");
        sb.AppendLine("/// in <c>AddApplication()</c> via assembly scanning. Generated CRUD handlers");
        sb.AppendLine("/// (Create/Update/Patch) implement this interface so cross-cutting decorators");
        sb.AppendLine("/// (validation, transaction, audit) can wrap them uniformly.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public interface ICommandHandler<TCommand, TResult>");
        sb.AppendLine("{");
        sb.AppendLine("    Task<TResult> HandleAsync(TCommand command, CancellationToken ct);");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string BuildQueryHandler(string ns)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Read-side use case: query in, result out. Implementations are auto-registered");
        sb.AppendLine("/// in <c>AddApplication()</c> via assembly scanning. Schema-derived read services");
        sb.AppendLine("/// (<c>I&lt;Entity&gt;ReadService</c>) currently use a different shape; new business");
        sb.AppendLine("/// queries should adopt this interface.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public interface IQueryHandler<TQuery, TResult>");
        sb.AppendLine("{");
        sb.AppendLine("    Task<TResult> HandleAsync(TQuery query, CancellationToken ct);");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string BuildReadme(string project) => $$"""
        # Use cases

        Business workflows live here — operations that span multiple aggregates or
        encode domain logic the schema cannot infer. The generated CRUD in
        `Application/Features/<Plural>/` is **scaffolded baseline**, not real use
        cases; replace or extend it with hand-written use cases in this folder as
        the domain matures.

        ## Adding a use case

        1. Define a command (write) or query (read) record:
           ```csharp
           public sealed record RegisterCustomerCommand(string Name, string Email);
           ```

        2. Implement the matching abstraction from `{{project}}.Application.Abstractions`:
           ```csharp
           using {{project}}.Application.Abstractions;

           public sealed class RegisterCustomerHandler
               : ICommandHandler<RegisterCustomerCommand, CustomerDto>
           {
               public async Task<CustomerDto> HandleAsync(
                   RegisterCustomerCommand command, CancellationToken ct)
               {
                   // ... domain orchestration ...
               }
           }
           ```

        3. **No DI wiring needed.** `AddApplication()` scans this assembly for
           classes implementing `ICommandHandler<,>` or `IQueryHandler<,>` and
           registers each one against the interface(s) it implements.

        4. Add an endpoint in `Api/Endpoints/` that injects the abstraction:
           ```csharp
           group.MapPost("/register",
               async (RegisterCustomerCommand cmd,
                      ICommandHandler<RegisterCustomerCommand, CustomerDto> handler,
                      CancellationToken ct) =>
               {
                   var dto = await handler.HandleAsync(cmd, ct);
                   return Results.Ok(dto);
               });
           ```

        ## Generated CRUD vs hand-written use cases

        | Aspect | Generated CRUD | Hand-written use case |
        | --- | --- | --- |
        | Lives in | `Application/Features/<Plural>/` | `Application/UseCases/` |
        | Shape | One handler per HTTP verb per aggregate | Domain-named, may span aggregates |
        | DI registration | Concrete + via interface scan | Via interface scan |
        | Implements | `ICommandHandler<TCmd, TResult>` | `ICommandHandler<,>` or `IQueryHandler<,>` |

        Both wire through the same abstraction, so decorators (validation,
        transaction, audit, authorization) added later wrap them uniformly.
        """;
}
