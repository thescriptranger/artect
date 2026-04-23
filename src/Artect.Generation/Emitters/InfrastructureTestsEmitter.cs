using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits &lt;Project&gt;.Infrastructure.Tests project — abstract contract test bases (LSP)
/// for repository interfaces, EF InMemory-backed concrete tests, and EF mapping round-trip tests.
/// Gated by <c>cfg.IncludeTestsProject</c> and <c>cfg.DataAccess == EfCore</c>.
/// </summary>
public sealed class InfrastructureTestsEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        if (!ctx.Config.IncludeTestsProject) return System.Array.Empty<EmittedFile>();
        if (ctx.Config.DataAccess != DataAccessKind.EfCore) return System.Array.Empty<EmittedFile>();

        var project = ctx.Config.ProjectName;
        var tfm = ctx.Config.TargetFramework.ToMoniker();
        var testProject = $"{project}.Infrastructure.Tests";
        var testsDir = $"tests/{testProject}";

        var list = new List<EmittedFile>
        {
            new EmittedFile($"{testsDir}/{testProject}.csproj", BuildCsproj(project, tfm)),
        };

        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.IsJoinTable) continue;
            if (!entity.HasPrimaryKey) continue;

            list.AddRange(BuildEntityTests(ctx, testsDir, entity));
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
    <PackageReference Include=""Microsoft.EntityFrameworkCore.InMemory"" Version=""9.*"" />
    <PackageReference Include=""FluentAssertions"" Version=""6.*"" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include=""../../src/{project}.Application/{project}.Application.csproj"" />
    <ProjectReference Include=""../../src/{project}.Infrastructure/{project}.Infrastructure.csproj"" />
    <ProjectReference Include=""../../src/{project}.Domain/{project}.Domain.csproj"" />
  </ItemGroup>
