using System.Collections.Generic;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits <c>appsettings.json</c> and <c>appsettings.Development.json</c>
/// into the Api project directory.
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
              "AllowedHosts": "*"
            }
            """;

        var dev = """
            {
              "Logging": {
                "LogLevel": {
                  "Default": "Debug",
                  "Microsoft.AspNetCore": "Information"
                }
              }
            }
            """;

        return new[]
        {
            new EmittedFile(CleanLayout.AppSettingsPath(project), prod),
            new EmittedFile(AppSettingsDevPath(project), dev),
        };
    }

    static string AppSettingsDevPath(string project) =>
        $"{CleanLayout.ApiDir(project)}/appsettings.Development.json";
}
