using System.Collections.Generic;
using System.Text;
using Artect.Config;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits five .csproj files — one per Clean Architecture project (Api, Application, Domain, Infrastructure, Shared).
/// Uses StringBuilder for the conditional PackageReference elements.
/// </summary>
public sealed class CsProjEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var cfg     = ctx.Config;
        var project = cfg.ProjectName;
        var tfm     = cfg.TargetFramework.ToMoniker();

        return new[]
        {
            EmitApi(project, tfm, cfg),
            EmitApplication(project, tfm, cfg),
            EmitDomain(project, tfm),
            EmitInfrastructure(project, tfm, cfg),
            EmitShared(project, tfm),
        };
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    static string SharedProps(string tfm, string rootAndAssembly) => $"""
          <TargetFramework>{tfm}</TargetFramework>
          <Nullable>enable</Nullable>
          <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
          <WarningsAsErrors />
          <ImplicitUsings>enable</ImplicitUsings>
          <LangVersion>latest</LangVersion>
          <RootNamespace>{rootAndAssembly}</RootNamespace>
          <AssemblyName>{rootAndAssembly}</AssemblyName>
          <NoWarn>$(NoWarn);CS1591</NoWarn>
        """;

    static string PackageRef(string id, string version) =>
        $"    <PackageReference Include=\"{id}\" Version=\"{version}\" />";

    static string ProjectRef(string relativePath) =>
        $"    <ProjectReference Include=\"{relativePath}\" />";

    // ── Api ───────────────────────────────────────────────────────────────────

    static EmittedFile EmitApi(string project, string tfm, ArtectConfig cfg)
    {
        var major = cfg.TargetFramework.MajorVersion();
        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk.Web\">");
        sb.AppendLine();
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine($"    <OutputType>Exe</OutputType>");
        sb.AppendLine(SharedProps(tfm, $"{project}.Api"));
        sb.AppendLine($"    <InvariantGlobalization>false</InvariantGlobalization>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine();
        sb.AppendLine("  <ItemGroup>");
        sb.AppendLine(PackageRef("Microsoft.AspNetCore.OpenApi", $"{major}.0.*"));
        sb.AppendLine(PackageRef("Scalar.AspNetCore", "2.*"));

        if (cfg.Auth == AuthKind.JwtBearer)
            sb.AppendLine(PackageRef("Microsoft.AspNetCore.Authentication.JwtBearer", $"{major}.0.*"));
        else if (cfg.Auth == AuthKind.AzureAd)
            sb.AppendLine(PackageRef("Microsoft.Identity.Web", "3.*"));

        if (cfg.ApiVersioning != ApiVersioningKind.None)
            sb.AppendLine(PackageRef("Asp.Versioning.Http", "8.*"));

        // V#18: secure-by-default production middleware. Health-checks EFCore is needed
        // for the readiness probe; OpenTelemetry packages enable traces, metrics, logs
        // with the OTLP exporter. Versions floated to 1.* / latest minor — these are the
        // shipped-stable lines as of the OTel 1.10 wave.
        if (cfg.DataAccess == DataAccessKind.EfCore)
            sb.AppendLine(PackageRef("Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore", $"{major}.0.*"));
        sb.AppendLine(PackageRef("OpenTelemetry.Extensions.Hosting", "1.*"));
        sb.AppendLine(PackageRef("OpenTelemetry.Instrumentation.AspNetCore", "1.*"));
        sb.AppendLine(PackageRef("OpenTelemetry.Instrumentation.Http", "1.*"));
        sb.AppendLine(PackageRef("OpenTelemetry.Instrumentation.Runtime", "1.*"));
        sb.AppendLine(PackageRef("OpenTelemetry.Exporter.OpenTelemetryProtocol", "1.*"));

        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine();
        sb.AppendLine("  <ItemGroup>");
        sb.AppendLine(ProjectRef($"../{CleanLayout.ApplicationProjectName(project)}/{CleanLayout.ApplicationProjectName(project)}.csproj"));
        sb.AppendLine(ProjectRef($"../{CleanLayout.InfrastructureProjectName(project)}/{CleanLayout.InfrastructureProjectName(project)}.csproj"));
        sb.AppendLine(ProjectRef($"../{CleanLayout.SharedProjectName(project)}/{CleanLayout.SharedProjectName(project)}.csproj"));
        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine();
        sb.Append("</Project>");

        var name = CleanLayout.ApiProjectName(project);
        var path = $"{CleanLayout.ApiDir(project)}/{name}.csproj";
        return new EmittedFile(path, sb.ToString());
    }

    // ── Application ──────────────────────────────────────────────────────────

    static EmittedFile EmitApplication(string project, string tfm, ArtectConfig cfg)
    {
        var major = cfg.TargetFramework.MajorVersion();
        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        sb.AppendLine();
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine(SharedProps(tfm, $"{project}.Application"));
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine();
        sb.AppendLine("  <ItemGroup>");
        // Needed for IServiceCollection + AddScoped/AddSingleton/AddTransient extensions
        // used by the emitted ApplicationServiceCollectionExtensions installer.
        sb.AppendLine(PackageRef("Microsoft.Extensions.DependencyInjection.Abstractions", $"{major}.0.*"));
        // Needed for IAppLogger<T> adapter + LoggingBehavior decorator (ILogger types).
        sb.AppendLine(PackageRef("Microsoft.Extensions.Logging.Abstractions", $"{major}.0.*"));
        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine();
        sb.AppendLine("  <ItemGroup>");
        sb.AppendLine(ProjectRef($"../{CleanLayout.DomainProjectName(project)}/{CleanLayout.DomainProjectName(project)}.csproj"));
        // V#5: Application references Shared so the Patch handler can consume Optional<T>
        // from Shared.Common. The dependency is bounded to that single type — Application
        // does not consume Requests/Responses/Errors from Shared.
        sb.AppendLine(ProjectRef($"../{CleanLayout.SharedProjectName(project)}/{CleanLayout.SharedProjectName(project)}.csproj"));
        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine();
        sb.Append("</Project>");

        var name = CleanLayout.ApplicationProjectName(project);
        var path = $"{CleanLayout.ApplicationDir(project)}/{name}.csproj";
        return new EmittedFile(path, sb.ToString());
    }

    // ── Domain ────────────────────────────────────────────────────────────────

    static EmittedFile EmitDomain(string project, string tfm)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        sb.AppendLine();
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine(SharedProps(tfm, $"{project}.Domain"));
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine();
        sb.Append("</Project>");

        var name = CleanLayout.DomainProjectName(project);
        var path = $"{CleanLayout.DomainDir(project)}/{name}.csproj";
        return new EmittedFile(path, sb.ToString());
    }

    // ── Infrastructure ────────────────────────────────────────────────────────

    static EmittedFile EmitInfrastructure(string project, string tfm, ArtectConfig cfg)
    {
        var major = cfg.TargetFramework.MajorVersion();
        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        sb.AppendLine();
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine(SharedProps(tfm, $"{project}.Infrastructure"));
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine();
        sb.AppendLine("  <ItemGroup>");

        if (cfg.DataAccess == DataAccessKind.EfCore)
        {
            sb.AppendLine(PackageRef("Microsoft.EntityFrameworkCore.SqlServer", $"{major}.0.*"));
            sb.AppendLine(PackageRef("Microsoft.Extensions.Configuration.Abstractions", $"{major}.0.*"));
            sb.AppendLine(PackageRef("Microsoft.Extensions.DependencyInjection.Abstractions", $"{major}.0.*"));
        }
        else // Dapper
        {
            sb.AppendLine(PackageRef("Dapper", "2.*"));
            sb.AppendLine(PackageRef("Microsoft.Data.SqlClient", "5.*"));
        }

        // V#13: outbox dispatcher is a BackgroundService — needs Hosting.Abstractions for
        // IHostedService and Logging.Abstractions for ILogger<T>. Both abstractions only;
        // the concrete implementations come in transitively from the Api host.
        if (cfg.EnableDomainEvents)
        {
            sb.AppendLine(PackageRef("Microsoft.Extensions.Hosting.Abstractions", $"{major}.0.*"));
            sb.AppendLine(PackageRef("Microsoft.Extensions.Logging.Abstractions", $"{major}.0.*"));
        }

        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine();
        sb.AppendLine("  <ItemGroup>");
        sb.AppendLine(ProjectRef($"../{CleanLayout.ApplicationProjectName(project)}/{CleanLayout.ApplicationProjectName(project)}.csproj"));
        sb.AppendLine(ProjectRef($"../{CleanLayout.DomainProjectName(project)}/{CleanLayout.DomainProjectName(project)}.csproj"));
        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine();
        sb.Append("</Project>");

        var name = CleanLayout.InfrastructureProjectName(project);
        var path = $"{CleanLayout.InfrastructureDir(project)}/{name}.csproj";
        return new EmittedFile(path, sb.ToString());
    }

    // ── Shared ────────────────────────────────────────────────────────────────

    static EmittedFile EmitShared(string project, string tfm)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        sb.AppendLine();
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine(SharedProps(tfm, $"{project}.Shared"));
        sb.AppendLine($"    <IsPackable>true</IsPackable>");
        sb.AppendLine($"    <PackageId>{project}.Shared</PackageId>");
        sb.AppendLine($"    <Version>1.0.0</Version>");
        sb.AppendLine($"    <Description>Wire contracts for the {project} API.</Description>");
        sb.AppendLine($"    <!-- <PackageProjectUrl></PackageProjectUrl> -->");
        sb.AppendLine($"    <!-- <PackageLicenseExpression>MIT</PackageLicenseExpression> -->");
        sb.AppendLine($"    <!-- <RepositoryUrl></RepositoryUrl> -->");
        sb.AppendLine($"    <!-- <Authors></Authors> -->");
        sb.AppendLine($"    <!-- <Company></Company> -->");
        sb.AppendLine($"    <GeneratePackageOnBuild>false</GeneratePackageOnBuild>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine();
        sb.Append("</Project>");

        var name = CleanLayout.SharedProjectName(project);
        var path = $"{CleanLayout.SharedDir(project)}/{name}.csproj";
        return new EmittedFile(path, sb.ToString());
    }
}
