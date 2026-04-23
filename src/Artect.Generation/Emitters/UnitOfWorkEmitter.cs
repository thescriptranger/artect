using System.Collections.Generic;
using System.Text;
using Artect.Config;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits the <c>IUnitOfWork</c> port into Application/Abstractions/Ports,
/// and the matching implementation (<c>EfUnitOfWork</c> or <c>DapperUnitOfWork</c>)
/// into Infrastructure/Data. Always runs regardless of other flags.
/// </summary>
public sealed class UnitOfWorkEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var project    = ctx.Config.ProjectName;
        var portsNs    = CleanLayout.PortsNamespace(project);
        var infraDataNs = $"{CleanLayout.InfrastructureNamespace(project)}.Data";
        var appPortsNs  = portsNs;

        var files = new List<EmittedFile>();

        // ── 1. IUnitOfWork interface ──────────────────────────────────────────
        var iface = new StringBuilder();
        iface.AppendLine($"namespace {portsNs};");
        iface.AppendLine();
        iface.AppendLine("public interface IUnitOfWork");
        iface.AppendLine("{");
        iface.AppendLine("    System.Threading.Tasks.Task<int> CommitAsync(System.Threading.CancellationToken ct = default);");
        iface.AppendLine("}");

        files.Add(new EmittedFile(
            CleanLayout.PortsPath(project, "IUnitOfWork"),
            iface.ToString()));

        // ── 2. Implementation ─────────────────────────────────────────────────
        if (ctx.Config.DataAccess == DataAccessKind.EfCore)
        {
            var dbCtx = $"{project}DbContext";

            var impl = new StringBuilder();
            impl.AppendLine($"using {appPortsNs};");
            impl.AppendLine($"using {infraDataNs};");
            impl.AppendLine();
            impl.AppendLine($"namespace {infraDataNs};");
            impl.AppendLine();
            impl.AppendLine("public sealed class EfUnitOfWork : IUnitOfWork");
            impl.AppendLine("{");
            impl.AppendLine($"    readonly {dbCtx} _db;");
            impl.AppendLine($"    public EfUnitOfWork({dbCtx} db) => _db = db;");
            impl.AppendLine();
            impl.AppendLine("    public System.Threading.Tasks.Task<int> CommitAsync(System.Threading.CancellationToken ct = default)");
            impl.AppendLine("        => _db.SaveChangesAsync(ct);");
            impl.AppendLine("}");

            files.Add(new EmittedFile(
                CleanLayout.DbContextPath(project, "EfUnitOfWork"),
                impl.ToString()));
        }
        else // Dapper
        {
            var impl = new StringBuilder();
            impl.AppendLine($"using {appPortsNs};");
            impl.AppendLine();
            impl.AppendLine($"namespace {infraDataNs};");
            impl.AppendLine();
            impl.AppendLine("/// <summary>");
            impl.AppendLine("/// Dapper has no ambient unit-of-work. Each repository call commits its own statement(s).");
            impl.AppendLine("/// Override this if you need cross-call transactional semantics (e.g. wrap calls in a shared");
            impl.AppendLine("/// <see cref=\"System.Transactions.TransactionScope\"/> or pass an <see cref=\"System.Data.IDbTransaction\"/>");
            impl.AppendLine("/// to repository methods).");
            impl.AppendLine("/// </summary>");
            impl.AppendLine("public sealed class DapperUnitOfWork : IUnitOfWork");
            impl.AppendLine("{");
            impl.AppendLine("    public System.Threading.Tasks.Task<int> CommitAsync(System.Threading.CancellationToken ct = default)");
            impl.AppendLine("    {");
            impl.AppendLine("        // TODO: implement transaction-scope semantics if multi-statement atomicity is required.");
            impl.AppendLine("        return System.Threading.Tasks.Task.FromResult(0);");
            impl.AppendLine("    }");
            impl.AppendLine("}");

            files.Add(new EmittedFile(
                CleanLayout.DbContextPath(project, "DapperUnitOfWork"),
                impl.ToString()));
        }

        return files;
    }
}
