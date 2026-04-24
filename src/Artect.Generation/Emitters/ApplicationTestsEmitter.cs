using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits &lt;Project&gt;.Application.Tests project — NSubstitute-based unit tests
/// for use-case interactors. Tests mock only the repository interfaces.
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

        var list = new List<EmittedFile>
        {
            new EmittedFile($"{testsDir}/{testProject}.csproj", BuildCsproj(project, tfm)),
        };

        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.IsJoinTable) continue;
            if (!entity.HasPrimaryKey) continue;
            list.AddRange(BuildUseCaseTests(ctx, testsDir, entity));
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
    <PackageReference Include=""NSubstitute"" Version=""5.*"" />
    <PackageReference Include=""FluentAssertions"" Version=""6.*"" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include=""../../src/{project}.Application/{project}.Application.csproj"" />
    <ProjectReference Include=""../../src/{project}.Domain/{project}.Domain.csproj"" />
  </ItemGroup>
</Project>";

    static IEnumerable<EmittedFile> BuildUseCaseTests(EmitterContext ctx, string testsDir, NamedEntity entity)
    {
        var corrections = ctx.NamingCorrections;

        if (ctx.Config.Crud.HasFlag(CrudOperation.Post))
            yield return BuildCreateTest(ctx.Config.ProjectName, testsDir, entity, corrections);
        if (ctx.Config.Crud.HasFlag(CrudOperation.GetById))
            yield return BuildGetByIdTest(ctx.Config.ProjectName, testsDir, entity, corrections);
        if (ctx.Config.Crud.HasFlag(CrudOperation.Put))
            yield return BuildUpdateTest(ctx.Config.ProjectName, testsDir, entity, corrections);
        if (ctx.Config.Crud.HasFlag(CrudOperation.Delete))
            yield return BuildDeleteTest(ctx.Config.ProjectName, testsDir, entity, corrections);
    }

    static EmittedFile BuildCreateTest(string project, string testsDir, NamedEntity entity, System.Collections.Generic.IReadOnlyDictionary<string, string> corrections)
    {
        var e = entity.EntityTypeName;
        var repoAbsNs = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var commandsNs = CleanLayout.ApplicationCommandsNamespace(project);
        var modelsNs = CleanLayout.ApplicationDtosNamespace(project);
        var useCasesNs = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";
        var commonNs = CleanLayout.ApplicationCommonNamespace(project);
        var entityNs = $"{CleanLayout.DomainNamespace(project)}.Entities";

        var writeRepo = $"I{e}Repository";
        var repoParam = "repo";

        var sb = new StringBuilder();
        sb.AppendLine("using FluentAssertions;");
        sb.AppendLine("using NSubstitute;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Xunit;");
        sb.AppendLine($"using {repoAbsNs};");
        sb.AppendLine($"using {commandsNs};");
        sb.AppendLine($"using {modelsNs};");
        sb.AppendLine($"using {useCasesNs};");
        sb.AppendLine($"using {commonNs};");
        sb.AppendLine($"using {entityNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {project}.Application.Tests.UseCases;");
        sb.AppendLine();
        sb.AppendLine($"public class Create{e}UseCaseTests");
        sb.AppendLine("{");
        sb.AppendLine("    [Fact]");
        sb.AppendLine("    public async Task Returns_Success_when_inputs_valid()");
        sb.AppendLine("    {");
        sb.AppendLine($"        var {repoParam} = Substitute.For<{writeRepo}>();");
        sb.AppendLine($"        {repoParam}.CreateAsync(Arg.Any<{e}>(), Arg.Any<CancellationToken>())");
        sb.AppendLine($"             .Returns(new {e}Dto {{ {BuildModelInit(entity, corrections)} }});");
        sb.AppendLine($"        var sut = new Create{e}UseCase({repoParam});");
        sb.AppendLine($"        var command = new Create{e}Command {{ {BuildValidArgs(entity, corrections)} }};");
        sb.AppendLine($"        var result = await sut.ExecuteAsync(command, default);");
        sb.AppendLine($"        result.Should().BeOfType<UseCaseResult<{e}Dto>.Success>();");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return new EmittedFile($"{testsDir}/UseCases/Create{e}UseCaseTests.cs", sb.ToString());
    }

    static EmittedFile BuildGetByIdTest(string project, string testsDir, NamedEntity entity, System.Collections.Generic.IReadOnlyDictionary<string, string> corrections)
    {
        var e = entity.EntityTypeName;
        var repoAbsNs = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var queriesNs = CleanLayout.ApplicationQueriesNamespace(project);
        var modelsNs = CleanLayout.ApplicationDtosNamespace(project);
        var useCasesNs = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";
        var commonNs = CleanLayout.ApplicationCommonNamespace(project);

        var readRepo = $"I{e}Repository";
        var repoParam = "repo";

        var pk = entity.Table.PrimaryKey!;
        var pkNames = pk.ColumnNames.ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        var pkCols = entity.Table.Columns.Where(c => pkNames.Contains(c.Name)).ToList();
        var pkProp = EntityNaming.PropertyName(pkCols[0], corrections);
        var pkType = SqlTypeMap.ToCs(pkCols[0].ClrType);
        var pkValidLiteral = ValidPlaceholder(pkCols[0]);

        var sb = new StringBuilder();
        sb.AppendLine("using FluentAssertions;");
        sb.AppendLine("using NSubstitute;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Xunit;");
        sb.AppendLine($"using {repoAbsNs};");
        sb.AppendLine($"using {queriesNs};");
        sb.AppendLine($"using {modelsNs};");
        sb.AppendLine($"using {useCasesNs};");
        sb.AppendLine($"using {commonNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {project}.Application.Tests.UseCases;");
        sb.AppendLine();
        sb.AppendLine($"public class Get{e}ByIdUseCaseTests");
        sb.AppendLine("{");

        // Success path
        sb.AppendLine("    [Fact]");
        sb.AppendLine("    public async Task Returns_Success_when_entity_exists()");
        sb.AppendLine("    {");
        sb.AppendLine($"        var {repoParam} = Substitute.For<{readRepo}>();");
        sb.AppendLine($"        {repoParam}.GetByIdAsync(Arg.Any<{pkType}>(), Arg.Any<CancellationToken>())");
        sb.AppendLine($"             .Returns(new {e}Dto {{ {BuildModelInit(entity, corrections)} }});");
        sb.AppendLine($"        var sut = new Get{e}ByIdUseCase({repoParam});");
        sb.AppendLine($"        var query = new Get{e}ByIdQuery {{ {pkProp} = {pkValidLiteral} }};");
        sb.AppendLine($"        var result = await sut.ExecuteAsync(query, default);");
        sb.AppendLine($"        result.Should().BeOfType<UseCaseResult<{e}Dto>.Success>();");
        sb.AppendLine("    }");
        sb.AppendLine();

        // NotFound path
        sb.AppendLine("    [Fact]");
        sb.AppendLine("    public async Task Returns_NotFound_when_entity_missing()");
        sb.AppendLine("    {");
        sb.AppendLine($"        var {repoParam} = Substitute.For<{readRepo}>();");
        sb.AppendLine($"        {repoParam}.GetByIdAsync(Arg.Any<{pkType}>(), Arg.Any<CancellationToken>())");
        sb.AppendLine($"             .Returns(({e}Dto?)null);");
        sb.AppendLine($"        var sut = new Get{e}ByIdUseCase({repoParam});");
        sb.AppendLine($"        var query = new Get{e}ByIdQuery {{ {pkProp} = {pkValidLiteral} }};");
        sb.AppendLine($"        var result = await sut.ExecuteAsync(query, default);");
        sb.AppendLine($"        result.Should().BeOfType<UseCaseResult<{e}Dto>.NotFound>();");
        sb.AppendLine("    }");

        sb.AppendLine("}");

        return new EmittedFile($"{testsDir}/UseCases/Get{e}ByIdUseCaseTests.cs", sb.ToString());
    }

    static EmittedFile BuildUpdateTest(string project, string testsDir, NamedEntity entity, System.Collections.Generic.IReadOnlyDictionary<string, string> corrections)
    {
        var e = entity.EntityTypeName;
        var repoAbsNs = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var commandsNs = CleanLayout.ApplicationCommandsNamespace(project);
        var modelsNs = CleanLayout.ApplicationDtosNamespace(project);
        var useCasesNs = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";
        var commonNs = CleanLayout.ApplicationCommonNamespace(project);

        var repo = $"I{e}Repository";
        var repoParam = "repo";

        var pk = entity.Table.PrimaryKey!;
        var pkNames = pk.ColumnNames.ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        var pkCols = entity.Table.Columns.Where(c => pkNames.Contains(c.Name)).ToList();
        var pkProp = EntityNaming.PropertyName(pkCols[0], corrections);
        var pkType = SqlTypeMap.ToCs(pkCols[0].ClrType);
        var pkValidLiteral = ValidPlaceholder(pkCols[0]);

        var sb = new StringBuilder();
        sb.AppendLine("using FluentAssertions;");
        sb.AppendLine("using NSubstitute;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Xunit;");
        sb.AppendLine($"using {repoAbsNs};");
        sb.AppendLine($"using {commandsNs};");
        sb.AppendLine($"using {modelsNs};");
        sb.AppendLine($"using {useCasesNs};");
        sb.AppendLine($"using {commonNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {project}.Application.Tests.UseCases;");
        sb.AppendLine();
        sb.AppendLine($"public class Update{e}UseCaseTests");
        sb.AppendLine("{");

        // Success path
        sb.AppendLine("    [Fact]");
        sb.AppendLine("    public async Task Returns_Success_when_entity_exists()");
        sb.AppendLine("    {");
        sb.AppendLine($"        var {repoParam} = Substitute.For<{repo}>();");
        sb.AppendLine($"        {repoParam}.GetByIdAsync(Arg.Any<{pkType}>(), Arg.Any<CancellationToken>())");
        sb.AppendLine($"             .Returns(new {e}Dto {{ {BuildModelInit(entity, corrections)} }});");
        sb.AppendLine($"        var sut = new Update{e}UseCase({repoParam});");
        sb.AppendLine($"        var command = new Update{e}Command {{ {BuildValidArgs(entity, corrections)} }};");
        sb.AppendLine($"        var result = await sut.ExecuteAsync(command, default);");
        sb.AppendLine($"        result.Should().BeOfType<UseCaseResult<Unit>.Success>();");
        sb.AppendLine("    }");
        sb.AppendLine();

        // NotFound path
        sb.AppendLine("    [Fact]");
        sb.AppendLine("    public async Task Returns_NotFound_when_entity_missing()");
        sb.AppendLine("    {");
        sb.AppendLine($"        var {repoParam} = Substitute.For<{repo}>();");
        sb.AppendLine($"        {repoParam}.GetByIdAsync(Arg.Any<{pkType}>(), Arg.Any<CancellationToken>())");
        sb.AppendLine($"             .Returns(({e}Dto?)null);");
        sb.AppendLine($"        var sut = new Update{e}UseCase({repoParam});");
        sb.AppendLine($"        var command = new Update{e}Command {{ {BuildValidArgs(entity, corrections)} }};");
        sb.AppendLine($"        var result = await sut.ExecuteAsync(command, default);");
        sb.AppendLine($"        result.Should().BeOfType<UseCaseResult<Unit>.NotFound>();");
        sb.AppendLine("    }");

        sb.AppendLine("}");

        return new EmittedFile($"{testsDir}/UseCases/Update{e}UseCaseTests.cs", sb.ToString());
    }

    static EmittedFile BuildDeleteTest(string project, string testsDir, NamedEntity entity, System.Collections.Generic.IReadOnlyDictionary<string, string> corrections)
    {
        var e = entity.EntityTypeName;
        var repoAbsNs = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var commandsNs = CleanLayout.ApplicationCommandsNamespace(project);
        var modelsNs = CleanLayout.ApplicationDtosNamespace(project);
        var useCasesNs = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";
        var commonNs = CleanLayout.ApplicationCommonNamespace(project);

        var repo = $"I{e}Repository";
        var repoParam = "repo";

        var pk = entity.Table.PrimaryKey!;
        var pkNames = pk.ColumnNames.ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        var pkCols = entity.Table.Columns.Where(c => pkNames.Contains(c.Name)).ToList();
        var pkProp = EntityNaming.PropertyName(pkCols[0], corrections);
        var pkType = SqlTypeMap.ToCs(pkCols[0].ClrType);
        var pkValidLiteral = ValidPlaceholder(pkCols[0]);

        var sb = new StringBuilder();
        sb.AppendLine("using FluentAssertions;");
        sb.AppendLine("using NSubstitute;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Xunit;");
        sb.AppendLine($"using {repoAbsNs};");
        sb.AppendLine($"using {commandsNs};");
        sb.AppendLine($"using {modelsNs};");
        sb.AppendLine($"using {useCasesNs};");
        sb.AppendLine($"using {commonNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {project}.Application.Tests.UseCases;");
        sb.AppendLine();
        sb.AppendLine($"public class Delete{e}UseCaseTests");
        sb.AppendLine("{");

        // Success path
        sb.AppendLine("    [Fact]");
        sb.AppendLine("    public async Task Returns_Success_when_entity_exists()");
        sb.AppendLine("    {");
        sb.AppendLine($"        var {repoParam} = Substitute.For<{repo}>();");
        sb.AppendLine($"        {repoParam}.GetByIdAsync(Arg.Any<{pkType}>(), Arg.Any<CancellationToken>())");
        sb.AppendLine($"             .Returns(new {e}Dto {{ {BuildModelInit(entity, corrections)} }});");
        sb.AppendLine($"        var sut = new Delete{e}UseCase({repoParam});");
        sb.AppendLine($"        var command = new Delete{e}Command {{ {pkProp} = {pkValidLiteral} }};");
        sb.AppendLine($"        var result = await sut.ExecuteAsync(command, default);");
        sb.AppendLine($"        result.Should().BeOfType<UseCaseResult<Unit>.Success>();");
        sb.AppendLine("    }");
        sb.AppendLine();

        // NotFound path
        sb.AppendLine("    [Fact]");
        sb.AppendLine("    public async Task Returns_NotFound_when_entity_missing()");
        sb.AppendLine("    {");
        sb.AppendLine($"        var {repoParam} = Substitute.For<{repo}>();");
        sb.AppendLine($"        {repoParam}.GetByIdAsync(Arg.Any<{pkType}>(), Arg.Any<CancellationToken>())");
        sb.AppendLine($"             .Returns(({e}Dto?)null);");
        sb.AppendLine($"        var sut = new Delete{e}UseCase({repoParam});");
        sb.AppendLine($"        var command = new Delete{e}Command {{ {pkProp} = {pkValidLiteral} }};");
        sb.AppendLine($"        var result = await sut.ExecuteAsync(command, default);");
        sb.AppendLine($"        result.Should().BeOfType<UseCaseResult<Unit>.NotFound>();");
        sb.AppendLine("    }");

        sb.AppendLine("}");

        return new EmittedFile($"{testsDir}/UseCases/Delete{e}UseCaseTests.cs", sb.ToString());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static string BuildValidArgs(NamedEntity entity, System.Collections.Generic.IReadOnlyDictionary<string, string> corrections) =>
        string.Join(", ", entity.Table.Columns
            .Where(c => !c.IsServerGenerated)
            .Select(c => $"{EntityNaming.PropertyName(c, corrections)} = {ValidPlaceholder(c)}"));

    static string BuildModelInit(NamedEntity entity, System.Collections.Generic.IReadOnlyDictionary<string, string> corrections) =>
        string.Join(", ", entity.Table.Columns
            .Select(c => $"{EntityNaming.PropertyName(c, corrections)} = {ValidPlaceholder(c)}"));

    static string ValidPlaceholder(Column c) => c.ClrType switch
    {
        ClrType.String => $"\"valid-{c.Name.ToLowerInvariant()}\"",
        ClrType.Int32 => "1",
        ClrType.Int64 => "1L",
        ClrType.Int16 => "(short)1",
        ClrType.Byte => "(byte)1",
        ClrType.Boolean => "false",
        ClrType.Decimal => "1m",
        ClrType.Double => "1.0",
        ClrType.Single => "1.0f",
        ClrType.DateTime => "System.DateTime.UtcNow",
        ClrType.DateTimeOffset => "System.DateTimeOffset.UtcNow",
        ClrType.Guid => "System.Guid.NewGuid()",
        ClrType.ByteArray => "System.Array.Empty<byte>()",
        _ => "default!",
    };
}
