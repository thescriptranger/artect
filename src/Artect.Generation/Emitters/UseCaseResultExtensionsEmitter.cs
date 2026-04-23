using System.Collections.Generic;
using System.Text;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits <c>UseCaseResultExtensions.cs</c> into <c>src/&lt;Project&gt;.Api/</c>.
/// Includes three overloads:
/// <list type="bullet">
///   <item><c>ToIResult&lt;T&gt;</c> — non-mapping (T is returned directly as Ok body)</item>
///   <item><c>ToIResult&lt;TModel, TResponse&gt;(Func&lt;TModel, TResponse&gt; map)</c> — maps model to response DTO</item>
///   <item><c>ToIResult(this UseCaseResult&lt;Unit&gt;)</c> — Unit-specific; returns NoContent on success</item>
/// </list>
/// All overloads convert <c>ApplicationError</c> → <c>ValidationError</c> via <c>.ToWire()</c> from
/// <c>ApplicationErrorMappers</c> in the Api.Mapping namespace.
/// </summary>
public sealed class UseCaseResultExtensionsEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var project    = ctx.Config.ProjectName;
        var apiNs      = CleanLayout.ApiNamespace(project);
        var ucNs       = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";
        var errNs      = $"{CleanLayout.SharedNamespace(project)}.Errors";
        var appErrNs   = CleanLayout.ApplicationErrorsNamespace(project);
        var mapNs      = CleanLayout.ApiMappingNamespace(project);
        var commonNs   = CleanLayout.ApplicationCommonNamespace(project);

        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using Microsoft.AspNetCore.Http;");
        sb.AppendLine("using Microsoft.AspNetCore.Http.HttpResults;");
        sb.AppendLine($"using {appErrNs};");
        sb.AppendLine($"using {commonNs};");
        sb.AppendLine($"using {errNs};");
        sb.AppendLine($"using {mapNs};");
        sb.AppendLine($"using {ucNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {apiNs};");
        sb.AppendLine();
        sb.AppendLine("public static class UseCaseResultExtensions");
        sb.AppendLine("{");

        // --- non-generic overload for UseCaseResult<Unit> (returns NoContent on success) ---
        sb.AppendLine("    public static IResult ToIResult(this UseCaseResult<Unit> result) => result switch");
        sb.AppendLine("    {");
        sb.AppendLine("        UseCaseResult<Unit>.Success => TypedResults.NoContent(),");
        sb.AppendLine("        UseCaseResult<Unit>.NotFound nf => TypedResults.NotFound(new ApiProblem(\"Not found\", 404, $\"{nf.EntityType} '{nf.Id}' not found.\", null)),");
        sb.AppendLine("        UseCaseResult<Unit>.ValidationFailed vf => TypedResults.BadRequest(new ApiProblem(\"Validation failed\", 400, null, vf.Errors.ToWire())),");
        sb.AppendLine("        UseCaseResult<Unit>.Conflict c => TypedResults.Conflict(new ApiProblem(\"Conflict\", 409, c.Message, null)),");
        sb.AppendLine("        _ => TypedResults.Problem(statusCode: 500),");
        sb.AppendLine("    };");
        sb.AppendLine();

        // --- generic non-mapping overload (T returned directly as Ok body) ---
        sb.AppendLine("    public static IResult ToIResult<T>(this UseCaseResult<T> result) => result switch");
        sb.AppendLine("    {");
        sb.AppendLine("        UseCaseResult<T>.Success s => TypedResults.Ok(s.Value),");
        sb.AppendLine("        UseCaseResult<T>.NotFound nf => TypedResults.NotFound(new ApiProblem(\"Not found\", 404, $\"{nf.EntityType} '{nf.Id}' not found.\", null)),");
        sb.AppendLine("        UseCaseResult<T>.ValidationFailed vf => TypedResults.BadRequest(new ApiProblem(\"Validation failed\", 400, null, vf.Errors.ToWire())),");
        sb.AppendLine("        UseCaseResult<T>.Conflict c => TypedResults.Conflict(new ApiProblem(\"Conflict\", 409, c.Message, null)),");
        sb.AppendLine("        _ => TypedResults.Problem(statusCode: 500),");
        sb.AppendLine("    };");
        sb.AppendLine();

        // --- primary mapping overload (maps TModel → TResponse via provided delegate) ---
        sb.AppendLine("    public static IResult ToIResult<TModel, TResponse>(this UseCaseResult<TModel> result, Func<TModel, TResponse> map) => result switch");
        sb.AppendLine("    {");
        sb.AppendLine("        UseCaseResult<TModel>.Success s => TypedResults.Ok(map(s.Value)),");
        sb.AppendLine("        UseCaseResult<TModel>.NotFound nf => TypedResults.NotFound(new ApiProblem(\"Not found\", 404, $\"{nf.EntityType} '{nf.Id}' not found.\", null)),");
        sb.AppendLine("        UseCaseResult<TModel>.ValidationFailed vf => TypedResults.BadRequest(new ApiProblem(\"Validation failed\", 400, null, vf.Errors.ToWire())),");
        sb.AppendLine("        UseCaseResult<TModel>.Conflict c => TypedResults.Conflict(new ApiProblem(\"Conflict\", 409, c.Message, null)),");
        sb.AppendLine("        _ => TypedResults.Problem(statusCode: 500),");
        sb.AppendLine("    };");
        sb.AppendLine();

        // --- ToCreatedResult convenience ---
        sb.AppendLine("    public static IResult ToCreatedResult<TModel, TResponse>(this UseCaseResult<TModel> result, string location, Func<TModel, TResponse> map) => result switch");
        sb.AppendLine("    {");
        sb.AppendLine("        UseCaseResult<TModel>.Success s => TypedResults.Created(location, map(s.Value)),");
        sb.AppendLine("        _ => result.ToIResult(map),");
        sb.AppendLine("    };");
        sb.AppendLine("}");

        var path = CleanLayout.UseCaseResultExtensionsPath(project);
        return new[] { new EmittedFile(path, sb.ToString()) };
    }
}
