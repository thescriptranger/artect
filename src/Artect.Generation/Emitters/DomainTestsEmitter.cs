using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits &lt;Project&gt;.Domain.Tests project — pure value tests for the
/// &lt;Entity&gt;.Create(...) factory methods. No NSubstitute; no network required.
/// Gated by <c>cfg.IncludeTestsProject</c>.
/// </summary>
public sealed class DomainTestsEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        if (!ctx.Config.IncludeTestsProject) return System.Array.Empty<EmittedFile>();
        var project = ctx.Config.ProjectName;
        var tfm = ctx.Config.TargetFramework.ToMoniker();
        var testProject = $"{project}.Domain.Tests";
        var testsDir = $"tests/{testProject}";

        var list = new List<EmittedFile>
        {
            new EmittedFile($"{testsDir}/{testProject}.csproj", BuildCsproj(project, tfm)),
        };

        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.IsJoinTable) continue;
            if (!entity.HasPrimaryKey) continue;
            list.Add(BuildEntityTests(ctx, testsDir, entity));
        }
        return list;
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
    <PackageReference Include=""FluentAssertions"" Version=""6.*"" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include=""../../src/{project}.Domain/{project}.Domain.csproj"" />
  </ItemGroup>
</Project>";

    static EmittedFile BuildEntityTests(EmitterContext ctx, string testsDir, NamedEntity entity)
    {
        var project = ctx.Config.ProjectName;
        var e = entity.EntityTypeName;
        var sb = new StringBuilder();
        sb.AppendLine("using FluentAssertions;");
        sb.AppendLine("using Xunit;");
        sb.AppendLine($"using {project}.Domain.Common;");
        sb.AppendLine($"using {project}.Domain.Entities;");
        sb.AppendLine();
        sb.AppendLine($"namespace {project}.Domain.Tests.Entities;");
        sb.AppendLine();
        sb.AppendLine($"public class {e}Tests");
        sb.AppendLine("{");

        // Per required string column, emit a test that empty input fails
        foreach (var col in entity.Table.Columns
                     .Where(c => !c.IsServerGenerated && !c.IsNullable
                                 && c.ClrType == Artect.Core.Schema.ClrType.String))
        {
            var colName = col.Name;
            sb.AppendLine($"    [Fact]");
            sb.AppendLine($"    public void Create_rejects_empty_{colName}()");
            sb.AppendLine("    {");
            sb.AppendLine($"        var result = {e}.Create({BuildCreateArgs(entity, failingCol: colName)});");
            sb.AppendLine($"        result.Should().BeOfType<Result<{e}>.Failure>();");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        // Success test
        sb.AppendLine("    [Fact]");
        sb.AppendLine("    public void Create_succeeds_with_minimal_valid_input()");
        sb.AppendLine("    {");
        sb.AppendLine($"        var result = {e}.Create({BuildCreateArgs(entity)});");
        sb.AppendLine($"        result.Should().BeOfType<Result<{e}>.Success>();");
        sb.AppendLine("    }");

        sb.AppendLine("}");
        return new EmittedFile($"{testsDir}/Entities/{e}Tests.cs", sb.ToString());
    }

    static string BuildCreateArgs(NamedEntity entity, string? failingCol = null)
    {
        var args = entity.Table.Columns
            .Where(c => !c.IsServerGenerated)
            .Select(c =>
            {
                if (failingCol is not null && c.Name == failingCol)
                    return c.ClrType == Artect.Core.Schema.ClrType.String ? "\"\"" : "default";
                return ValidPlaceholder(c);
            });
        return string.Join(", ", args);
    }

    static string ValidPlaceholder(Artect.Core.Schema.Column c) => c.ClrType switch
    {
        Artect.Core.Schema.ClrType.String => $"\"valid-{c.Name.ToLowerInvariant()}\"",
        Artect.Core.Schema.ClrType.Int32 => "1",
        Artect.Core.Schema.ClrType.Int64 => "1L",
        Artect.Core.Schema.ClrType.Int16 => "(short)1",
        Artect.Core.Schema.ClrType.Byte => "(byte)1",
        Artect.Core.Schema.ClrType.Boolean => "false",
        Artect.Core.Schema.ClrType.Decimal => "1m",
        Artect.Core.Schema.ClrType.Double => "1.0",
        Artect.Core.Schema.ClrType.Single => "1.0f",
        Artect.Core.Schema.ClrType.DateTime => "System.DateTime.UtcNow",
        Artect.Core.Schema.ClrType.DateTimeOffset => "System.DateTimeOffset.UtcNow",
        Artect.Core.Schema.ClrType.Guid => "System.Guid.NewGuid()",
        Artect.Core.Schema.ClrType.ByteArray => "System.Array.Empty<byte>()",
        _ => "default!",
    };
}
