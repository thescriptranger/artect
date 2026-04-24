using System.Collections.Generic;
using System.Text;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits <c>Program.cs</c> into <c>src/&lt;Project&gt;.Api/</c>.
/// IT Director shape: three AddX() calls, MapScalarApiReference() in Development,
/// app.MapApiEndpoints() via EndpointRegistration, await app.RunAsync().
/// Auth and versioning are not emitted here; users add them when needed.
/// </summary>
public sealed class ProgramCsEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var project = ctx.Config.ProjectName;
        var path    = CleanLayout.ProgramCsPath(project);
        var content = Build(project);
        return new[] { new EmittedFile(path, content) };
    }

    static string Build(string project)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"using {project}.Api;");
        sb.AppendLine($"using {project}.Application;");
        sb.AppendLine($"using {project}.Infrastructure;");
        sb.AppendLine("using Scalar.AspNetCore;");
        sb.AppendLine();
        sb.AppendLine("var builder = WebApplication.CreateBuilder(args);");
        sb.AppendLine();
        sb.AppendLine("builder.Services.AddOpenApi();");
        sb.AppendLine("builder.Services.AddApi();");
        sb.AppendLine("builder.Services.AddApplication();");
        sb.AppendLine("builder.Services.AddInfrastructure(builder.Configuration);");
        sb.AppendLine();
        sb.AppendLine("var app = builder.Build();");
        sb.AppendLine();
        sb.AppendLine("if (app.Environment.IsDevelopment())");
        sb.AppendLine("{");
        sb.AppendLine("    app.MapOpenApi();");
        sb.AppendLine("    app.MapScalarApiReference();");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("app.UseHttpsRedirection();");
        sb.AppendLine("app.MapApiEndpoints();");
        sb.AppendLine();
        sb.AppendLine("await app.RunAsync();");
        sb.AppendLine();
        sb.AppendLine("public static partial class Program { }");
        return sb.ToString();
    }
}
