using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;

namespace Artect.Generation.Emitters;

public sealed class InfrastructureDiEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        if (ctx.Config.DataAccess != DataAccessKind.EfCore) return System.Array.Empty<EmittedFile>();

        var project = ctx.Config.ProjectName;
        var dbCtx   = $"{project}DbContext";
        var dataNs  = $"{CleanLayout.InfrastructureNamespace(project)}.Data";
        var infraNs = CleanLayout.InfrastructureNamespace(project);
        var appAbsNs = CleanLayout.ApplicationAbstractionsNamespace(project);

        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        sb.AppendLine("using Microsoft.Extensions.Configuration;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine($"using {dataNs};");
        sb.AppendLine($"using {appAbsNs};");

        var entities = ctx.Model.Entities.Where(e => !e.IsJoinTable && e.HasPrimaryKey).ToList();
        foreach (var entity in entities.OrderBy(e => e.EntityTypeName, System.StringComparer.Ordinal))
        {
            sb.AppendLine($"using {CleanLayout.InfrastructureDataEntityNamespace(project, entity.EntityTypeName)};");
            sb.AppendLine($"using {CleanLayout.ApplicationFeatureAbstractionsNamespace(project, entity.EntityTypeName)};");
        }

        sb.AppendLine();
        sb.AppendLine($"namespace {infraNs};");
        sb.AppendLine();
        sb.AppendLine("public static class DependencyInjection");
        sb.AppendLine("{");
        sb.AppendLine("    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)");
        sb.AppendLine("    {");
        sb.AppendLine("        var connectionString = configuration.GetConnectionString(\"DefaultConnection\");");
        sb.AppendLine("        if (string.IsNullOrWhiteSpace(connectionString))");
        sb.AppendLine("            throw new InvalidOperationException(");
        sb.AppendLine("                \"Missing connection string 'DefaultConnection'. Set it in appsettings.json, \" +");
        sb.AppendLine("                \"appsettings.Development.json, or the ConnectionStrings__DefaultConnection environment variable.\");");
        sb.AppendLine();
        sb.AppendLine($"        services.AddDbContext<{dbCtx}>(options => options.UseSqlServer(connectionString));");
        sb.AppendLine("        services.AddScoped<IUnitOfWork, EfUnitOfWork>();");
        sb.AppendLine();

        foreach (var entity in entities.OrderBy(e => e.EntityTypeName, System.StringComparer.Ordinal))
        {
            var name = entity.EntityTypeName;
            sb.AppendLine($"        services.AddScoped<I{name}Repository, {name}Repository>();");
            sb.AppendLine($"        services.AddScoped<I{name}ReadService, {name}ReadService>();");
        }

        sb.AppendLine();
        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return new[] { new EmittedFile($"{CleanLayout.InfrastructureDir(project)}/DependencyInjection.cs", sb.ToString()) };
    }
}
