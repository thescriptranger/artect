using System.Collections.Generic;
using System.Text;
using Artect.Config;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits the IUnitOfWork abstraction into Application/Abstractions/ and the
/// EF Core implementation into Infrastructure/Data/. Repositories stage changes;
/// handlers call <c>IUnitOfWork.CommitAsync</c> exactly once per use case.
/// </summary>
public sealed class UnitOfWorkEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var project = ctx.Config.ProjectName;
        var absNs   = CleanLayout.ApplicationAbstractionsNamespace(project);

        var iface = new StringBuilder();
        iface.AppendLine($"namespace {absNs};");
        iface.AppendLine();
        iface.AppendLine("public interface IUnitOfWork");
        iface.AppendLine("{");
        iface.AppendLine("    Task<int> CommitAsync(CancellationToken ct);");
        iface.AppendLine("}");

        var files = new List<EmittedFile>
        {
            new EmittedFile(
                CleanLayout.ApplicationAbstractionsPath(project, "IUnitOfWork"),
                iface.ToString()),
        };

        if (ctx.Config.DataAccess == DataAccessKind.EfCore)
        {
            var infraDataNs = $"{CleanLayout.InfrastructureNamespace(project)}.Data";
            var dbCtx       = $"{project}DbContext";

            var impl = new StringBuilder();
            impl.AppendLine($"using {absNs};");
            impl.AppendLine();
            impl.AppendLine($"namespace {infraDataNs};");
            impl.AppendLine();
            impl.AppendLine($"public sealed class EfUnitOfWork({dbCtx} db) : IUnitOfWork");
            impl.AppendLine("{");
            impl.AppendLine("    public Task<int> CommitAsync(CancellationToken ct) => db.SaveChangesAsync(ct);");
            impl.AppendLine("}");

            files.Add(new EmittedFile(
                $"{CleanLayout.InfrastructureDir(project)}/Data/EfUnitOfWork.cs",
                impl.ToString()));
        }

        return files;
    }
}
