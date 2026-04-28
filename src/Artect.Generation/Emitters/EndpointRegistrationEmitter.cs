using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Naming;

namespace Artect.Generation.Emitters;

public sealed class EndpointRegistrationEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var project = ctx.Config.ProjectName;
        var corrections = ctx.NamingCorrections;
        var versioningEnabled = ctx.Config.ApiVersioning != ApiVersioningKind.None;

        var entities = ctx.Model.Entities
            .Where(e => !e.ShouldSkip(EntityClassification.AggregateRoot))
            .OrderBy(e => e.EntityTypeName, System.StringComparer.Ordinal)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"using {project}.Api.Endpoints;");
        sb.AppendLine("using Microsoft.AspNetCore.Builder;");
        sb.AppendLine("using Microsoft.AspNetCore.Routing;");
        if (versioningEnabled)
            sb.AppendLine("using Asp.Versioning.Builder;");
        sb.AppendLine();
        sb.AppendLine($"namespace {project}.Api;");
        sb.AppendLine();
        sb.AppendLine("public static class EndpointRegistration");
        sb.AppendLine("{");
        if (versioningEnabled)
        {
            sb.AppendLine("    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app, ApiVersionSet versionSet)");
            sb.AppendLine("    {");
            foreach (var entity in entities)
            {
                var plural = CasingHelper.ToPascalCase(Pluralizer.Pluralize(entity.EntityTypeName), corrections);
                sb.AppendLine($"        app.Map{plural}Endpoints(versionSet);");
            }
        }
        else
        {
            sb.AppendLine("    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app)");
            sb.AppendLine("    {");
            foreach (var entity in entities)
            {
                var plural = CasingHelper.ToPascalCase(Pluralizer.Pluralize(entity.EntityTypeName), corrections);
                sb.AppendLine($"        app.Map{plural}Endpoints();");
            }
        }
        sb.AppendLine("        return app;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return new[] { new EmittedFile($"{CleanLayout.ApiDir(project)}/EndpointRegistration.cs", sb.ToString()) };
    }
}
