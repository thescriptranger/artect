using System.Collections.Generic;
using System.Text;
using Artect.Config;

namespace Artect.Generation.Emitters;

/// <summary>
/// V#9: emits cross-cutting auth scaffolding into the Api project when
/// <c>cfg.Auth != None</c>:
/// <list type="bullet">
/// <item>An <c>OpenApi/SecuritySchemeTransformer.cs</c> document transformer that
///   advertises the configured scheme (Bearer for JWT/Auth0/AzureAd; ApiKey for
///   ApiKey) on the generated OpenAPI document. Wired into <c>AddOpenApi(...)</c>
///   by <see cref="ProgramCsEmitter"/>.</item>
/// <item>An <c>Auth/ApiKeyAuthenticationHandler.cs</c> stub when
///   <c>cfg.Auth == ApiKey</c> — referenced by the AuthApiKey template that
///   ProgramCsEmitter injects into <c>Program.cs</c>.</item>
/// </list>
/// </summary>
public sealed class ApiAuthEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var auth = ctx.Config.Auth;
        if (auth == AuthKind.None) return System.Array.Empty<EmittedFile>();

        var project = ctx.Config.ProjectName;
        var apiNs = $"{project}.Api";
        var list = new List<EmittedFile>
        {
            new($"{CleanLayout.ApiDir(project)}/OpenApi/SecuritySchemeTransformer.cs",
                BuildSecuritySchemeTransformer($"{apiNs}.OpenApi", auth)),
        };

        if (auth == AuthKind.ApiKey)
        {
            list.Add(new EmittedFile(
                $"{CleanLayout.ApiDir(project)}/Auth/ApiKeyAuthenticationHandler.cs",
                BuildApiKeyHandler($"{apiNs}.Auth")));
        }

        return list;
    }

    static string BuildSecuritySchemeTransformer(string ns, AuthKind auth)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Microsoft.AspNetCore.OpenApi;");
        sb.AppendLine("using Microsoft.OpenApi.Models;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// V#9 acceptance #3: advertises the configured authentication scheme on the");
        sb.AppendLine("/// generated OpenAPI document so consumers (Scalar, Swagger UI, code generators)");
        sb.AppendLine("/// see the security expectations.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public sealed class SecuritySchemeTransformer : IOpenApiDocumentTransformer");
        sb.AppendLine("{");
        sb.AppendLine("    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)");
        sb.AppendLine("    {");
        sb.AppendLine("        document.Components ??= new OpenApiComponents();");
        sb.AppendLine("        document.Components.SecuritySchemes ??= new System.Collections.Generic.Dictionary<string, OpenApiSecurityScheme>();");
        if (auth == AuthKind.ApiKey)
        {
            sb.AppendLine("        document.Components.SecuritySchemes[\"apiKey\"] = new OpenApiSecurityScheme");
            sb.AppendLine("        {");
            sb.AppendLine("            Type = SecuritySchemeType.ApiKey,");
            sb.AppendLine("            In = ParameterLocation.Header,");
            sb.AppendLine("            Name = \"X-Api-Key\",");
            sb.AppendLine("            Description = \"API key sent via the X-Api-Key request header.\",");
            sb.AppendLine("        };");
            sb.AppendLine("        var requirement = new OpenApiSecurityRequirement");
            sb.AppendLine("        {");
            sb.AppendLine("            [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = \"apiKey\" } }] = new System.Collections.Generic.List<string>(),");
            sb.AppendLine("        };");
        }
        else
        {
            // JWT, Auth0, AzureAd — all use Bearer.
            sb.AppendLine("        document.Components.SecuritySchemes[\"bearerAuth\"] = new OpenApiSecurityScheme");
            sb.AppendLine("        {");
            sb.AppendLine("            Type = SecuritySchemeType.Http,");
            sb.AppendLine("            Scheme = \"bearer\",");
            sb.AppendLine("            BearerFormat = \"JWT\",");
            sb.AppendLine("            Description = \"Bearer token issued by the configured identity provider.\",");
            sb.AppendLine("        };");
            sb.AppendLine("        var requirement = new OpenApiSecurityRequirement");
            sb.AppendLine("        {");
            sb.AppendLine("            [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = \"bearerAuth\" } }] = new System.Collections.Generic.List<string>(),");
            sb.AppendLine("        };");
        }
        sb.AppendLine("        document.SecurityRequirements ??= new System.Collections.Generic.List<OpenApiSecurityRequirement>();");
        sb.AppendLine("        document.SecurityRequirements.Add(requirement);");
        sb.AppendLine("        return Task.CompletedTask;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string BuildApiKeyHandler(string ns)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System.Security.Claims;");
        sb.AppendLine("using System.Text.Encodings.Web;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Microsoft.AspNetCore.Authentication;");
        sb.AppendLine("using Microsoft.Extensions.Configuration;");
        sb.AppendLine("using Microsoft.Extensions.Logging;");
        sb.AppendLine("using Microsoft.Extensions.Options;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// V#9 ApiKey scheme handler. Reads the X-Api-Key header and compares against");
        sb.AppendLine("/// <c>Auth:ApiKey</c> in configuration. Replace with a real implementation that");
        sb.AppendLine("/// looks up the key in a secret store / DB before going to production.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public sealed class ApiKeyAuthenticationHandler(");
        sb.AppendLine("    IOptionsMonitor<AuthenticationSchemeOptions> options,");
        sb.AppendLine("    ILoggerFactory logger,");
        sb.AppendLine("    UrlEncoder encoder,");
        sb.AppendLine("    IConfiguration configuration) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)");
        sb.AppendLine("{");
        sb.AppendLine("    const string HeaderName = \"X-Api-Key\";");
        sb.AppendLine();
        sb.AppendLine("    protected override Task<AuthenticateResult> HandleAuthenticateAsync()");
        sb.AppendLine("    {");
        sb.AppendLine("        if (!Request.Headers.TryGetValue(HeaderName, out var headerValues) || headerValues.Count == 0)");
        sb.AppendLine("            return Task.FromResult(AuthenticateResult.NoResult());");
        sb.AppendLine();
        sb.AppendLine("        var supplied = headerValues[0];");
        sb.AppendLine("        var expected = configuration[\"Auth:ApiKey\"];");
        sb.AppendLine("        if (string.IsNullOrEmpty(expected))");
        sb.AppendLine("            return Task.FromResult(AuthenticateResult.Fail(\"Missing configuration key 'Auth:ApiKey'.\"));");
        sb.AppendLine("        if (!string.Equals(supplied, expected, System.StringComparison.Ordinal))");
        sb.AppendLine("            return Task.FromResult(AuthenticateResult.Fail(\"Invalid API key.\"));");
        sb.AppendLine();
        sb.AppendLine("        var claims = new[] { new Claim(ClaimTypes.Name, \"api-key-client\") };");
        sb.AppendLine("        var identity = new ClaimsIdentity(claims, Scheme.Name);");
        sb.AppendLine("        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);");
        sb.AppendLine("        return Task.FromResult(AuthenticateResult.Success(ticket));");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
