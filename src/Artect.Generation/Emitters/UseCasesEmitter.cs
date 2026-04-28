using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;

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

        var files = new List<EmittedFile>
        {
            new EmittedFile(
                CleanLayout.ApplicationAbstractionsPath(project, "ICommandHandler"),
                BuildCommandHandler(ns)),
            new EmittedFile(
                CleanLayout.ApplicationAbstractionsPath(project, "IQueryHandler"),
                BuildQueryHandler(ns)),
            new EmittedFile(
                CleanLayout.ApplicationAbstractionsPath(project, "IRepository"),
                BuildRepositoryMarker(ns)),
            new EmittedFile(
                CleanLayout.ApplicationAbstractionsPath(project, "IReadService"),
                BuildReadServiceMarker(ns)),
            new EmittedFile(
                CleanLayout.ApplicationAbstractionsPath(project, "QueryValidationException"),
                BuildQueryValidationException(ns)),
            new EmittedFile(
                $"{CleanLayout.ApplicationDir(project)}/UseCases/README.md",
                BuildReadme(project)),
        };

        // V#12: emit ITenantContext only when at least one entity has a TenantId column.
        // Putting the abstraction in Application.Abstractions keeps Infrastructure (which
        // implements DbContext + interceptors) inverted: it depends on this contract, not
        // on a concrete HttpContext-aware type. Api supplies the real implementation.
        var anyTenant = ctx.Model.Entities.Any(e => e.AnyColumnHasFlag(ColumnMetadata.TenantId));
        if (anyTenant)
        {
            files.Add(new EmittedFile(
                CleanLayout.ApplicationAbstractionsPath(project, "ITenantContext"),
                BuildTenantContext(ns)));
        }

        // V#13: when domain events are enabled, emit IDomainEventPublisher in the
        // Application layer so the SaveChanges interceptor (Infrastructure) can depend
        // on it through DIP. The default implementation is a logger that writes events
        // to ILogger; replace it with a transport-backed publisher (Service Bus, Kafka,
        // etc.) without touching Infrastructure.
        if (ctx.Config.EnableDomainEvents)
        {
            var commonNs = CleanLayout.DomainCommonNamespace(project);
            files.Add(new EmittedFile(
                CleanLayout.ApplicationAbstractionsPath(project, "IDomainEventPublisher"),
                BuildDomainEventPublisher(ns, commonNs)));
        }

        return files;
    }

    static string BuildDomainEventPublisher(string ns, string commonNs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("public interface IDomainEventPublisher");
        sb.AppendLine("{");
        sb.AppendLine($"    Task PublishAsync({commonNs}.IDomainEvent domainEvent, CancellationToken ct);");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string BuildTenantContext(string ns)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("public interface ITenantContext");
        sb.AppendLine("{");
        sb.AppendLine("    Guid CurrentTenantId { get; }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string BuildRepositoryMarker(string ns)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("public interface IRepository");
        sb.AppendLine("{");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string BuildReadServiceMarker(string ns)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("public interface IReadService");
        sb.AppendLine("{");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string BuildQueryValidationException(string ns)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
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
