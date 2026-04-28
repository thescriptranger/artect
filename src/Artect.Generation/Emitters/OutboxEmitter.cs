using System.Collections.Generic;
using System.Text;
using Artect.Config;

namespace Artect.Generation.Emitters;

/// <summary>
/// V#13: emits the transactional-outbox infrastructure — the <c>OutboxMessage</c>
/// entity and its EF Core configuration. The outbox enforces at-least-once delivery
/// of domain events: the SaveChanges interceptor enrolls pending events as
/// <c>OutboxMessage</c> rows in the same transaction as the aggregate write, and a
/// background dispatcher publishes them out-of-band.
///
/// Only emits when <see cref="ArtectConfig.EnableDomainEvents"/> is true and the
/// chosen data access is EF Core (Dapper-only solutions don't get the outbox until
/// a hand-written equivalent is needed).
/// </summary>
public sealed class OutboxEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        if (!ctx.Config.EnableDomainEvents) return System.Array.Empty<EmittedFile>();
        if (ctx.Config.DataAccess != DataAccessKind.EfCore) return System.Array.Empty<EmittedFile>();

        var project = ctx.Config.ProjectName;
        var infraNs = CleanLayout.InfrastructureNamespace(project);
        var ns = $"{infraNs}.Outbox";
        var dir = $"{CleanLayout.InfrastructureDir(project)}/Outbox";

        return new[]
        {
            new EmittedFile($"{dir}/OutboxMessage.cs", BuildOutboxMessage(ns)),
            new EmittedFile($"{dir}/OutboxMessageConfiguration.cs", BuildConfiguration(ns)),
        };
    }

    static string BuildOutboxMessage(string ns)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// V#13: a queued domain-event delivery record. Written by the SaveChanges");
        sb.AppendLine("/// interceptor in the same transaction as the aggregate; consumed by the");
        sb.AppendLine("/// outbox dispatcher background service. <see cref=\"ProcessedAtUtc\"/> is set");
        sb.AppendLine("/// when the publisher succeeds; rows where it remains null are eligible for");
        sb.AppendLine("/// retry. Lives in Infrastructure because the outbox is a transport detail,");
        sb.AppendLine("/// not a domain concept.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public sealed class OutboxMessage");
        sb.AppendLine("{");
        sb.AppendLine("    public Guid Id { get; set; }");
        sb.AppendLine("    public DateTime OccurredAtUtc { get; set; }");
        sb.AppendLine("    public string EventType { get; set; } = default!;");
        sb.AppendLine("    public string Payload { get; set; } = default!;");
        sb.AppendLine("    public DateTime? ProcessedAtUtc { get; set; }");
        sb.AppendLine("    public string? Error { get; set; }");
        sb.AppendLine("    public int AttemptCount { get; set; }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string BuildConfiguration(string ns)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        sb.AppendLine("using Microsoft.EntityFrameworkCore.Metadata.Builders;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>");
        sb.AppendLine("{");
        sb.AppendLine("    public void Configure(EntityTypeBuilder<OutboxMessage> builder)");
        sb.AppendLine("    {");
        sb.AppendLine("        builder.ToTable(\"OutboxMessages\");");
        sb.AppendLine("        builder.HasKey(x => x.Id);");
        sb.AppendLine("        builder.Property(x => x.EventType).HasMaxLength(512).IsRequired();");
        sb.AppendLine("        builder.Property(x => x.Payload).IsRequired();");
        sb.AppendLine("        builder.Property(x => x.Error).HasMaxLength(2000);");
        sb.AppendLine("        // Dispatcher reads pending rows in OccurredAtUtc order. The filtered");
        sb.AppendLine("        // index keeps the seek fast even when the table grows.");
        sb.AppendLine("        builder.HasIndex(x => new { x.ProcessedAtUtc, x.OccurredAtUtc })");
        sb.AppendLine("            .HasFilter(\"[ProcessedAtUtc] IS NULL\");");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
