using System.Collections.Generic;
using System.Text;
using Artect.Config;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits <c>Program.cs</c> into <c>src/&lt;Project&gt;.Api/</c>.
///
/// V#9: when <c>cfg.Auth != None</c>, this emitter
/// <list type="bullet">
/// <item>Loads the matching auth template (AuthJwt / AuthAuth0 / AuthAzureAd /
///   AuthApiKey) and inlines it into the service-registration block. The
///   templates already call <c>builder.Services.AddAuthentication(...)</c> +
///   <c>AddAuthorization()</c>.</item>
/// <item>Emits <c>app.UseAuthentication()</c> + <c>app.UseAuthorization()</c>
///   in the middleware pipeline before <c>MapApiEndpoints()</c>.</item>
/// <item>Wires <c>SecuritySchemeTransformer</c> into <c>AddOpenApi(...)</c> so
///   the generated OpenAPI document advertises the auth scheme.</item>
/// </list>
///
/// Endpoint groups carry their own <c>RequireAuthorization()</c> call (set by
/// <see cref="MinimalApiEndpointEmitter"/>). The combination of
/// <c>UseAuthentication</c> middleware + per-group <c>RequireAuthorization</c>
/// satisfies V#9 acceptance #1 + #2.
/// </summary>
public sealed class ProgramCsEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var path = CleanLayout.ProgramCsPath(ctx.Config.ProjectName);
        return new[] { new EmittedFile(path, Build(ctx)) };
    }

    static string Build(EmitterContext ctx)
    {
        var project = ctx.Config.ProjectName;
        var auth = ctx.Config.Auth;
        var authEnabled = auth != AuthKind.None;
        var authTemplateContent = authEnabled ? LoadAuthTemplate(ctx, auth) : null;

        var versioning = ctx.Config.ApiVersioning;
        var versioningEnabled = versioning != ApiVersioningKind.None;
        var versioningTemplateContent = versioningEnabled ? LoadVersioningTemplate(ctx, versioning) : null;

        var sb = new StringBuilder();
        sb.AppendLine($"using {project}.Api;");
        sb.AppendLine($"using {project}.Api.Middleware;");
        if (authEnabled)
            sb.AppendLine($"using {project}.Api.OpenApi;");
        if (auth == AuthKind.ApiKey)
            sb.AppendLine($"using {project}.Api.Auth;");
        sb.AppendLine($"using {project}.Application;");
        sb.AppendLine($"using {project}.Infrastructure;");
        sb.AppendLine("using Scalar.AspNetCore;");
        sb.AppendLine();
        sb.AppendLine("var builder = WebApplication.CreateBuilder(args);");
        sb.AppendLine();
        if (authEnabled)
        {
            sb.AppendLine("builder.Services.AddOpenApi(options =>");
            sb.AppendLine("{");
            sb.AppendLine("    options.AddDocumentTransformer<SecuritySchemeTransformer>();");
            sb.AppendLine("});");
        }
        else
        {
            sb.AppendLine("builder.Services.AddOpenApi();");
        }
        sb.AppendLine("builder.Services.AddProblemDetails();");
        sb.AppendLine("builder.Services.AddExceptionHandler<GlobalExceptionHandler>();");
        sb.AppendLine("builder.Services.AddHealthChecks();");
        if (authEnabled)
        {
            sb.AppendLine();
            sb.AppendLine("// V#9: authentication + authorization wiring (from auth template).");
            sb.Append(authTemplateContent);
            if (!authTemplateContent!.EndsWith('\n'))
                sb.AppendLine();
            sb.AppendLine();
        }
        if (versioningEnabled)
        {
            sb.AppendLine();
            sb.AppendLine("// V#10: API versioning service registration (from versioning template).");
            sb.Append(versioningTemplateContent);
            if (!versioningTemplateContent!.EndsWith('\n'))
                sb.AppendLine();
            sb.AppendLine();
        }
        sb.AppendLine("builder.Services.AddApi();");
        sb.AppendLine("builder.Services.AddApplication();");
        sb.AppendLine("builder.Services.AddInfrastructure(builder.Configuration);");
        sb.AppendLine();
        sb.AppendLine("var app = builder.Build();");
        sb.AppendLine();
        sb.AppendLine("app.UseExceptionHandler();");
        sb.AppendLine("app.UseMiddleware<CorrelationIdMiddleware>();");
        sb.AppendLine();
        sb.AppendLine("if (app.Environment.IsDevelopment())");
        sb.AppendLine("{");
        sb.AppendLine("    app.MapOpenApi();");
        sb.AppendLine("    app.MapScalarApiReference();");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("app.UseHttpsRedirection();");
        if (authEnabled)
        {
            // V#9 acceptance #1: UseAuthentication MUST come before UseAuthorization.
            sb.AppendLine("app.UseAuthentication();");
            sb.AppendLine("app.UseAuthorization();");
        }
        sb.AppendLine("app.MapHealthChecks(\"/health\");");
        if (versioningEnabled)
        {
            sb.AppendLine();
            sb.AppendLine("// V#10: shared ApiVersionSet — generated endpoints register against it. Add new");
            sb.AppendLine("// versions here (.HasApiVersion(new ApiVersion(2, 0)) etc.) when you ship V2.");
            sb.AppendLine("var apiVersionSet = app.NewApiVersionSet()");
            sb.AppendLine("    .HasApiVersion(new Asp.Versioning.ApiVersion(1, 0))");
            sb.AppendLine("    .ReportApiVersions()");
            sb.AppendLine("    .Build();");
            sb.AppendLine();
            sb.AppendLine("app.MapApiEndpoints(apiVersionSet);");
        }
        else
        {
            sb.AppendLine("app.MapApiEndpoints();");
        }
        sb.AppendLine();
        sb.AppendLine("await app.RunAsync();");
        sb.AppendLine();
        sb.AppendLine("public partial class Program { }");
        return sb.ToString();
    }

    static string LoadAuthTemplate(EmitterContext ctx, AuthKind auth) => auth switch
    {
        AuthKind.JwtBearer => ctx.Templates.Load("AuthJwt.cs.artect"),
        AuthKind.Auth0     => ctx.Templates.Load("AuthAuth0.cs.artect"),
        AuthKind.AzureAd   => ctx.Templates.Load("AuthAzureAd.cs.artect"),
        AuthKind.ApiKey    => ctx.Templates.Load("AuthApiKey.cs.artect"),
        _                  => string.Empty,
    };

    static string LoadVersioningTemplate(EmitterContext ctx, ApiVersioningKind kind) => kind switch
    {
        ApiVersioningKind.Header      => ctx.Templates.Load("VersioningHeader.cs.artect"),
        ApiVersioningKind.QueryString => ctx.Templates.Load("VersioningQueryString.cs.artect"),
        ApiVersioningKind.UrlSegment  => ctx.Templates.Load("VersioningUrlSegment.cs.artect"),
        _                             => string.Empty,
    };
}
