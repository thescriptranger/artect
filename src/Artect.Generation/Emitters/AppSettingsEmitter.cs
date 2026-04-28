using System.Collections.Generic;
using System.Text.Json;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits <c>appsettings.json</c> (committed; production reads secrets from env vars)
/// and <c>appsettings.Development.json</c> (gitignored; local dev convenience) into the
/// Api project directory.
///
/// When a connection string was supplied to Artect at generate-time (via <c>--connection</c>
/// flag or <c>ARTECT_CONNECTION</c> env var), it is embedded into
/// <c>appsettings.Development.json</c> so the scaffold can run locally without a manual
/// copy-paste step. <c>appsettings.json</c> keeps the empty placeholder — production
/// secrets belong in environment variables, user-secrets, or a vault, never a committed file.
/// </summary>
public sealed class AppSettingsEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var project = ctx.Config.ProjectName;

        var prod = """
            {
              "ConnectionStrings": {
                "DefaultConnection": ""
              },
              "Logging": {
                "LogLevel": {
                  "Default": "Information",
                  "Microsoft.AspNetCore": "Warning"
                }
              },
              "AllowedHosts": "*",
              "Cors": {
                "AllowedOrigins": []
              },
              "RateLimiting": {
                "Enabled": true,
                "PermitLimit": 100,
                "WindowSeconds": 60
              },
              "OpenTelemetry": {
                "ServiceName": "",
                "OtlpEndpoint": ""
              }
            }
            """;

        var dev = BuildDevSettings(ctx.ConnectionString);

        return new[]
        {
            new EmittedFile(CleanLayout.AppSettingsPath(project), prod),
            new EmittedFile(AppSettingsDevPath(project), dev),
        };
    }

    static string BuildDevSettings(string? connectionString)
    {
        // Always include the Logging block; include ConnectionStrings only when we have a
        // non-empty string to embed (keeps the file minimal otherwise).
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return """
                {
                  "Logging": {
                    "LogLevel": {
                      "Default": "Debug",
                      "Microsoft.AspNetCore": "Information"
                    }
                  }
                }
                """;
        }

        // JsonSerializer.Serialize handles escaping of quotes, backslashes, control chars etc.
        var encoded = JsonSerializer.Serialize(connectionString);
        return $$"""
            {
              "ConnectionStrings": {
                "DefaultConnection": {{encoded}}
              },
              "Logging": {
                "LogLevel": {
                  "Default": "Debug",
                  "Microsoft.AspNetCore": "Information"
                }
              }
            }
            """;
    }

    static string AppSettingsDevPath(string project) =>
        $"{CleanLayout.ApiDir(project)}/appsettings.Development.json";
}
