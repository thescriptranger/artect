using System.Collections.Generic;
using System.Text;

namespace Artect.Generation.Emitters;

/// <summary>
/// V#15: emits the Application DI installer as a single assembly scan rather than
/// a per-entity AddScoped block. Generated handlers (Create/Update/Patch/Delete)
/// are picked up by their class-name suffix and registered as themselves; the
/// same iteration also binds them against every <c>ICommandHandler&lt;,&gt;</c> /
/// <c>IQueryHandler&lt;,&gt;</c> they implement, so cross-cutting decorators can wrap
/// generated and hand-written use cases uniformly. Adding an entity adds zero
/// lines to this file.
/// </summary>
public sealed class ApplicationDiEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var project = ctx.Config.ProjectName;
        var ns = CleanLayout.ApplicationNamespace(project);
        var absNs = CleanLayout.ApplicationAbstractionsNamespace(project);

        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine($"using {absNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("public static class DependencyInjection");
        sb.AppendLine("{");
        sb.AppendLine("    public static IServiceCollection AddApplication(this IServiceCollection services)");
        sb.AppendLine("    {");
        sb.AppendLine("        // V#15: assembly-scan registration for all generated and hand-written use cases.");
        sb.AppendLine("        // 1. Concrete *Handler classes register as themselves so endpoints can inject");
        sb.AppendLine("        //    the concrete handler (e.g., CreateCustomerHandler).");
        sb.AppendLine("        // 2. The same types also register against every ICommandHandler<,> / IQueryHandler<,>");
        sb.AppendLine("        //    interface they implement, so future decorators (validation, transaction,");
        sb.AppendLine("        //    audit, authorization) can wrap generated and hand-written handlers uniformly.");
        sb.AppendLine("        var assembly = typeof(DependencyInjection).Assembly;");
        sb.AppendLine("        foreach (var type in assembly.GetTypes())");
        sb.AppendLine("        {");
        sb.AppendLine("            if (type.IsAbstract || type.IsInterface) continue;");
        sb.AppendLine();
        sb.AppendLine("            if (type.Name.EndsWith(\"Handler\", System.StringComparison.Ordinal))");
        sb.AppendLine("                services.AddScoped(type);");
        sb.AppendLine();
        sb.AppendLine("            foreach (var iface in type.GetInterfaces())");
        sb.AppendLine("            {");
        sb.AppendLine("                if (!iface.IsGenericType) continue;");
        sb.AppendLine("                var def = iface.GetGenericTypeDefinition();");
        sb.AppendLine("                if (def == typeof(ICommandHandler<,>) || def == typeof(IQueryHandler<,>))");
        sb.AppendLine("                    services.AddScoped(iface, type);");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return new[] { new EmittedFile($"{CleanLayout.ApplicationDir(project)}/DependencyInjection.cs", sb.ToString()) };
    }
}
