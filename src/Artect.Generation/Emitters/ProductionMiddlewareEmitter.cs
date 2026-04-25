using System.Collections.Generic;
using System.Text;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits the framework-agnostic production-middleware files into Api/Middleware/:
/// a global exception handler that maps unhandled exceptions to ProblemDetails, and
/// a correlation-ID middleware that propagates X-Correlation-ID through the
/// HttpContext.TraceIdentifier. Other production concerns (CORS, auth, OTel,
/// structured logging, rate limiting) are deliberately deferred to JD Framework.
/// </summary>
public sealed class ProductionMiddlewareEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var project = ctx.Config.ProjectName;
        var ns = $"{project}.Api.Middleware";
        var dir = $"{CleanLayout.ApiDir(project)}/Middleware";

        return new[]
        {
            new EmittedFile($"{dir}/GlobalExceptionHandler.cs", BuildGlobalExceptionHandler(ns)),
            new EmittedFile($"{dir}/CorrelationIdMiddleware.cs", BuildCorrelationIdMiddleware(ns)),
        };
    }

    static string BuildGlobalExceptionHandler(string ns)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.AspNetCore.Diagnostics;");
        sb.AppendLine("using Microsoft.AspNetCore.Http;");
        sb.AppendLine("using Microsoft.AspNetCore.Mvc;");
        sb.AppendLine("using Microsoft.Extensions.Logging;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler");
        sb.AppendLine("{");
        sb.AppendLine("    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)");
        sb.AppendLine("    {");
        sb.AppendLine("        logger.LogError(exception, \"Unhandled exception for {TraceId}\", httpContext.TraceIdentifier);");
        sb.AppendLine();
        sb.AppendLine("        var problem = new ProblemDetails");
        sb.AppendLine("        {");
        sb.AppendLine("            Status = StatusCodes.Status500InternalServerError,");
        sb.AppendLine("            Title  = \"An unexpected error occurred.\",");
        sb.AppendLine("            Type   = \"https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.1\",");
        sb.AppendLine("            Detail = httpContext.RequestServices.GetService<IHostEnvironment>()?.IsDevelopment() == true");
        sb.AppendLine("                ? exception.Message");
        sb.AppendLine("                : null,");
        sb.AppendLine("            Instance = httpContext.Request.Path,");
        sb.AppendLine("        };");
        sb.AppendLine("        problem.Extensions[\"traceId\"] = httpContext.TraceIdentifier;");
        sb.AppendLine();
        sb.AppendLine("        httpContext.Response.StatusCode = problem.Status.Value;");
        sb.AppendLine("        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken).ConfigureAwait(false);");
        sb.AppendLine("        return true;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string BuildCorrelationIdMiddleware(string ns)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.AspNetCore.Http;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("public sealed class CorrelationIdMiddleware(RequestDelegate next)");
        sb.AppendLine("{");
        sb.AppendLine("    const string HeaderName = \"X-Correlation-ID\";");
        sb.AppendLine();
        sb.AppendLine("    public async Task InvokeAsync(HttpContext context)");
        sb.AppendLine("    {");
        sb.AppendLine("        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var values) && values.Count > 0");
        sb.AppendLine("            ? values[0]!");
        sb.AppendLine("            : Guid.NewGuid().ToString(\"N\");");
        sb.AppendLine();
        sb.AppendLine("        context.TraceIdentifier = correlationId;");
        sb.AppendLine("        context.Response.OnStarting(() =>");
        sb.AppendLine("        {");
        sb.AppendLine("            context.Response.Headers[HeaderName] = correlationId;");
        sb.AppendLine("            return Task.CompletedTask;");
        sb.AppendLine("        });");
        sb.AppendLine();
        sb.AppendLine("        await next(context).ConfigureAwait(false);");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
