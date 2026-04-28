using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;

namespace Artect.Generation.Emitters;

public sealed class ApplicationDiEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var project = ctx.Config.ProjectName;
        var crud    = ctx.Config.Crud;
        var anyWrite = (crud & (CrudOperation.Post | CrudOperation.Put | CrudOperation.Patch | CrudOperation.Delete)) != 0;

        var ns = CleanLayout.ApplicationNamespace(project);
        var entities = ctx.Model.Entities
            .Where(e => !e.ShouldSkip(EntityClassification.AggregateRoot))
            .OrderBy(e => e.EntityTypeName, System.StringComparer.Ordinal)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine($"using {CleanLayout.ApplicationAbstractionsNamespace(project)};");
        if (anyWrite)
        {
            foreach (var entity in entities)
                sb.AppendLine($"using {CleanLayout.ApplicationFeatureNamespace(project, entity.EntityTypeName)};");
        }
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("public static class DependencyInjection");
        sb.AppendLine("{");
        sb.AppendLine("    public static IServiceCollection AddApplication(this IServiceCollection services)");
        sb.AppendLine("    {");

        if (anyWrite)
        {
            foreach (var entity in entities)
            {
                var name = entity.EntityTypeName;
                if ((crud & CrudOperation.Post) != 0)
                    sb.AppendLine($"        services.AddScoped<Create{name}Handler>();");
                if ((crud & CrudOperation.Put) != 0)
                    sb.AppendLine($"        services.AddScoped<Update{name}Handler>();");
                if ((crud & CrudOperation.Patch) != 0)
                    sb.AppendLine($"        services.AddScoped<Patch{name}Handler>();");
                if ((crud & CrudOperation.Delete) != 0)
                    sb.AppendLine($"        services.AddScoped<Delete{name}Handler>();");
            }
        }

        sb.AppendLine();
        sb.AppendLine("        // V#6: scan this assembly for ICommandHandler<,> / IQueryHandler<,>");
        sb.AppendLine("        // implementations and register each one against its interface(s). Picks up");
        sb.AppendLine("        // generated CRUD handlers (Create/Update/Patch — they implement");
        sb.AppendLine("        // ICommandHandler<>) AND any hand-written use cases the user adds in");
        sb.AppendLine("        // Application/UseCases/. No manual DI wiring needed for new use cases.");
        sb.AppendLine("        var assembly = typeof(DependencyInjection).Assembly;");
        sb.AppendLine("        foreach (var type in assembly.GetTypes())");
        sb.AppendLine("        {");
        sb.AppendLine("            if (type.IsAbstract || type.IsInterface) continue;");
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