</Project>";

    static IEnumerable<EmittedFile> BuildEntityTests(EmitterContext ctx, string testsDir, NamedEntity entity)
    {
        var split = ctx.Config.SplitRepositoriesByIntent;
        var project = ctx.Config.ProjectName;

        if (split)
        {
            yield return BuildReadRepositoryContract(project, testsDir, entity);
            yield return BuildReadRepositoryTests(project, testsDir, entity);
        }
        else
        {
            yield return BuildMonolithicRepositoryContract(project, testsDir, entity);
            yield return BuildMonolithicRepositoryTests(project, testsDir, entity);
        }

        yield return BuildMappingTests(project, testsDir, entity);
    }

    // ── Split: abstract contract for IEntityReadRepository ────────────────────

    static EmittedFile BuildReadRepositoryContract(string project, string testsDir, NamedEntity entity)
    {
        var e = entity.EntityTypeName;
        var repoAbsNs = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var modelsNs = CleanLayout.ApplicationModelsNamespace(project);
        var commonNs = CleanLayout.ApplicationCommonNamespace(project);

        var pk = entity.Table.PrimaryKey!;
        var pkNames = pk.ColumnNames.ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        var pkCol = entity.Table.Columns.First(c => pkNames.Contains(c.Name));
        var pkType = SqlTypeMap.ToCs(pkCol.ClrType);
        var pkDefaultLiteral = DefaultLiteralFor(pkCol.ClrType);

        var sb = new StringBuilder();
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using FluentAssertions;");
        sb.AppendLine("using Xunit;");
        sb.AppendLine($"using {repoAbsNs};");
        sb.AppendLine($"using {modelsNs};");
        sb.AppendLine($"using {commonNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {project}.Infrastructure.Tests.Contracts;");
        sb.AppendLine();
        sb.AppendLine($"/// <summary>");
        sb.AppendLine($"/// Liskov Substitution: every implementation of I{e}ReadRepository must pass these tests.");
        sb.AppendLine($"/// </summary>");
        sb.AppendLine($"public abstract class {e}ReadRepositoryContract");
        sb.AppendLine("{");
        sb.AppendLine($"    protected abstract I{e}ReadRepository CreateSut();");
        sb.AppendLine();
        sb.AppendLine("    [Fact]");
        sb.AppendLine($"    public async Task GetByIdAsync_returns_null_for_missing_id()");
        sb.AppendLine("    {");
        sb.AppendLine("        var sut = CreateSut();");
        sb.AppendLine($"        var result = await sut.GetByIdAsync({pkDefaultLiteral}, default);");
        sb.AppendLine("        result.Should().BeNull();");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    [Fact]");
        sb.AppendLine($"    public async Task ListAsync_returns_empty_page_on_empty_store()");
        sb.AppendLine("    {");
        sb.AppendLine("        var sut = CreateSut();");
        sb.AppendLine($"        var result = await sut.ListAsync(1, 10, default);");
        sb.AppendLine("        result.Should().NotBeNull();");
        sb.AppendLine("        result.Items.Should().BeEmpty();");
        sb.AppendLine("        result.TotalCount.Should().Be(0);");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return new EmittedFile($"{testsDir}/Contracts/{e}ReadRepositoryContract.cs", sb.ToString());
    }

    // ── Split: concrete EF tests inheriting the contract ─────────────────────

    static EmittedFile BuildReadRepositoryTests(string project, string testsDir, NamedEntity entity)
    {
        var e = entity.EntityTypeName;
        var dbCtx = $"{project}DbContext";
        var infraDataNs = $"{CleanLayout.InfrastructureNamespace(project)}.Data";
        var infraRepoNs = $"{CleanLayout.InfrastructureNamespace(project)}.Repositories";
        var repoAbsNs = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";

        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        sb.AppendLine($"using {infraDataNs};");
        sb.AppendLine($"using {infraRepoNs};");
        sb.AppendLine($"using {repoAbsNs};");
        sb.AppendLine($"using {project}.Infrastructure.Tests.Contracts;");
        sb.AppendLine();
        sb.AppendLine($"namespace {project}.Infrastructure.Tests.Repositories;");
        sb.AppendLine();
        sb.AppendLine($"public class Ef{e}ReadRepositoryTests : {e}ReadRepositoryContract");
        sb.AppendLine("{");
        sb.AppendLine($"    protected override I{e}ReadRepository CreateSut()");
        sb.AppendLine("    {");
        sb.AppendLine($"        var options = new DbContextOptionsBuilder<{dbCtx}>()");
        sb.AppendLine($"            .UseInMemoryDatabase(\"Ef{e}ReadRepo-\" + System.Guid.NewGuid().ToString(\"N\"))");
        sb.AppendLine("            .Options;");
        sb.AppendLine($"        return new {e}ReadRepository(new {dbCtx}(options));");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return new EmittedFile($"{testsDir}/Repositories/Ef{e}ReadRepositoryTests.cs", sb.ToString());
    }

    // ── Monolithic (SplitRepositoriesByIntent == false) ───────────────────────

    static EmittedFile BuildMonolithicRepositoryContract(string project, string testsDir, NamedEntity entity)
    {
        var e = entity.EntityTypeName;
        var repoAbsNs = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var modelsNs = CleanLayout.ApplicationModelsNamespace(project);
        var commonNs = CleanLayout.ApplicationCommonNamespace(project);

        var pk = entity.Table.PrimaryKey!;
        var pkNames = pk.ColumnNames.ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        var pkCol = entity.Table.Columns.First(c => pkNames.Contains(c.Name));
        var pkDefaultLiteral = DefaultLiteralFor(pkCol.ClrType);

        var sb = new StringBuilder();
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using FluentAssertions;");
        sb.AppendLine("using Xunit;");
        sb.AppendLine($"using {repoAbsNs};");
        sb.AppendLine($"using {modelsNs};");
        sb.AppendLine($"using {commonNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {project}.Infrastructure.Tests.Contracts;");
        sb.AppendLine();
        sb.AppendLine($"/// <summary>");
        sb.AppendLine($"/// Liskov Substitution: every implementation of I{e}Repository must pass these tests.");
        sb.AppendLine($"/// </summary>");
        sb.AppendLine($"public abstract class {e}RepositoryContract");
        sb.AppendLine("{");
        sb.AppendLine($"    protected abstract I{e}Repository CreateSut();");
        sb.AppendLine();
        sb.AppendLine("    [Fact]");
        sb.AppendLine($"    public async Task GetByIdAsync_returns_null_for_missing_id()");
        sb.AppendLine("    {");
        sb.AppendLine("        var sut = CreateSut();");
        sb.AppendLine($"        var result = await sut.GetByIdAsync({pkDefaultLiteral}, default);");
        sb.AppendLine("        result.Should().BeNull();");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    [Fact]");
        sb.AppendLine($"    public async Task ListAsync_returns_empty_page_on_empty_store()");
        sb.AppendLine("    {");
        sb.AppendLine("        var sut = CreateSut();");
        sb.AppendLine($"        var result = await sut.ListAsync(1, 10, default);");
        sb.AppendLine("        result.Should().NotBeNull();");
        sb.AppendLine("        result.Items.Should().BeEmpty();");
        sb.AppendLine("        result.TotalCount.Should().Be(0);");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return new EmittedFile($"{testsDir}/Contracts/{e}RepositoryContract.cs", sb.ToString());
    }

    static EmittedFile BuildMonolithicRepositoryTests(string project, string testsDir, NamedEntity entity)
    {
        var e = entity.EntityTypeName;
        var dbCtx = $"{project}DbContext";
        var infraDataNs = $"{CleanLayout.InfrastructureNamespace(project)}.Data";
        var infraRepoNs = $"{CleanLayout.InfrastructureNamespace(project)}.Repositories";
        var repoAbsNs = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";

        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        sb.AppendLine($"using {infraDataNs};");
        sb.AppendLine($"using {infraRepoNs};");
        sb.AppendLine($"using {repoAbsNs};");
        sb.AppendLine($"using {project}.Infrastructure.Tests.Contracts;");
        sb.AppendLine();
        sb.AppendLine($"namespace {project}.Infrastructure.Tests.Repositories;");
        sb.AppendLine();
        sb.AppendLine($"public class Ef{e}RepositoryTests : {e}RepositoryContract");
        sb.AppendLine("{");
        sb.AppendLine($"    protected override I{e}Repository CreateSut()");
        sb.AppendLine("    {");
        sb.AppendLine($"        var options = new DbContextOptionsBuilder<{dbCtx}>()");
        sb.AppendLine($"            .UseInMemoryDatabase(\"Ef{e}Repo-\" + System.Guid.NewGuid().ToString(\"N\"))");
        sb.AppendLine("            .Options;");
        sb.AppendLine($"        return new {e}Repository(new {dbCtx}(options));");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return new EmittedFile($"{testsDir}/Repositories/Ef{e}RepositoryTests.cs", sb.ToString());
    }

    // ── EF mapping round-trip ──────────────────────────────────────────────────

    static EmittedFile BuildMappingTests(string project, string testsDir, NamedEntity entity)
    {
        var e = entity.EntityTypeName;
        var dbCtx = $"{project}DbContext";
        var infraDataNs = $"{CleanLayout.InfrastructureNamespace(project)}.Data";
        var entityNs = $"{CleanLayout.DomainNamespace(project)}.Entities";

        var pk = entity.Table.PrimaryKey!;
        var pkNames = pk.ColumnNames.ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        var pkCol = entity.Table.Columns.First(c => pkNames.Contains(c.Name));
        var pkProp = EntityNaming.PropertyName(pkCol);

        var nonGenCols = entity.Table.Columns.Where(c => !c.IsServerGenerated).ToList();
        var initBody = string.Join(", ",
            nonGenCols.Select(c => $"{EntityNaming.PropertyName(c)} = {ValidPlaceholder(c)}"));

        var sb = new StringBuilder();
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using FluentAssertions;");
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        sb.AppendLine("using Xunit;");
        sb.AppendLine($"using {infraDataNs};");
        sb.AppendLine($"using {entityNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {project}.Infrastructure.Tests.Mappings;");
        sb.AppendLine();
        sb.AppendLine($"public class {e}MappingTests");
        sb.AppendLine("{");
        sb.AppendLine("    [Fact]");
        sb.AppendLine($"    public async Task {e}_can_be_added_and_retrieved_from_InMemory_DbContext()");
        sb.AppendLine("    {");
        sb.AppendLine($"        var options = new DbContextOptionsBuilder<{dbCtx}>()");
        sb.AppendLine($"            .UseInMemoryDatabase(\"{e}Mapping-\" + System.Guid.NewGuid().ToString(\"N\"))");
        sb.AppendLine("            .Options;");
        sb.AppendLine();
        sb.AppendLine($"        await using var ctx = new {dbCtx}(options);");
        sb.AppendLine($"        var entity = new {e} {{ {initBody} }};");
        sb.AppendLine($"        await ctx.AddAsync(entity);");
        sb.AppendLine("        await ctx.SaveChangesAsync();");
        sb.AppendLine();
        sb.AppendLine($"        await using var ctx2 = new {dbCtx}(options);");
        sb.AppendLine($"        var found = await ctx2.Set<{e}>().FirstOrDefaultAsync();");
        sb.AppendLine("        found.Should().NotBeNull();");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return new EmittedFile($"{testsDir}/Mappings/{e}MappingTests.cs", sb.ToString());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static string DefaultLiteralFor(ClrType clr) => clr switch
    {
        ClrType.Int32 => "0",
        ClrType.Int64 => "0L",
        ClrType.Int16 => "(short)0",
        ClrType.Byte => "(byte)0",
        ClrType.Guid => "System.Guid.Empty",
        ClrType.String => "\"\"",
        _ => "default!",
    };

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
