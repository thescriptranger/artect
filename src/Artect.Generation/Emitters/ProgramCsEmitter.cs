using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits <c>Program.cs</c> into <c>src/&lt;Project&gt;.Api/</c>.
/// Uses StringBuilder rather than the template DSL because the number of
/// conditionals (auth, versioning, per-entity endpoint mapping) would
/// make a Handlebars template unreadable.
/// Auth and versioning bodies are loaded as raw fragment templates so they
/// remain editable without recompiling the tool.
/// DI registrations have been extracted into per-layer installer extensions
/// emitted by <see cref="ServiceInstallerEmitter"/>; Program.cs now delegates
/// to <c>AddApplicationServices()</c> and <c>AddInfrastructureServices()</c>.
/// </summary>
public sealed class ProgramCsEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var project = ctx.Config.ProjectName;
        var content = Build(ctx, project);
        var path    = CleanLayout.ProgramCsPath(project);
        return new[] { new EmittedFile(path, content) };
    }

    static string Build(EmitterContext ctx, string project)
    {
        var cfg  = ctx.Config;
        var model = ctx.Model;
        var auth  = cfg.Auth;
        var ver   = cfg.ApiVersioning;

        var endpointNs = $"{CleanLayout.ApiNamespace(project)}.Endpoints";

        // ── Usings (sorted) ───────────────────────────────────────────────────
        var usings = new SortedSet<string>(System.StringComparer.Ordinal);
        usings.Add("Scalar.AspNetCore");
        usings.Add(endpointNs);

        var sb = new StringBuilder();

        foreach (var u in usings)
            sb.AppendLine($"using {u};");
        sb.AppendLine();

        // ── Builder setup ─────────────────────────────────────────────────────
        sb.AppendLine("var builder = WebApplication.CreateBuilder(args);");
        sb.AppendLine();

        // OpenAPI
        sb.AppendLine("builder.Services.AddOpenApi();");
        sb.AppendLine();

        // ── DI — delegate to layer-owned installer extensions ─────────────────
        sb.AppendLine("builder.Services.AddApplicationServices();");
        sb.AppendLine("builder.Services.AddInfrastructureServices(builder.Configuration);");

        // ── Auth fragment ─────────────────────────────────────────────────────
        if (auth != AuthKind.None)
        {
            sb.AppendLine();
            var fragmentName = auth switch
            {
                AuthKind.JwtBearer => "AuthJwt.cs.artect",
                AuthKind.Auth0     => "AuthAuth0.cs.artect",
                AuthKind.AzureAd   => "AuthAzureAd.cs.artect",
                AuthKind.ApiKey    => "AuthApiKey.cs.artect",
                _                  => null,
            };
            if (fragmentName is not null)
            {
                var fragment = ctx.Templates.Load(fragmentName);
                sb.Append(fragment);
            }
        }

        // ── Versioning fragment ───────────────────────────────────────────────
        if (ver != ApiVersioningKind.None)
        {
            sb.AppendLine();
            var fragmentName = ver switch
            {
                ApiVersioningKind.UrlSegment  => "VersioningUrlSegment.cs.artect",
                ApiVersioningKind.Header      => "VersioningHeader.cs.artect",
                ApiVersioningKind.QueryString => "VersioningQueryString.cs.artect",
                _                             => null,
            };
            if (fragmentName is not null)
            {
                var fragment = ctx.Templates.Load(fragmentName);
                sb.Append(fragment);
            }
        }

        // ── Build the app ─────────────────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine("var app = builder.Build();");
        sb.AppendLine();

        // ── Middleware pipeline ───────────────────────────────────────────────
        sb.AppendLine("if (app.Environment.IsDevelopment())");
        sb.AppendLine("{");
        sb.AppendLine("    app.MapOpenApi();");
        sb.AppendLine("    app.MapScalarApiReference();");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("app.UseHttpsRedirection();");

        if (auth != AuthKind.None)
        {
            sb.AppendLine("app.UseAuthentication();");
            sb.AppendLine("app.UseAuthorization();");
        }

        sb.AppendLine();

        // ── Endpoint mapping (alpha-sorted) ───────────────────────────────────
        var allEntityPlurals = model.Entities
            .Where(e => !e.IsJoinTable)
            .Select(e => e.DbSetPropertyName)
            .OrderBy(p => p, System.StringComparer.Ordinal);

        var viewPlurals = ctx.Graph.Views
            .Select(v => CasingHelper.ToPascalCase(Pluralizer.Pluralize(Pluralizer.Singularize(v.Name)), ctx.NamingCorrections))
            .OrderBy(p => p, System.StringComparer.Ordinal);

        foreach (var plural in allEntityPlurals.Concat(viewPlurals).OrderBy(p => p, System.StringComparer.Ordinal))
            sb.AppendLine($"app.Map{plural}Endpoints();");

        sb.AppendLine();
        sb.AppendLine("app.Run();");
        sb.AppendLine();
        sb.AppendLine("// Required for WebApplicationFactory<Program> in integration tests.");
        sb.AppendLine("public partial class Program { }");

        return sb.ToString();
    }
}
