using System.Collections.Generic;
using System.Text;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits <c>UseCaseResultExtensions.cs</c> into <c>src/&lt;Project&gt;.Api/</c>.
/// </summary>
public sealed class UseCaseResultExtensionsEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var project   = ctx.Config.ProjectName;
        var apiNs     = CleanLayout.ApiNamespace(project);
        var ucNs      = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";
        var errNs     = $"{CleanLayout.SharedNamespace(project)}.Errors";

        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.AspNetCore.Http;");
        sb.AppendLine("using Microsoft.AspNetCore.Http.HttpResults;");
        sb.AppendLine($"using {errNs};");
        sb.AppendLine($"using {ucNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {apiNs};");
        sb.AppendLine();
        sb.AppendLine("public static class UseCaseResultExtensions");
        sb.AppendLine("{");
        sb.AppendLine("    public static IResult ToIResult(this UseCaseResult result) => result switch");
        sb.AppendLine("    {");
        sb.AppendLine("        UseCaseResult.Success => TypedResults.NoContent(),");
        sb.AppendLine("        UseCaseResult.NotFound nf => TypedResults.NotFound(new ApiProblem(\"Not found\", 404, $\"{nf.EntityType} '{nf.Id}' not found.\", null)),");
        sb.AppendLine("        UseCaseResult.ValidationFailed vf => TypedResults.BadRequest(new ApiProblem(\"Validation failed\", 400, null, vf.Errors)),");
        sb.AppendLine("        UseCaseResult.Conflict c => TypedResults.Conflict(new ApiProblem(\"Conflict\", 409, c.Message, null)),");
        sb.AppendLine("        _ => TypedResults.Problem(statusCode: 500),");
        sb.AppendLine("    };");
        sb.AppendLine();
        sb.AppendLine("    public static IResult ToIResult<T>(this UseCaseResult<T> result) => result switch");
        sb.AppendLine("    {");
        sb.AppendLine("        UseCaseResult<T>.Success s => TypedResults.Ok(s.Value),");
        sb.AppendLine("        UseCaseResult<T>.NotFound nf => TypedResults.NotFound(new ApiProblem(\"Not found\", 404, $\"{nf.EntityType} '{nf.Id}' not found.\", null)),");
        sb.AppendLine("        UseCaseResult<T>.ValidationFailed vf => TypedResults.BadRequest(new ApiProblem(\"Validation failed\", 400, null, vf.Errors)),");
        sb.AppendLine("        UseCaseResult<T>.Conflict c => TypedResults.Conflict(new ApiProblem(\"Conflict\", 409, c.Message, null)),");
        sb.AppendLine("        _ => TypedResults.Problem(statusCode: 500),");
        sb.AppendLine("    };");
        sb.AppendLine();
        sb.AppendLine("    public static IResult ToCreatedResult<T>(this UseCaseResult<T> result, string location) => result switch");
        sb.AppendLine("    {");
        sb.AppendLine("        UseCaseResult<T>.Success s => TypedResults.Created(location, s.Value),");
        sb.AppendLine("        _ => result.ToIResult(),");
        sb.AppendLine("    };");
        sb.AppendLine("}");

        var path = CleanLayout.UseCaseResultExtensionsPath(project);
        return new[] { new EmittedFile(path, sb.ToString()) };
    }
}
