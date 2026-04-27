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
        var entities = ctx.Model.Entities.Where(e => !e.IsJoinTable && e.HasPrimaryKey)
            .OrderBy(e => e.EntityTypeName, System.StringComparer.Ordinal)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
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

        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return new[] { new EmittedFile($"{CleanLayout.ApplicationDir(project)}/DependencyInjection.cs", sb.ToString()) };
    }
}
