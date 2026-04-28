using System.Collections.Generic;
using System.Text;
using Artect.Config;

namespace Artect.Generation.Emitters;

public sealed class ApiDiEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var project = ctx.Config.ProjectName;
        var appValidNs = CleanLayout.ApplicationValidatorsNamespace(project);
        var registerValidationFilter = (ctx.Config.Crud
            & (CrudOperation.Post | CrudOperation.Put | CrudOperation.Patch)) != 0;

        var sb = new StringBuilder();
        sb.AppendLine($"using {appValidNs};");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        if (registerValidationFilter)
            sb.AppendLine($"using {project}.Api.Filters;");
        sb.AppendLine();
        sb.AppendLine($"namespace {project}.Api;");
        sb.AppendLine();
        sb.AppendLine("public static class DependencyInjection");
        sb.AppendLine("{");
        sb.AppendLine("    public static IServiceCollection AddApi(this IServiceCollection services)");
        sb.AppendLine("    {");
        sb.AppendLine("        var validatorType = typeof(IValidator<>);");
        sb.AppendLine("        var assembly = typeof(DependencyInjection).Assembly;");
        sb.AppendLine();
        sb.AppendLine("        foreach (var type in assembly.GetTypes())");
        sb.AppendLine("        {");
        sb.AppendLine("            if (type.IsAbstract || type.IsInterface) continue;");
        sb.AppendLine("            foreach (var iface in type.GetInterfaces())");
        sb.AppendLine("            {");
        sb.AppendLine("                if (!iface.IsGenericType || iface.GetGenericTypeDefinition() != validatorType) continue;");
        sb.AppendLine("                services.AddScoped(iface, type);");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        if (registerValidationFilter)
        {
            sb.AppendLine();
            sb.AppendLine("        // V#8: register the generic ValidationFilter so endpoints can wire it via");
            sb.AppendLine("        // .AddEndpointFilter<ValidationFilter<TRequest>>(). The open-generic");
            sb.AppendLine("        // registration covers every closed TRequest used in routes.");
            sb.AppendLine("        services.AddTransient(typeof(ValidationFilter<>));");
        }
        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return new[] { new EmittedFile($"{CleanLayout.ApiDir(project)}/DependencyInjection.cs", sb.ToString()) };
    }
}
