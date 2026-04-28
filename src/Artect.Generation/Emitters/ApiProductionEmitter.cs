using System.Collections.Generic;
using System.Text;
using Artect.Config;

namespace Artect.Generation.Emitters;

/// <summary>
/// V#18: emits secure-by-default production-middleware files into
/// <c>Api/Configuration/</c>. All six concerns are on by default with
/// configuration-driven opt-out / tuning so the generated solution boots
/// production-ready without any manual wiring:
///
/// <list type="bullet">
/// <item><c>SecurityHeadersMiddleware</c> — HSTS, X-Content-Type-Options, X-Frame-Options, Content-Security-Policy, Referrer-Policy, Permissions-Policy.</item>
/// <item><c>CorsServiceCollectionExtensions</c> — default-deny CORS policy reading allowed origins from <c>Cors:AllowedOrigins</c>.</item>
/// <item><c>RateLimitingServiceCollectionExtensions</c> — fixed-window per-IP rate limiter reading <c>RateLimiting:*</c>.</item>
/// <item><c>HealthCheckExtensions</c> — split <c>/health/live</c> + <c>/health/ready</c>; readiness pings the EF Core DbContext when applicable.</item>
/// <item><c>OpenTelemetryServiceCollectionExtensions</c> — traces, metrics, and logs with OTLP exporter when <c>OpenTelemetry:OtlpEndpoint</c> is configured.</item>
/// <item><c>SensitiveDataLoggingInterceptor</c> — <c>IHttpLoggingInterceptor</c> that strips Authorization / Cookie / Set-Cookie / X-Api-Key headers from request and response logs.</item>
/// </list>
/// </summary>
public sealed class ApiProductionEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var project = ctx.Config.ProjectName;
        var ns = $"{project}.Api.Configuration";
        var dir = $"{CleanLayout.ApiDir(project)}/Configuration";
        var efEnabled = ctx.Config.DataAccess == DataAccessKind.EfCore;
        var dbCtxNs = $"{CleanLayout.InfrastructureNamespace(project)}.Data";
        var dbCtxName = $"{project}DbContext";

        return new[]
        {
            new EmittedFile($"{dir}/SecurityHeadersMiddleware.cs", BuildSecurityHeaders(ns)),
            new EmittedFile($"{dir}/CorsServiceCollectionExtensions.cs", BuildCors(ns)),
            new EmittedFile($"{dir}/RateLimitingServiceCollectionExtensions.cs", BuildRateLimiting(ns)),
            new EmittedFile($"{dir}/HealthCheckExtensions.cs", BuildHealthChecks(ns, efEnabled, dbCtxNs, dbCtxName)),
            new EmittedFile($"{dir}/OpenTelemetryServiceCollectionExtensions.cs", BuildOpenTelemetry(ns, project)),
            new EmittedFile($"{dir}/SensitiveDataLoggingInterceptor.cs", BuildSensitiveDataInterceptor(ns)),
        };
    }

    static string BuildSecurityHeaders(string ns)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.AspNetCore.Builder;");
        sb.AppendLine("using Microsoft.AspNetCore.Http;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("public static class SecurityHeadersMiddlewareExtensions");
        sb.AppendLine("{");
        sb.AppendLine("    static readonly string[] DocsPathPrefixes = { \"/scalar\", \"/openapi\" };");
        sb.AppendLine();
        sb.AppendLine("    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>");
        sb.AppendLine("        app.Use(async (context, next) =>");
        sb.AppendLine("        {");
        sb.AppendLine("            context.Response.OnStarting(() =>");
        sb.AppendLine("            {");
        sb.AppendLine("                var headers = context.Response.Headers;");
        sb.AppendLine("                var path = context.Request.Path.Value ?? string.Empty;");
        sb.AppendLine("                var isDocsRequest = false;");
        sb.AppendLine("                foreach (var prefix in DocsPathPrefixes)");
        sb.AppendLine("                {");
        sb.AppendLine("                    if (path.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))");
        sb.AppendLine("                    {");
        sb.AppendLine("                        isDocsRequest = true;");
        sb.AppendLine("                        break;");
        sb.AppendLine("                    }");
        sb.AppendLine("                }");
        sb.AppendLine();
        sb.AppendLine("                headers[\"X-Content-Type-Options\"] = \"nosniff\";");
        sb.AppendLine("                headers[\"Referrer-Policy\"] = \"strict-origin-when-cross-origin\";");
        sb.AppendLine("                headers[\"Permissions-Policy\"] = \"geolocation=(), microphone=(), camera=()\";");
        sb.AppendLine("                if (context.Request.IsHttps)");
        sb.AppendLine("                    headers[\"Strict-Transport-Security\"] = \"max-age=31536000; includeSubDomains\";");
        sb.AppendLine();
        sb.AppendLine("                if (!isDocsRequest)");
        sb.AppendLine("                {");
        sb.AppendLine("                    headers[\"X-Frame-Options\"] = \"DENY\";");
        sb.AppendLine("                    if (!headers.ContainsKey(\"Content-Security-Policy\"))");
        sb.AppendLine("                        headers[\"Content-Security-Policy\"] = \"default-src 'self'; frame-ancestors 'none'; base-uri 'self'\";");
        sb.AppendLine("                }");
        sb.AppendLine("                return System.Threading.Tasks.Task.CompletedTask;");
        sb.AppendLine("            });");
        sb.AppendLine("            await next();");
        sb.AppendLine("        });");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string BuildCors(string ns)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.AspNetCore.Cors.Infrastructure;");
        sb.AppendLine("using Microsoft.Extensions.Configuration;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("public static class CorsConfiguration");
        sb.AppendLine("{");
        sb.AppendLine("    public const string PolicyName = \"Default\";");
        sb.AppendLine();
        sb.AppendLine("    public static IServiceCollection AddDefaultCorsPolicy(this IServiceCollection services, IConfiguration configuration)");
        sb.AppendLine("    {");
        sb.AppendLine("        var origins = configuration.GetSection(\"Cors:AllowedOrigins\").Get<string[]>() ?? System.Array.Empty<string>();");
        sb.AppendLine("        services.AddCors(options =>");
        sb.AppendLine("        {");
        sb.AppendLine("            options.AddPolicy(PolicyName, policy =>");
        sb.AppendLine("            {");
        sb.AppendLine("                if (origins.Length == 0)");
        sb.AppendLine("                {");
        sb.AppendLine("                    policy.WithOrigins(System.Array.Empty<string>())");
        sb.AppendLine("                          .DisallowCredentials();");
        sb.AppendLine("                }");
        sb.AppendLine("                else");
        sb.AppendLine("                {");
        sb.AppendLine("                    policy.WithOrigins(origins)");
        sb.AppendLine("                          .AllowAnyHeader()");
        sb.AppendLine("                          .AllowAnyMethod()");
        sb.AppendLine("                          .AllowCredentials();");
        sb.AppendLine("                }");
        sb.AppendLine("            });");
        sb.AppendLine("        });");
        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string BuildRateLimiting(string ns)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading.RateLimiting;");
        sb.AppendLine("using Microsoft.AspNetCore.Http;");
        sb.AppendLine("using Microsoft.AspNetCore.RateLimiting;");
        sb.AppendLine("using Microsoft.Extensions.Configuration;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("public static class RateLimitingServiceCollectionExtensions");
        sb.AppendLine("{");
        sb.AppendLine("    public static IServiceCollection AddDefaultRateLimiting(this IServiceCollection services, IConfiguration configuration)");
        sb.AppendLine("    {");
        sb.AppendLine("        var enabled = configuration.GetValue(\"RateLimiting:Enabled\", true);");
        sb.AppendLine("        var permitLimit = configuration.GetValue(\"RateLimiting:PermitLimit\", 100);");
        sb.AppendLine("        var windowSeconds = configuration.GetValue(\"RateLimiting:WindowSeconds\", 60);");
        sb.AppendLine();
        sb.AppendLine("        services.AddRateLimiter(options =>");
        sb.AppendLine("        {");
        sb.AppendLine("            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;");
        sb.AppendLine("            if (!enabled) return;");
        sb.AppendLine();
        sb.AppendLine("            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>");
        sb.AppendLine("            {");
        sb.AppendLine("                var key = http.User.Identity?.IsAuthenticated == true");
        sb.AppendLine("                    ? \"u:\" + (http.User.Identity!.Name ?? \"anon\")");
        sb.AppendLine("                    : \"ip:\" + (http.Connection.RemoteIpAddress?.ToString() ?? \"unknown\");");
        sb.AppendLine("                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions");
        sb.AppendLine("                {");
        sb.AppendLine("                    PermitLimit = permitLimit,");
        sb.AppendLine("                    Window = TimeSpan.FromSeconds(windowSeconds),");
        sb.AppendLine("                    QueueLimit = 0,");
        sb.AppendLine("                    AutoReplenishment = true,");
        sb.AppendLine("                });");
        sb.AppendLine("            });");
        sb.AppendLine("        });");
        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string BuildHealthChecks(string ns, bool efEnabled, string dbCtxNs, string dbCtxName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using Microsoft.AspNetCore.Builder;");
        sb.AppendLine("using Microsoft.AspNetCore.Diagnostics.HealthChecks;");
        sb.AppendLine("using Microsoft.AspNetCore.Routing;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        if (efEnabled)
            sb.AppendLine($"using {dbCtxNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("public static class HealthCheckExtensions");
        sb.AppendLine("{");
        sb.AppendLine("    public static IServiceCollection AddDefaultHealthChecks(this IServiceCollection services)");
        sb.AppendLine("    {");
        sb.AppendLine("        var builder = services.AddHealthChecks();");
        if (efEnabled)
        {
            sb.AppendLine($"        builder.AddDbContextCheck<{dbCtxName}>(name: \"db\", tags: new[] {{ \"ready\" }});");
        }
        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public static IEndpointRouteBuilder MapDefaultHealthEndpoints(this IEndpointRouteBuilder app)");
        sb.AppendLine("    {");
        sb.AppendLine("        app.MapHealthChecks(\"/health\");");
        sb.AppendLine("        app.MapHealthChecks(\"/health/live\", new HealthCheckOptions");
        sb.AppendLine("        {");
        sb.AppendLine("            Predicate = _ => false,");
        sb.AppendLine("        });");
        sb.AppendLine("        app.MapHealthChecks(\"/health/ready\", new HealthCheckOptions");
        sb.AppendLine("        {");
        sb.AppendLine("            Predicate = check => check.Tags.Contains(\"ready\"),");
        sb.AppendLine("        });");
        sb.AppendLine("        return app;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string BuildOpenTelemetry(string ns, string project)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.Extensions.Configuration;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using Microsoft.Extensions.Hosting;");
        sb.AppendLine("using Microsoft.Extensions.Logging;");
        sb.AppendLine("using OpenTelemetry.Logs;");
        sb.AppendLine("using OpenTelemetry.Metrics;");
        sb.AppendLine("using OpenTelemetry.Resources;");
        sb.AppendLine("using OpenTelemetry.Trace;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("public static class OpenTelemetryServiceCollectionExtensions");
        sb.AppendLine("{");
        sb.AppendLine($"    const string DefaultServiceName = \"{project}.Api\";");
        sb.AppendLine();
        sb.AppendLine("    public static IServiceCollection AddDefaultOpenTelemetry(this IServiceCollection services, IConfiguration configuration, IHostEnvironment env)");
        sb.AppendLine("    {");
        sb.AppendLine("        var otlpEndpoint = configuration[\"OpenTelemetry:OtlpEndpoint\"];");
        sb.AppendLine("        var configuredServiceName = configuration[\"OpenTelemetry:ServiceName\"];");
        sb.AppendLine("        var serviceName = string.IsNullOrWhiteSpace(configuredServiceName) ? DefaultServiceName : configuredServiceName;");
        sb.AppendLine();
        sb.AppendLine("        var resourceBuilder = ResourceBuilder.CreateDefault()");
        sb.AppendLine("            .AddService(serviceName, serviceVersion: typeof(OpenTelemetryServiceCollectionExtensions).Assembly.GetName().Version?.ToString())");
        sb.AppendLine("            .AddAttributes(new[] { new System.Collections.Generic.KeyValuePair<string, object>(\"deployment.environment\", env.EnvironmentName) });");
        sb.AppendLine();
        sb.AppendLine("        services.AddOpenTelemetry()");
        sb.AppendLine("            .ConfigureResource(r => r.AddService(serviceName))");
        sb.AppendLine("            .WithTracing(t =>");
        sb.AppendLine("            {");
        sb.AppendLine("                t.SetResourceBuilder(resourceBuilder);");
        sb.AppendLine("                t.AddAspNetCoreInstrumentation();");
        sb.AppendLine("                t.AddHttpClientInstrumentation();");
        sb.AppendLine("                if (!string.IsNullOrWhiteSpace(otlpEndpoint))");
        sb.AppendLine("                    t.AddOtlpExporter(o => o.Endpoint = new System.Uri(otlpEndpoint!));");
        sb.AppendLine("            })");
        sb.AppendLine("            .WithMetrics(m =>");
        sb.AppendLine("            {");
        sb.AppendLine("                m.SetResourceBuilder(resourceBuilder);");
        sb.AppendLine("                m.AddAspNetCoreInstrumentation();");
        sb.AppendLine("                m.AddHttpClientInstrumentation();");
        sb.AppendLine("                m.AddRuntimeInstrumentation();");
        sb.AppendLine("                if (!string.IsNullOrWhiteSpace(otlpEndpoint))");
        sb.AppendLine("                    m.AddOtlpExporter(o => o.Endpoint = new System.Uri(otlpEndpoint!));");
        sb.AppendLine("            });");
        sb.AppendLine();
        sb.AppendLine("        services.AddLogging(builder =>");
        sb.AppendLine("        {");
        sb.AppendLine("            builder.AddOpenTelemetry(o =>");
        sb.AppendLine("            {");
        sb.AppendLine("                o.SetResourceBuilder(resourceBuilder);");
        sb.AppendLine("                o.IncludeFormattedMessage = true;");
        sb.AppendLine("                o.IncludeScopes = true;");
        sb.AppendLine("                o.ParseStateValues = true;");
        sb.AppendLine("                if (!string.IsNullOrWhiteSpace(otlpEndpoint))");
        sb.AppendLine("                    o.AddOtlpExporter(e => e.Endpoint = new System.Uri(otlpEndpoint!));");
        sb.AppendLine("            });");
        sb.AppendLine("        });");
        sb.AppendLine();
        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string BuildSensitiveDataInterceptor(string ns)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Microsoft.AspNetCore.HttpLogging;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("public sealed class SensitiveDataLoggingInterceptor : IHttpLoggingInterceptor");
        sb.AppendLine("{");
        sb.AppendLine("    static readonly string[] RedactedHeaders =");
        sb.AppendLine("    {");
        sb.AppendLine("        \"Authorization\",");
        sb.AppendLine("        \"Cookie\",");
        sb.AppendLine("        \"Set-Cookie\",");
        sb.AppendLine("        \"X-Api-Key\",");
        sb.AppendLine("        \"Proxy-Authorization\",");
        sb.AppendLine("    };");
        sb.AppendLine();
        sb.AppendLine("    public ValueTask OnRequestAsync(HttpLoggingInterceptorContext logContext)");
        sb.AppendLine("    {");
        sb.AppendLine("        Redact(logContext);");
        sb.AppendLine("        return ValueTask.CompletedTask;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public ValueTask OnResponseAsync(HttpLoggingInterceptorContext logContext)");
        sb.AppendLine("    {");
        sb.AppendLine("        Redact(logContext);");
        sb.AppendLine("        return ValueTask.CompletedTask;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    static void Redact(HttpLoggingInterceptorContext ctx)");
        sb.AppendLine("    {");
        sb.AppendLine("        foreach (var header in RedactedHeaders)");
        sb.AppendLine("            ctx.AddParameter(header, \"[REDACTED]\");");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
