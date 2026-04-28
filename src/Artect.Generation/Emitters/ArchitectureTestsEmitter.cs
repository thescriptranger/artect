using System.Collections.Generic;
using System.Text;
using Artect.Config;

namespace Artect.Generation.Emitters;

/// <summary>
/// V#14 acceptance #1 + #2: emits a dedicated <c>&lt;Project&gt;.Architecture.Tests</c>
/// project that uses NetArchTest.Rules to enforce Clean Architecture dependency
/// rules between the four generated projects (Domain, Application, Infrastructure,
/// Api). Tests fail at <c>dotnet test</c> time when a developer accidentally
/// references an outer layer from an inner one.
///
/// Rules enforced:
/// - Domain shall not depend on Application, Infrastructure, or Api.
/// - Application shall not depend on Infrastructure or Api.
/// - Infrastructure shall not depend on Api.
///
/// Shared is intentionally not bound to any rule — both Application and Api may
/// reference it (V#5 documented Application → Shared for <c>Optional&lt;T&gt;</c>).
/// Gated by <c>cfg.IncludeTestsProject</c>.
/// </summary>
public sealed class ArchitectureTestsEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        if (!ctx.Config.IncludeTestsProject) return System.Array.Empty<EmittedFile>();

        var project = ctx.Config.ProjectName;
        var tfm = ctx.Config.TargetFramework.ToMoniker();
        var testProject = $"{project}.Architecture.Tests";
        var testsDir = $"tests/{testProject}";

        return new EmittedFile[]
        {
            new EmittedFile($"{testsDir}/{testProject}.csproj", BuildCsproj(project, tfm)),
            new EmittedFile($"{testsDir}/LayerDependencyTests.cs", BuildTests(project, testProject)),
            new EmittedFile($"{testsDir}/RegistrationCompletenessTests.cs", BuildRegistrationTests(project, testProject)),
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
    <PackageReference Include=""NetArchTest.Rules"" Version=""1.*"" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include=""../../src/{project}.Api/{project}.Api.csproj"" />
    <ProjectReference Include=""../../src/{project}.Application/{project}.Application.csproj"" />
    <ProjectReference Include=""../../src/{project}.Domain/{project}.Domain.csproj"" />
    <ProjectReference Include=""../../src/{project}.Infrastructure/{project}.Infrastructure.csproj"" />
  </ItemGroup>
</Project>";

    static string BuildTests(string project, string testsNs)
    {
        var domain = $"{project}.Domain";
        var application = $"{project}.Application";
        var infrastructure = $"{project}.Infrastructure";
        var api = $"{project}.Api";

        var sb = new StringBuilder();
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Reflection;");
        sb.AppendLine("using NetArchTest.Rules;");
        sb.AppendLine("using Xunit;");
        sb.AppendLine();
        sb.AppendLine($"namespace {testsNs};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// V#14: Clean Architecture dependency-rule tests. Each test loads an inner-");
        sb.AppendLine("/// layer assembly and asserts none of its types reference outer-layer namespaces.");
        sb.AppendLine("/// Failures list the offending types so the fix is mechanical.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public class LayerDependencyTests");
        sb.AppendLine("{");
        sb.AppendLine($"    static Assembly Domain() => Assembly.Load(\"{domain}\");");
        sb.AppendLine($"    static Assembly Application() => Assembly.Load(\"{application}\");");
        sb.AppendLine($"    static Assembly Infrastructure() => Assembly.Load(\"{infrastructure}\");");
        sb.AppendLine();
        sb.AppendLine("    [Fact]");
        sb.AppendLine("    public void Domain_does_not_depend_on_outer_layers()");
        sb.AppendLine("    {");
        sb.AppendLine("        var result = Types.InAssembly(Domain())");
        sb.AppendLine("            .ShouldNot()");
        sb.AppendLine($"            .HaveDependencyOnAny(\"{application}\", \"{infrastructure}\", \"{api}\")");
        sb.AppendLine("            .GetResult();");
        sb.AppendLine("        Assert.True(result.IsSuccessful, FormatFailure(\"Domain\", result));");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    [Fact]");
        sb.AppendLine("    public void Application_does_not_depend_on_outer_layers()");
        sb.AppendLine("    {");
        sb.AppendLine("        var result = Types.InAssembly(Application())");
        sb.AppendLine("            .ShouldNot()");
        sb.AppendLine($"            .HaveDependencyOnAny(\"{infrastructure}\", \"{api}\")");
        sb.AppendLine("            .GetResult();");
        sb.AppendLine("        Assert.True(result.IsSuccessful, FormatFailure(\"Application\", result));");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    [Fact]");
        sb.AppendLine("    public void Infrastructure_does_not_depend_on_api()");
        sb.AppendLine("    {");
        sb.AppendLine("        var result = Types.InAssembly(Infrastructure())");
        sb.AppendLine("            .ShouldNot()");
        sb.AppendLine($"            .HaveDependencyOn(\"{api}\")");
        sb.AppendLine("            .GetResult();");
        sb.AppendLine("        Assert.True(result.IsSuccessful, FormatFailure(\"Infrastructure\", result));");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    static string FormatFailure(string layer, NetArchTest.Rules.TestResult result)");
        sb.AppendLine("    {");
        sb.AppendLine("        var offenders = result.FailingTypeNames is null");
        sb.AppendLine("            ? \"<unknown>\"");
        sb.AppendLine("            : string.Join(\", \", result.FailingTypeNames);");
        sb.AppendLine("        return layer + \" violates layer dependency rules. Offending types: \" + offenders;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string BuildRegistrationTests(string project, string testsNs)
    {
        var application = $"{project}.Application";
        var infrastructure = $"{project}.Infrastructure";
        var absNs = $"{project}.Application.Abstractions";

        var sb = new StringBuilder();
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Reflection;");
        sb.AppendLine($"using {absNs};");
        sb.AppendLine("using Xunit;");
        sb.AppendLine();
        sb.AppendLine($"namespace {testsNs};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// V#15 acceptance #3: missing handler/repository registrations fail at test time,");
        sb.AppendLine("/// not at runtime under load. For every IRepository- and IReadService-derived");
        sb.AppendLine("/// interface declared in Application, this asserts exactly one concrete");
        sb.AppendLine("/// implementation lives in Infrastructure. The marker-driven scan in");
        sb.AppendLine("/// Infrastructure.DependencyInjection then picks the impl up automatically.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public class RegistrationCompletenessTests");
        sb.AppendLine("{");
        sb.AppendLine($"    static Assembly Application() => Assembly.Load(\"{application}\");");
        sb.AppendLine($"    static Assembly Infrastructure() => Assembly.Load(\"{infrastructure}\");");
        sb.AppendLine();
        sb.AppendLine("    [Fact]");
        sb.AppendLine("    public void Every_IRepository_has_an_Infrastructure_impl()");
        sb.AppendLine("    {");
        sb.AppendLine("        AssertExactlyOneImplPerInterface(typeof(IRepository));");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    [Fact]");
        sb.AppendLine("    public void Every_IReadService_has_an_Infrastructure_impl()");
        sb.AppendLine("    {");
        sb.AppendLine("        AssertExactlyOneImplPerInterface(typeof(IReadService));");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    static void AssertExactlyOneImplPerInterface(System.Type marker)");
        sb.AppendLine("    {");
        sb.AppendLine("        var interfaces = Application().GetExportedTypes()");
        sb.AppendLine("            .Where(t => t.IsInterface && t != marker && marker.IsAssignableFrom(t))");
        sb.AppendLine("            .ToList();");
        sb.AppendLine();
        sb.AppendLine("        var concretes = Infrastructure().GetTypes()");
        sb.AppendLine("            .Where(t => !t.IsAbstract && !t.IsInterface)");
        sb.AppendLine("            .ToList();");
        sb.AppendLine();
        sb.AppendLine("        var missing = new System.Collections.Generic.List<string>();");
        sb.AppendLine("        foreach (var iface in interfaces)");
        sb.AppendLine("        {");
        sb.AppendLine("            var impls = concretes.Where(c => iface.IsAssignableFrom(c)).ToList();");
        sb.AppendLine("            if (impls.Count == 0) missing.Add($\"{iface.Name}: no implementation in Infrastructure\");");
        sb.AppendLine("            else if (impls.Count > 1) missing.Add($\"{iface.Name}: ambiguous — \" + string.Join(\", \", impls.Select(i => i.Name)));");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        Assert.True(missing.Count == 0,");
        sb.AppendLine("            $\"{marker.Name} registration completeness violations: \" + string.Join(\"; \", missing));");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
