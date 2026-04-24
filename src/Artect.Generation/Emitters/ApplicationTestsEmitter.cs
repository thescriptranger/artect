using System.Collections.Generic;
using Artect.Config;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits &lt;Project&gt;.Application.Tests project.
/// In the IT-Director shape there are no interactors, validators, or pipeline behaviors to
/// unit-test in isolation, so this emitter produces a single placeholder SmokeTests.cs
/// (one per project) to keep the test project compilable and discoverable by the runner.
/// Mapping tests (Dto.ToResponse) are emitted by <see cref="ApiTestsEmitter"/> where the
/// ToResponse() extension already lives, avoiding an upward layer reference.
/// Gated by <c>cfg.IncludeTestsProject</c>.
/// </summary>
public sealed class ApplicationTestsEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        if (!ctx.Config.IncludeTestsProject) return System.Array.Empty<EmittedFile>();

        var project = ctx.Config.ProjectName;
        var tfm = ctx.Config.TargetFramework.ToMoniker();
        var testProject = $"{project}.Application.Tests";
        var testsDir = $"tests/{testProject}";

        return new EmittedFile[]
        {
            new EmittedFile($"{testsDir}/{testProject}.csproj", BuildCsproj(project, tfm)),
            new EmittedFile($"{testsDir}/SmokeTests.cs", BuildSmoke(project)),
        };
    }

    static string BuildCsproj(string project, string tfm) => $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>{tfm}</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <LangVersion>12.0</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""xunit"" Version=""2.*"" />
    <PackageReference Include=""xunit.runner.visualstudio"" Version=""2.*"">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include=""Microsoft.NET.Test.Sdk"" Version=""17.*"" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include=""../../src/{project}.Application/{project}.Application.csproj"" />
  </ItemGroup>
</Project>";

    static string BuildSmoke(string project) => $@"using Xunit;

namespace {project}.Application.Tests;

/// <summary>
/// Placeholder test class — no Application-only unit tests exist in the IT-Director shape
/// (no interactors, no pipeline behaviors). Mapping tests live in Api.Tests where
/// the ToResponse() extension is naturally available.
/// </summary>
public class SmokeTests
{{
    [Fact]
    public void Application_layer_smoke_test() => Assert.True(true);
}}
";
}
