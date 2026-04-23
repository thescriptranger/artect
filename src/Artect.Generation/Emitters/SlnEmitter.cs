using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits <c>&lt;Project&gt;.sln</c> at the scaffold root.
/// GUIDs are derived deterministically from the project's relative .csproj path
/// so the file is byte-identical across machines and runs.
/// </summary>
public sealed class SlnEmitter : IEmitter
{
    private const string CsharpSdkProjectTypeGuid = "9A19103F-16F7-4668-BE54-9A1E7A4F7556";

    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var cfg     = ctx.Config;
        var project = cfg.ProjectName;

        // Collect csproj relative paths (forward-slash, as stored by other emitters)
        var csprojPaths = new List<string>
        {
            $"{CleanLayout.ApiDir(project)}/{CleanLayout.ApiProjectName(project)}.csproj",
            $"{CleanLayout.ApplicationDir(project)}/{CleanLayout.ApplicationProjectName(project)}.csproj",
            $"{CleanLayout.DomainDir(project)}/{CleanLayout.DomainProjectName(project)}.csproj",
            $"{CleanLayout.InfrastructureDir(project)}/{CleanLayout.InfrastructureProjectName(project)}.csproj",
            $"{CleanLayout.SharedDir(project)}/{CleanLayout.SharedProjectName(project)}.csproj",
        };

        if (cfg.IncludeTestsProject)
        {
            var domainTests  = $"{project}.Domain.Tests";
            var appTests     = $"{project}.Application.Tests";
            var infraTests   = $"{project}.Infrastructure.Tests";
            var apiTests     = $"{project}.Api.Tests";

            csprojPaths.Add($"tests/{domainTests}/{domainTests}.csproj");
            csprojPaths.Add($"tests/{appTests}/{appTests}.csproj");
            csprojPaths.Add($"tests/{infraTests}/{infraTests}.csproj");
            csprojPaths.Add($"tests/{apiTests}/{apiTests}.csproj");
        }

        var projectTypeGuid = "{" + CsharpSdkProjectTypeGuid + "}";
        var slnGuid         = StableGuid($"sln::{project}").ToString("B").ToUpperInvariant();

        var entries = new List<(string AssemblyName, string BackslashPath, string Guid)>();
        foreach (var p in csprojPaths)
        {
            var assemblyName = System.IO.Path.GetFileNameWithoutExtension(p);
            var backslash    = p.Replace('/', '\\');
            var guid         = StableGuid($"project::{p}").ToString("B").ToUpperInvariant();
            entries.Add((assemblyName, backslash, guid));
        }

        var sb = new StringBuilder();
        sb.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
        sb.AppendLine("# Visual Studio Version 17");

        foreach (var (name, path, guid) in entries)
        {
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "Project(\"{0}\") = \"{1}\", \"{2}\", \"{3}\"",
                projectTypeGuid, name, path, guid).AppendLine();
            sb.AppendLine("EndProject");
        }

        sb.AppendLine("Global");
        sb.AppendLine("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution");
        sb.AppendLine("\t\tDebug|Any CPU = Debug|Any CPU");
        sb.AppendLine("\t\tRelease|Any CPU = Release|Any CPU");
        sb.AppendLine("\tEndGlobalSection");
        sb.AppendLine("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution");

        foreach (var (_, _, guid) in entries)
        {
            sb.AppendFormat(CultureInfo.InvariantCulture, "\t\t{0}.Debug|Any CPU.ActiveCfg = Debug|Any CPU", guid).AppendLine();
            sb.AppendFormat(CultureInfo.InvariantCulture, "\t\t{0}.Debug|Any CPU.Build.0 = Debug|Any CPU", guid).AppendLine();
            sb.AppendFormat(CultureInfo.InvariantCulture, "\t\t{0}.Release|Any CPU.ActiveCfg = Release|Any CPU", guid).AppendLine();
            sb.AppendFormat(CultureInfo.InvariantCulture, "\t\t{0}.Release|Any CPU.Build.0 = Release|Any CPU", guid).AppendLine();
        }

        sb.AppendLine("\tEndGlobalSection");
        sb.AppendLine("\tGlobalSection(SolutionProperties) = preSolution");
        sb.AppendLine("\t\tHideSolutionNode = FALSE");
        sb.AppendLine("\tEndGlobalSection");
        sb.AppendLine("\tGlobalSection(ExtensibilityGlobals) = postSolution");
        sb.AppendFormat(CultureInfo.InvariantCulture, "\t\tSolutionGuid = {0}", slnGuid).AppendLine();
        sb.AppendLine("\tEndGlobalSection");
        sb.Append("EndGlobal");

        return new[] { new EmittedFile($"{project}.sln", sb.ToString()) };
    }

    // Deterministic GUID: MD5 of UTF-8 seed → 16 bytes → Guid
    static Guid StableGuid(string seed)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(seed));
        return new Guid(bytes);
    }
}
