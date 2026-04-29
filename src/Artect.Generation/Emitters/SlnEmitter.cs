using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Artect.Config;

namespace Artect.Generation.Emitters;

public sealed class SlnEmitter : IEmitter
{
    private const string CsharpSdkProjectTypeGuid  = "9A19103F-16F7-4668-BE54-9A1E7A4F7556";
    private const string SolutionFolderTypeGuid    = "2150E333-8FDC-42A3-9474-1A3956D46DE8";

    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var cfg     = ctx.Config;
        var project = cfg.ProjectName;

        var srcPaths = new List<string>
        {
            $"{CleanLayout.ApiDir(project)}/{CleanLayout.ApiProjectName(project)}.csproj",
            $"{CleanLayout.ApplicationDir(project)}/{CleanLayout.ApplicationProjectName(project)}.csproj",
            $"{CleanLayout.DomainDir(project)}/{CleanLayout.DomainProjectName(project)}.csproj",
            $"{CleanLayout.InfrastructureDir(project)}/{CleanLayout.InfrastructureProjectName(project)}.csproj",
            $"{CleanLayout.SharedDir(project)}/{CleanLayout.SharedProjectName(project)}.csproj",
        };

        var testPaths = new List<string>();
        if (cfg.IncludeTestsProject)
        {
            var domainTests = $"{project}.Domain.Tests";
            var appTests    = $"{project}.Application.Tests";
            var infraTests  = $"{project}.Infrastructure.Tests";
            var apiTests    = $"{project}.Api.Tests";
            var archTests   = $"{project}.Architecture.Tests";

            testPaths.Add($"tests/{domainTests}/{domainTests}.csproj");
            testPaths.Add($"tests/{appTests}/{appTests}.csproj");
            testPaths.Add($"tests/{infraTests}/{infraTests}.csproj");
            testPaths.Add($"tests/{apiTests}/{apiTests}.csproj");
            testPaths.Add($"tests/{archTests}/{archTests}.csproj");
        }

        return cfg.TargetFramework == TargetFramework.Net10_0
            ? new[] { new EmittedFile($"{project}.slnx", BuildSlnx(srcPaths, testPaths)) }
            : new[] { new EmittedFile($"{project}.sln", BuildSln(project, srcPaths, testPaths)) };
    }

    static string BuildSlnx(IReadOnlyList<string> srcPaths, IReadOnlyList<string> testPaths)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<Solution>");
        sb.AppendLine("  <Folder Name=\"/src/\">");
        foreach (var p in srcPaths)
            sb.AppendLine($"    <Project Path=\"{p}\" />");
        sb.AppendLine("  </Folder>");
        if (testPaths.Count > 0)
        {
            sb.AppendLine("  <Folder Name=\"/tests/\">");
            foreach (var p in testPaths)
                sb.AppendLine($"    <Project Path=\"{p}\" />");
            sb.AppendLine("  </Folder>");
        }
        sb.Append("</Solution>");
        return sb.ToString();
    }

    static string BuildSln(string project, IReadOnlyList<string> srcPaths, IReadOnlyList<string> testPaths)
    {
        var projectTypeGuid = "{" + CsharpSdkProjectTypeGuid + "}";
        var folderTypeGuid  = "{" + SolutionFolderTypeGuid + "}";
        var slnGuid         = StableGuid($"sln::{project}").ToString("B").ToUpperInvariant();
        var srcFolderGuid   = StableGuid($"folder::src::{project}").ToString("B").ToUpperInvariant();
        var testsFolderGuid = StableGuid($"folder::tests::{project}").ToString("B").ToUpperInvariant();

        var srcEntries = new List<(string AssemblyName, string BackslashPath, string Guid)>();
        foreach (var p in srcPaths) srcEntries.Add(BuildEntry(p));

        var testEntries = new List<(string AssemblyName, string BackslashPath, string Guid)>();
        foreach (var p in testPaths) testEntries.Add(BuildEntry(p));

        var allEntries = new List<(string AssemblyName, string BackslashPath, string Guid)>();
        allEntries.AddRange(srcEntries);
        allEntries.AddRange(testEntries);

        var sb = new StringBuilder();
        sb.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
        sb.AppendLine("# Visual Studio Version 17");

        foreach (var (name, path, guid) in allEntries)
        {
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "Project(\"{0}\") = \"{1}\", \"{2}\", \"{3}\"",
                projectTypeGuid, name, path, guid).AppendLine();
            sb.AppendLine("EndProject");
        }

        sb.AppendFormat(CultureInfo.InvariantCulture,
            "Project(\"{0}\") = \"src\", \"src\", \"{1}\"",
            folderTypeGuid, srcFolderGuid).AppendLine();
        sb.AppendLine("EndProject");

        if (testEntries.Count > 0)
        {
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "Project(\"{0}\") = \"tests\", \"tests\", \"{1}\"",
                folderTypeGuid, testsFolderGuid).AppendLine();
            sb.AppendLine("EndProject");
        }

        sb.AppendLine("Global");
        sb.AppendLine("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution");
        sb.AppendLine("\t\tDebug|Any CPU = Debug|Any CPU");
        sb.AppendLine("\t\tRelease|Any CPU = Release|Any CPU");
        sb.AppendLine("\tEndGlobalSection");
        sb.AppendLine("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution");

        foreach (var (_, _, guid) in allEntries)
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

        sb.AppendLine("\tGlobalSection(NestedProjects) = preSolution");
        foreach (var (_, _, guid) in srcEntries)
            sb.AppendFormat(CultureInfo.InvariantCulture, "\t\t{0} = {1}", guid, srcFolderGuid).AppendLine();
        foreach (var (_, _, guid) in testEntries)
            sb.AppendFormat(CultureInfo.InvariantCulture, "\t\t{0} = {1}", guid, testsFolderGuid).AppendLine();
        sb.AppendLine("\tEndGlobalSection");

        sb.AppendLine("\tGlobalSection(ExtensibilityGlobals) = postSolution");
        sb.AppendFormat(CultureInfo.InvariantCulture, "\t\tSolutionGuid = {0}", slnGuid).AppendLine();
        sb.AppendLine("\tEndGlobalSection");
        sb.Append("EndGlobal");

        return sb.ToString();
    }

    static (string AssemblyName, string BackslashPath, string Guid) BuildEntry(string csprojPath)
    {
        var assemblyName = System.IO.Path.GetFileNameWithoutExtension(csprojPath);
        var backslash    = csprojPath.Replace('/', '\\');
        var guid         = StableGuid($"project::{csprojPath}").ToString("B").ToUpperInvariant();
        return (assemblyName, backslash, guid);
    }

    static Guid StableGuid(string seed)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(seed));
        return new Guid(bytes);
    }
}
