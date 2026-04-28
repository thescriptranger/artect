using System.Collections.Generic;
using System.Text;
using Artect.Config;

namespace Artect.Generation.Emitters;

/// <summary>
/// V#13: emits the outbox-dispatcher background service plus a default
/// logger-only <c>IDomainEventPublisher</c> implementation. The dispatcher polls
/// the <c>OutboxMessages</c> table for unprocessed rows, deserializes the payload
/// back into a typed <c>IDomainEvent</c>, and hands it to the publisher. On
/// success the row is marked processed; on failure the attempt count and last
/// error are persisted so retries are bounded by row state, not memory.
///
/// The publisher is intentionally minimal — switching to Service Bus / Kafka /
/// RabbitMQ is a swap of <c>IDomainEventPublisher</c> at the DI seam, no other
/// generated code changes.
/// </summary>
public sealed class OutboxDispatcherEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        if (!ctx.Config.EnableDomainEvents) return System.Array.Empty<EmittedFile>();
        if (ctx.Config.DataAccess != DataAccessKind.EfCore) return System.Array.Empty<EmittedFile>();

        var project = ctx.Config.ProjectName;
        var infraNs = CleanLayout.InfrastructureNamespace(project);
        var ns = $"{infraNs}.Outbox";
        var dir = $"{CleanLayout.InfrastructureDir(project)}/Outbox";
        var commonNs = CleanLayout.DomainCommonNamespace(project);
        var appAbsNs = CleanLayout.ApplicationAbstractionsNamespace(project);
        var dbCtx = $"{project}DbContext";
        var dataNs = $"{infraNs}.Data";

        return new[]
        {
            new EmittedFile($"{dir}/OutboxDispatcher.cs", BuildDispatcher(ns, commonNs, appAbsNs, dataNs, dbCtx)),
            new EmittedFile($"{dir}/LoggingDomainEventPublisher.cs", BuildLoggingPublisher(ns, commonNs, appAbsNs)),
        };
    }

    static string BuildDispatcher(string ns, string commonNs, string appAbsNs, string dataNs, string dbCtx)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using Microsoft.Extensions.Hosting;");
        sb.AppendLine("using Microsoft.Extensions.Logging;");
        sb.AppendLine($"using {commonNs};");
        sb.AppendLine($"using {appAbsNs};");
        sb.AppendLine($"using {dataNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("public sealed class OutboxDispatcher(");
        sb.AppendLine("    IServiceScopeFactory scopeFactory,");
        sb.AppendLine("    ILogger<OutboxDispatcher> logger) : BackgroundService");
        sb.AppendLine("{");
        sb.AppendLine("    static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);");
        sb.AppendLine("    const int BatchSize = 50;");
        sb.AppendLine();
        sb.AppendLine("    protected override async Task ExecuteAsync(CancellationToken stoppingToken)");
        sb.AppendLine("    {");
        sb.AppendLine("        while (!stoppingToken.IsCancellationRequested)");
        sb.AppendLine("        {");
        sb.AppendLine("            try");
        sb.AppendLine("            {");
        sb.AppendLine("                await DispatchOnceAsync(stoppingToken);");
        sb.AppendLine("            }");
        sb.AppendLine("            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)");
        sb.AppendLine("            {");
        sb.AppendLine("                break;");
        sb.AppendLine("            }");
        sb.AppendLine("            catch (Exception ex)");
        sb.AppendLine("            {");
        sb.AppendLine("                logger.LogError(ex, \"Outbox dispatch loop failed; will retry.\");");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            try");
        sb.AppendLine("            {");
        sb.AppendLine("                await Task.Delay(PollInterval, stoppingToken);");
        sb.AppendLine("            }");
        sb.AppendLine("            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)");
        sb.AppendLine("            {");
        sb.AppendLine("                break;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    async Task DispatchOnceAsync(CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine("        await using var scope = scopeFactory.CreateAsyncScope();");
        sb.AppendLine($"        var db = scope.ServiceProvider.GetRequiredService<{dbCtx}>();");
        sb.AppendLine("        var publisher = scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>();");
        sb.AppendLine();
        sb.AppendLine("        var pending = await db.Set<OutboxMessage>()");
        sb.AppendLine("            .Where(m => m.ProcessedAtUtc == null)");
        sb.AppendLine("            .OrderBy(m => m.OccurredAtUtc)");
        sb.AppendLine("            .Take(BatchSize)");
        sb.AppendLine("            .ToListAsync(ct);");
        sb.AppendLine();
        sb.AppendLine("        if (pending.Count == 0) return;");
        sb.AppendLine();
        sb.AppendLine("        foreach (var message in pending)");
        sb.AppendLine("        {");
        sb.AppendLine("            try");
        sb.AppendLine("            {");
        sb.AppendLine("                var clrType = Type.GetType(message.EventType, throwOnError: false);");
        sb.AppendLine("                if (clrType is null)");
        sb.AppendLine("                {");
        sb.AppendLine("                    message.AttemptCount++;");
        sb.AppendLine("                    message.Error = $\"Cannot resolve event type '{message.EventType}'.\";");
        sb.AppendLine("                    logger.LogError(\"Outbox message {Id} has unresolvable type {Type}; leaving unprocessed.\", message.Id, message.EventType);");
        sb.AppendLine("                    continue;");
        sb.AppendLine("                }");
        sb.AppendLine();
        sb.AppendLine("                if (JsonSerializer.Deserialize(message.Payload, clrType) is not IDomainEvent domainEvent)");
        sb.AppendLine("                {");
        sb.AppendLine("                    message.AttemptCount++;");
        sb.AppendLine("                    message.Error = \"Deserialized payload is not an IDomainEvent.\";");
        sb.AppendLine("                    continue;");
        sb.AppendLine("                }");
        sb.AppendLine();
        sb.AppendLine("                await publisher.PublishAsync(domainEvent, ct);");
        sb.AppendLine("                message.ProcessedAtUtc = DateTime.UtcNow;");
        sb.AppendLine("                message.Error = null;");
        sb.AppendLine("            }");
        sb.AppendLine("            catch (Exception ex)");
        sb.AppendLine("            {");
        sb.AppendLine("                message.AttemptCount++;");
        sb.AppendLine("                message.Error = ex.Message;");
        sb.AppendLine("                logger.LogError(ex, \"Outbox publish failed for {Id} (attempt {Attempt}).\", message.Id, message.AttemptCount);");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        await db.SaveChangesAsync(ct);");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string BuildLoggingPublisher(string ns, string commonNs, string appAbsNs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Microsoft.Extensions.Logging;");
        sb.AppendLine($"using {commonNs};");
        sb.AppendLine($"using {appAbsNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("public sealed class LoggingDomainEventPublisher(ILogger<LoggingDomainEventPublisher> logger) : IDomainEventPublisher");
        sb.AppendLine("{");
        sb.AppendLine("    public Task PublishAsync(IDomainEvent domainEvent, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine("        var eventType = domainEvent.GetType();");
        sb.AppendLine("        logger.LogInformation(\"Domain event {EventType} occurred at {OccurredAtUtc}: {Payload}\",");
        sb.AppendLine("            eventType.Name,");
        sb.AppendLine("            domainEvent.OccurredAtUtc,");
        sb.AppendLine("            JsonSerializer.Serialize(domainEvent, eventType));");
        sb.AppendLine("        return Task.CompletedTask;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
