using System.Collections.Generic;
using Artect.Config;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits <c>Dockerfile</c> and <c>docker-compose.yml</c> at the scaffold root.
/// Only runs when <c>cfg.IncludeDockerAssets == true</c>.
/// </summary>
public sealed class DockerEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        if (!ctx.Config.IncludeDockerAssets)
            return System.Array.Empty<EmittedFile>();

        var cfg     = ctx.Config;
        var project = cfg.ProjectName;
        var tag     = cfg.TargetFramework.DockerTag();
        var apiName = CleanLayout.ApiProjectName(project);
        var infraName = CleanLayout.InfrastructureProjectName(project);

        return new[]
        {
            new EmittedFile("Dockerfile",           BuildDockerfile(project, apiName, tag)),
            new EmittedFile("docker-compose.yml",   BuildCompose(project)),
        };
    }

    // ── Dockerfile ─────────────────────────────────────────────────────────

    static string BuildDockerfile(string project, string apiName, string tag) => $"""
        FROM mcr.microsoft.com/dotnet/sdk:{tag} AS build
        WORKDIR /src
        COPY . .
        RUN dotnet publish src/{apiName}/{apiName}.csproj -c Release -o /app

        FROM mcr.microsoft.com/dotnet/aspnet:{tag}
        WORKDIR /app
        COPY --from=build /app .
        ENTRYPOINT ["dotnet", "{apiName}.dll"]
        """;

    // ── docker-compose.yml ─────────────────────────────────────────────────

    static string BuildCompose(string project) => $"""
        services:
          api:
            build: .
            ports:
              - "5080:8080"
            environment:
              - ConnectionStrings__DefaultConnection=Server=db;Database={project};User Id=sa;Password=YourStrong!Pa55;TrustServerCertificate=True
            depends_on:
              db:
                condition: service_healthy

          db:
            image: mcr.microsoft.com/mssql/server:2022-latest
            environment:
              - ACCEPT_EULA=Y
              - MSSQL_SA_PASSWORD=YourStrong!Pa55
            ports:
              - "14336:1433"
            volumes:
              - sqldata:/var/opt/mssql
            healthcheck:
              test: ["CMD", "/opt/mssql-tools18/bin/sqlcmd", "-C", "-S", "localhost", "-U", "sa", "-P", "YourStrong!Pa55", "-Q", "select 1"]
              interval: 10s
              timeout: 5s
              retries: 10

        volumes:
          sqldata:
        """;
}
