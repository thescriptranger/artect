using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// V#12: emits the SaveChanges interceptor that centralizes audit-field stamping
/// and tenant-id stamping, plus a default <c>NoTenantContext</c> implementation in
/// Infrastructure (when any entity uses TenantId). Only runs when at least one
/// entity carries an Audit, TenantId, or SoftDeleteFlag column.
/// </summary>
public sealed class InfrastructureInterceptorsEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        if (ctx.Config.DataAccess != DataAccessKind.EfCore)
            return System.Array.Empty<EmittedFile>();

        var anyAudit = ctx.Model.Entities.Any(e => e.AnyColumnHasFlag(ColumnMetadata.Audit));
        var anyTenant = ctx.Model.Entities.Any(e => e.AnyColumnHasFlag(ColumnMetadata.TenantId));
        if (!anyAudit && !anyTenant) return System.Array.Empty<EmittedFile>();

        var project = ctx.Config.ProjectName;
        var ns = $"{CleanLayout.InfrastructureNamespace(project)}.Interceptors";
        var dir = $"{CleanLayout.InfrastructureDir(project)}/Interceptors";
        var entityNs = $"{CleanLayout.DomainNamespace(project)}.Entities";
        var appAbsNs = CleanLayout.ApplicationAbstractionsNamespace(project);

        var list = new List<EmittedFile>
        {
            new EmittedFile(
                $"{dir}/AuditingSaveChangesInterceptor.cs",
                BuildInterceptor(ctx, ns, entityNs, appAbsNs, anyTenant)),
        };

        if (anyTenant)
        {
            list.Add(new EmittedFile(
                $"{CleanLayout.InfrastructureDir(project)}/NoTenantContext.cs",
                BuildNoTenantContext(CleanLayout.InfrastructureNamespace(project), appAbsNs)));
        }

        return list;
    }

    static string BuildInterceptor(EmitterContext ctx, string ns, string entityNs, string appAbsNs, bool tenantInjected)
    {
        var corrections = ctx.NamingCorrections;
        // For each entity, gather the Audit + TenantId columns we need to stamp. The
        // interceptor uses runtime ChangeTracker entries, so it can iterate any entity
        // type — but we pre-emit a typed switch so the stamps are statically checked.
        var auditEntries = new List<(string EntityType, List<(string Prop, bool StampOnUpdate)> AuditCols, string? TenantProp)>();
        foreach (var entity in ctx.Model.Entities.OrderBy(e => e.EntityTypeName, System.StringComparer.Ordinal))
        {
            var auditCols = new List<(string Prop, bool StampOnUpdate)>();
            string? tenantProp = null;
            foreach (var col in entity.Table.Columns)
            {
                var prop = EntityNaming.PropertyName(col, corrections);
                if (entity.ColumnHasFlag(col.Name, ColumnMetadata.Audit))
                {
                    // Audit + ProtectedFromUpdate => stamp once on insert (CreatedAtUtc).
                    // Audit alone => stamp on every save (UpdatedAtUtc).
                    var stampOnUpdate = !entity.ColumnHasFlag(col.Name, ColumnMetadata.ProtectedFromUpdate);
                    auditCols.Add((prop, stampOnUpdate));
                }
                if (entity.ColumnHasFlag(col.Name, ColumnMetadata.TenantId))
                    tenantProp = prop;
            }
            if (auditCols.Count == 0 && tenantProp is null) continue;
            auditEntries.Add((entity.EntityTypeName, auditCols, tenantProp));
        }

        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        sb.AppendLine("using Microsoft.EntityFrameworkCore.ChangeTracking;");
        sb.AppendLine("using Microsoft.EntityFrameworkCore.Diagnostics;");
        sb.AppendLine($"using {entityNs};");
        if (tenantInjected)
            sb.AppendLine($"using {appAbsNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// V#12 SaveChanges interceptor. Centralizes audit-field stamping and tenant-id");
        sb.AppendLine("/// stamping for every Added / Modified entity. Audit columns flagged");
        sb.AppendLine("/// `ProtectedFromUpdate` are stamped on Insert only (CreatedAtUtc pattern);");
        sb.AppendLine("/// audit columns without that flag are stamped on every save (UpdatedAtUtc).");
        sb.AppendLine("/// TenantId columns are stamped on Insert from <see cref=\"ITenantContext.CurrentTenantId\"/>.");
        sb.AppendLine("/// </summary>");
        if (tenantInjected)
            sb.AppendLine("public sealed class AuditingSaveChangesInterceptor(ITenantContext tenantContext) : SaveChangesInterceptor");
        else
            sb.AppendLine("public sealed class AuditingSaveChangesInterceptor : SaveChangesInterceptor");
        sb.AppendLine("{");
        sb.AppendLine("    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)");
        sb.AppendLine("    {");
        sb.AppendLine("        Stamp(eventData.Context);");
        sb.AppendLine("        return base.SavingChangesAsync(eventData, result, cancellationToken);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)");
        sb.AppendLine("    {");
        sb.AppendLine("        Stamp(eventData.Context);");
        sb.AppendLine("        return base.SavingChanges(eventData, result);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    void Stamp(DbContext? dbContext)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (dbContext is null) return;");
        sb.AppendLine("        var now = DateTime.UtcNow;");
        if (tenantInjected)
            sb.AppendLine("        var tenantId = tenantContext.CurrentTenantId;");
        sb.AppendLine();
        sb.AppendLine("        foreach (var entry in dbContext.ChangeTracker.Entries())");
        sb.AppendLine("        {");
        sb.AppendLine("            if (entry.State != EntityState.Added && entry.State != EntityState.Modified) continue;");
        sb.AppendLine("            switch (entry.Entity)");
        sb.AppendLine("            {");
        foreach (var (entityType, auditCols, tenantProp) in auditEntries)
        {
            sb.AppendLine($"                case {entityType} typed:");
            foreach (var (prop, stampOnUpdate) in auditCols)
            {
                if (stampOnUpdate)
                {
                    // Stamp on both Added and Modified.
                    sb.AppendLine($"                    entry.Property(\"{prop}\").CurrentValue = now;");
                }
                else
                {
                    // Stamp only on Added (CreatedAtUtc-style).
                    sb.AppendLine($"                    if (entry.State == EntityState.Added) entry.Property(\"{prop}\").CurrentValue = now;");
                }
            }
            if (tenantProp is not null)
            {
                if (tenantInjected)
                {
                    // Stamp tenant on Added only — once a row belongs to a tenant it doesn't move.
                    sb.AppendLine($"                    if (entry.State == EntityState.Added) entry.Property(\"{tenantProp}\").CurrentValue = tenantId;");
                }
            }
            sb.AppendLine("                    _ = typed;");
            sb.AppendLine("                    break;");
        }
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string BuildNoTenantContext(string infraNs, string appAbsNs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine($"using {appAbsNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {infraNs};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// V#12 default <see cref=\"ITenantContext\"/> implementation. Returns Guid.Empty so");
        sb.AppendLine("/// the generated solution compiles and runs out of the box. Replace with a real");
        sb.AppendLine("/// HttpContext-aware implementation in the Api layer (read the tenant claim from");
        sb.AppendLine("/// the authenticated user) and re-register before going to production —");
        sb.AppendLine("/// otherwise every row is stamped Guid.Empty and every query filters on Guid.Empty.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public sealed class NoTenantContext : ITenantContext");
        sb.AppendLine("{");
        sb.AppendLine("    public Guid CurrentTenantId => Guid.Empty;");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
