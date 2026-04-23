using System.Collections.Generic;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits <c>Properties/launchSettings.json</c> into the Api project.
/// HTTP port 5080, HTTPS port 5443, DOTNET_ENVIRONMENT=Development.
/// Scalar UI is reachable at /scalar/v1 on the https profile.
/// </summary>
public sealed class LaunchSettingsEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var project = ctx.Config.ProjectName;
        var name    = CleanLayout.ApiProjectName(project);

        var content = $$"""
            {
              "$schema": "https://json.schemastore.org/launchsettings.json",
              "profiles": {
                "https": {
                  "commandName": "Project",
                  "launchBrowser": true,
                  "launchUrl": "scalar/v1",
                  "applicationUrl": "https://localhost:5443;http://localhost:5080",
                  "environmentVariables": {
                    "DOTNET_ENVIRONMENT": "Development"
                  }
                },
                "http": {
                  "commandName": "Project",
                  "launchBrowser": true,
                  "launchUrl": "scalar/v1",
                  "applicationUrl": "http://localhost:5080",
                  "environmentVariables": {
                    "DOTNET_ENVIRONMENT": "Development"
                  }
                },
                "{{name}}": {
                  "commandName": "Project",
                  "dotnetRunMessages": true,
                  "launchBrowser": false,
                  "applicationUrl": "https://localhost:5443;http://localhost:5080",
                  "environmentVariables": {
                    "DOTNET_ENVIRONMENT": "Development"
                  }
                }
              }
            }
            """;

        return new[] { new EmittedFile(CleanLayout.LaunchSettingsPath(project), content) };
    }
}
