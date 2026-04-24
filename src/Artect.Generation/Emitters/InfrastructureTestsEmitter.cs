using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits &lt;Project&gt;.Infrastructure.Tests project — EF Core InMemory tests for each
/// &lt;Entity&gt;DataAccess class. One test class per entity; each CRUD method is guarded by
/// its corresponding <see cref="CrudOperation"/> flag.
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

            list.Add(BuildDataAccessTests(ctx, testsDir, entity));
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
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include=""../../src/{project}.Application/{project}.Application.csproj"" />
    <ProjectReference Include=""../../src/{project}.Infrastructure/{project}.Infrastructure.csproj"" />
  </ItemGroup>
</Project>";

    static EmittedFile BuildDataAccessTests(EmitterContext ctx, string testsDir, NamedEntity entity)
    {
        var project = ctx.Config.ProjectName;
        var e = entity.EntityTypeName;
        var plural = entity.DbSetPropertyName;
        var dbCtx = $"{project}DbContext";
        var crud = ctx.Config.Crud;
        var corrections = ctx.NamingCorrections;

        var featureNs = CleanLayout.ApplicationFeatureNamespace(project, e);
        var absNs = CleanLayout.ApplicationFeatureAbstractionsNamespace(project, e);
        var infraDataNs = $"{CleanLayout.InfrastructureNamespace(project)}.Data";
        var dataEntityNs = CleanLayout.InfrastructureDataEntityNamespace(project, e);
        var classNs = $"{project}.Infrastructure.Tests.Data.{plural}";

        var pk = entity.Table.PrimaryKey!;
        var pkColName = pk.ColumnNames[0];
        var pkCol = entity.Table.Columns.First(c =>
            string.Equals(c.Name, pkColName, System.StringComparison.OrdinalIgnoreCase));
        var pkType = SqlTypeMap.ToCs(pkCol.ClrType);
        var pkDefault = DefaultLiteralFor(pkCol.ClrType);

        var nonServerGen = entity.Table.Columns.Where(c => !c.IsServerGenerated).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        sb.AppendLine("using Xunit;");
        sb.AppendLine($"using {featureNs};");
        sb.AppendLine($"using {absNs};");
        sb.AppendLine($"using {infraDataNs};");
        sb.AppendLine($"using {dataEntityNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {classNs};");
        sb.AppendLine();
        sb.AppendLine($"public class {e}DataAccessTests");
        sb.AppendLine("{");
        sb.AppendLine($"    private static {dbCtx} CreateDb(string? dbName = null) =>");
        sb.AppendLine($"        new {dbCtx}(new DbContextOptionsBuilder<{dbCtx}>()");
        sb.AppendLine($"            .UseInMemoryDatabase(dbName ?? $\"{e}-{{System.Guid.NewGuid()}}\").Options);");
        sb.AppendLine();

        // GetByIdAsync null-path (only if GetById is enabled)
        if ((crud & CrudOperation.GetById) != 0)
        {
            sb.AppendLine("    [Fact]");
            sb.AppendLine($"    public async Task GetByIdAsync_returns_null_when_missing()");
            sb.AppendLine("    {");
            sb.AppendLine("        using var db = CreateDb();");
            sb.AppendLine($"        var sut = new {e}DataAccess(db);");
            sb.AppendLine($"        var result = await sut.GetByIdAsync({pkDefault}, default);");
            sb.AppendLine("        Assert.Null(result);");
            sb.AppendLine("    }");
        }

        // CreateAsync persistence test (only if Post is enabled)
        if ((crud & CrudOperation.Post) != 0)
        {
            var positionalArgs = string.Join(", ", nonServerGen.Select(c => ValidPlaceholder(c)));

            sb.AppendLine();
            sb.AppendLine("    [Fact]");
            sb.AppendLine($"    public async Task CreateAsync_persists_and_returns_dto()");
            sb.AppendLine("    {");
            sb.AppendLine("        using var db = CreateDb();");
            sb.AppendLine($"        var sut = new {e}DataAccess(db);");
            sb.AppendLine($"        var command = new Create{e}Command({positionalArgs});");
            sb.AppendLine("        var dto = await sut.CreateAsync(command, default);");
            sb.AppendLine("        Assert.NotNull(dto);");
            sb.AppendLine("    }");
        }

        // GetPagedAsync empty-store test (only if GetList is enabled)
        if ((crud & CrudOperation.GetList) != 0)
        {
            sb.AppendLine();
            sb.AppendLine("    [Fact]");
            sb.AppendLine($"    public async Task GetPagedAsync_returns_empty_on_empty_store()");
            sb.AppendLine("    {");
            sb.AppendLine("        using var db = CreateDb();");
            sb.AppendLine($"        var sut = new {e}DataAccess(db);");
            sb.AppendLine("        var (items, total) = await sut.GetPagedAsync(1, 10, default);");
            sb.AppendLine("        Assert.Empty(items);");
            sb.AppendLine("        Assert.Equal(0, total);");
            sb.AppendLine("    }");
        }

        sb.AppendLine("}");

        return new EmittedFile($"{testsDir}/Data/{plural}/{e}DataAccessTests.cs", sb.ToString());
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
