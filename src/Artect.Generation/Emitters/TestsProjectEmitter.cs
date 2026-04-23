using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits the integration-tests project under <c>tests/&lt;Project&gt;.IntegrationTests/</c>.
/// Only runs when <c>cfg.IncludeTestsProject == true</c>.
///
/// Files emitted:
/// <list type="bullet">
///   <item><c>tests/&lt;Project&gt;.IntegrationTests/&lt;Project&gt;.IntegrationTests.csproj</c></item>
///   <item><c>tests/&lt;Project&gt;.IntegrationTests/TestWebApplicationFactory.cs</c></item>
///   <item>Per entity with PK (non-join, non-view):
///     <c>Validators/&lt;Entity&gt;ValidatorTests.cs</c> and,
///     when <c>DataAccess == EfCore</c>,
///     <c>Endpoints/&lt;Plural&gt;EndpointsTests.cs</c>.</item>
/// </list>
/// </summary>
public sealed class TestsProjectEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        if (!ctx.Config.IncludeTestsProject)
            return Array.Empty<EmittedFile>();

        var cfg     = ctx.Config;
        var project = cfg.ProjectName;
        var testsDir = CleanLayout.TestsDir(project);
        var testsName = CleanLayout.TestsProjectName(project);
        var testsNs  = testsName; // namespace == project name

        var list = new List<EmittedFile>();

        // ── .csproj ────────────────────────────────────────────────────────
        list.Add(new EmittedFile(
            $"{testsDir}/{testsName}.csproj",
            BuildCsproj(project, cfg.TargetFramework)));

        // ── TestWebApplicationFactory.cs ───────────────────────────────────
        list.Add(new EmittedFile(
            $"{testsDir}/TestWebApplicationFactory.cs",
            BuildFactory(project, testsNs, cfg.DataAccess)));

        // ── Per-entity test files ──────────────────────────────────────────
        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.IsJoinTable) continue;
            if (!entity.HasPrimaryKey) continue;

            var plural = entity.DbSetPropertyName;
            var route  = CasingHelper.ToKebabCase(plural);

            // Validator tests — always emitted (EfCore and Dapper)
            list.Add(new EmittedFile(
                $"{testsDir}/Validators/{entity.EntityTypeName}ValidatorTests.cs",
                BuildValidatorTest(entity, testsNs, project, cfg)));

            // Endpoint tests — EF Core only
            if (cfg.DataAccess == DataAccessKind.EfCore)
            {
                list.Add(new EmittedFile(
                    $"{testsDir}/Endpoints/{plural}EndpointsTests.cs",
                    BuildEndpointTest(entity, plural, route, testsNs, project, cfg)));
            }
        }

        return list;
    }

    // ── .csproj ────────────────────────────────────────────────────────────

    static string BuildCsproj(string project, TargetFramework tfm)
    {
        var moniker  = tfm.ToMoniker();
        var apiName  = CleanLayout.ApiProjectName(project);
        var apiRelPath = $"../../src/{apiName}/{apiName}.csproj";

        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        sb.AppendLine();
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine($"    <TargetFramework>{moniker}</TargetFramework>");
        sb.AppendLine("    <Nullable>enable</Nullable>");
        sb.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
        sb.AppendLine("    <IsPackable>false</IsPackable>");
        sb.AppendLine("    <LangVersion>12.0</LangVersion>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine();
        sb.AppendLine("  <ItemGroup>");
        sb.AppendLine("    <PackageReference Include=\"xunit\" Version=\"2.*\" />");
        sb.AppendLine("    <PackageReference Include=\"xunit.runner.visualstudio\" Version=\"2.*\">");
        sb.AppendLine("      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>");
        sb.AppendLine("      <PrivateAssets>all</PrivateAssets>");
        sb.AppendLine("    </PackageReference>");
        sb.AppendLine("    <PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.*\" />");
        sb.AppendLine("    <PackageReference Include=\"Microsoft.AspNetCore.Mvc.Testing\" Version=\"9.*\" />");
        sb.AppendLine("    <PackageReference Include=\"Microsoft.EntityFrameworkCore.InMemory\" Version=\"9.*\" />");
        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine();
        sb.AppendLine("  <ItemGroup>");
        sb.AppendLine($"    <ProjectReference Include=\"{apiRelPath}\" />");
        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine();
        sb.Append("</Project>");
        return sb.ToString();
    }

    // ── TestWebApplicationFactory.cs ───────────────────────────────────────

    static string BuildFactory(string project, string testsNs, DataAccessKind dataAccess)
    {
        var dbCtx   = $"{project}DbContext";
        var infraNs = CleanLayout.InfrastructureNamespace(project) + ".Data";

        if (dataAccess != DataAccessKind.EfCore)
        {
            return $$"""
                using Microsoft.AspNetCore.Mvc.Testing;

                namespace {{testsNs}};

                /// <summary>
                /// Minimal factory — Dapper tests would need a real DB, so the generated
                /// tests project currently exercises validators only when Dapper is chosen.
                /// </summary>
                public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
                {
                }
                """;
        }

        return $$"""
            using Microsoft.AspNetCore.Hosting;
            using Microsoft.AspNetCore.Mvc.Testing;
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.DependencyInjection;
            using {{infraNs}};

            namespace {{testsNs}};

            /// <summary>
            /// Replaces the real SQL Server DbContext with an in-memory one so the test
            /// server can be started without network access. Each test gets its own DB.
            /// </summary>
            public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
            {
                public string DatabaseName { get; } = "Tests-" + System.Guid.NewGuid().ToString("N")[..8];

                protected override void ConfigureWebHost(IWebHostBuilder builder)
                {
                    builder.ConfigureServices(services =>
                    {
                        var toRemove = services.Where(d => d.ServiceType == typeof(DbContextOptions<{{dbCtx}}>)).ToList();
                        foreach (var d in toRemove)
                        {
                            services.Remove(d);
                        }

                        services.AddDbContext<{{dbCtx}}>(opt => opt.UseInMemoryDatabase(DatabaseName));
                    });
                }
            }
            """;
    }

    // ── Validator test ─────────────────────────────────────────────────────

    static string BuildValidatorTest(NamedEntity entity, string testsNs, string project, ArtectConfig cfg)
    {
        var entityName  = entity.EntityTypeName;
        var requestsNs  = $"{CleanLayout.SharedNamespace(project)}.Requests";
        var validatorNs = $"{CleanLayout.ApplicationNamespace(project)}.Validators";

        var pkCols = entity.Table.PrimaryKey!.ColumnNames
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var (overrides, checkOverrides) = BuildMinimallyValidOverrides(entity, pkCols, cfg);
        var minimallyValidInit = BuildInitializer(overrides, skipPropertyName: null);

        var sbClass = new StringBuilder();

        // Rejects_null_request
        sbClass.Append("    [Fact]\n");
        sbClass.Append("    public void Rejects_null_request()\n");
        sbClass.Append("    {\n");
        sbClass.Append($"        var result = new {entityName}RequestValidators.Create{entityName}Validator().Validate(null!);\n");
        sbClass.Append("        Assert.False(result.IsValid);\n");
        sbClass.Append("    }\n");

        // Accepts_minimally_valid_request
        sbClass.Append("\n");
        sbClass.Append("    [Fact]\n");
        sbClass.Append("    public void Accepts_minimally_valid_request()\n");
        sbClass.Append("    {\n");
        sbClass.Append($"        var request = new Create{entityName}Request\n");
        sbClass.Append("        {\n");
        sbClass.Append(minimallyValidInit.Length == 0 ? "\n" : minimallyValidInit);
        sbClass.Append("        };\n");
        sbClass.Append("\n");
        sbClass.Append($"        var result = new {entityName}RequestValidators.Create{entityName}Validator().Validate(request);\n");
        sbClass.Append("        Assert.True(result.IsValid, string.Join(\"; \", result.Errors.Select(e => $\"{e.PropertyName}: {e.Message}\")));\n");
        sbClass.Append("    }\n");

        // Rejects_default_foreign_key (optional — only when ValidateForeignKeyReferences && there's a required FK)
        if (cfg.ValidateForeignKeyReferences)
        {
            var requiredFk = entity.ReferenceNavigations
                .Where(nav =>
                {
                    // Required = at least one FK column pair is non-nullable
                    var firstFkCol = nav.ColumnPairs.FirstOrDefault();
                    if (firstFkCol is null) return false;
                    var col = entity.Table.Columns.FirstOrDefault(c =>
                        string.Equals(c.Name, firstFkCol.FromColumn, StringComparison.OrdinalIgnoreCase));
                    return col is not null && !col.IsNullable;
                })
                .OrderBy(nav => nav.PropertyName, StringComparer.Ordinal)
                .FirstOrDefault();

            if (requiredFk is not null)
            {
                // The FK property name — find it on the entity via the column pair
                var fkColName = requiredFk.ColumnPairs.First().FromColumn;
                var fkCol     = entity.Table.Columns.FirstOrDefault(c =>
                    string.Equals(c.Name, fkColName, StringComparison.OrdinalIgnoreCase));
                var fkPropName = fkCol is not null ? EntityNaming.PropertyName(fkCol) : CasingHelper.ToPascalCase(fkColName);

                var fkInit = BuildInitializer(overrides, skipPropertyName: fkPropName);
                sbClass.Append("\n");
                sbClass.Append("    [Fact]\n");
                sbClass.Append("    public void Rejects_default_foreign_key()\n");
                sbClass.Append("    {\n");
                sbClass.Append($"        var request = new Create{entityName}Request\n");
                sbClass.Append("        {\n");
                sbClass.Append(fkInit);
                sbClass.Append("        };\n");
                sbClass.Append("\n");
                sbClass.Append($"        var result = new {entityName}RequestValidators.Create{entityName}Validator().Validate(request);\n");
                sbClass.Append("        Assert.False(result.IsValid);\n");
                sbClass.Append($"        Assert.Contains(result.Errors, e => e.PropertyName == \"{fkPropName}\");\n");
                sbClass.Append("    }\n");
            }
        }

        // Rejects_value_outside_check_constraint_{Col} — per translated CHECK, alphabetical
        foreach (var co in checkOverrides.OrderBy(c => c.PropertyName, StringComparer.Ordinal))
        {
            var violatingOverrides = overrides
                .Select(o => string.Equals(o.Property, co.PropertyName, StringComparison.Ordinal)
                    ? (o.Property, Value: co.ViolateLiteral)
                    : o)
                .ToList();
            var violatingInit = BuildInitializer(violatingOverrides, skipPropertyName: null);

            sbClass.Append("\n");
            sbClass.Append("    [Fact]\n");
            sbClass.Append($"    public void Rejects_value_outside_check_constraint_{co.PropertyName}()\n");
            sbClass.Append("    {\n");
            sbClass.Append($"        var request = new Create{entityName}Request\n");
            sbClass.Append("        {\n");
            sbClass.Append(violatingInit);
            sbClass.Append("        };\n");
            sbClass.Append("\n");
            sbClass.Append($"        var result = new {entityName}RequestValidators.Create{entityName}Validator().Validate(request);\n");
            sbClass.Append("        Assert.False(result.IsValid);\n");
            sbClass.Append($"        Assert.Contains(result.Errors, e => e.PropertyName == \"{co.PropertyName}\");\n");
            sbClass.Append("    }\n");
        }

        var result = new StringBuilder();
        result.Append($"using System.Linq;\n");
        result.Append($"using {requestsNs};\n");
        result.Append($"using {validatorNs};\n");
        result.Append("\n");
        result.Append($"namespace {testsNs}.Validators;\n");
        result.Append("\n");
        result.Append($"public sealed class {entityName}ValidatorTests\n");
        result.Append("{\n");
        result.Append(sbClass.ToString());
        result.Append("}");
        return result.ToString();
    }

    // ── Endpoint test ──────────────────────────────────────────────────────

    static string BuildEndpointTest(
        NamedEntity entity, string plural, string route,
        string testsNs, string project, ArtectConfig cfg)
    {
        var entityName  = entity.EntityTypeName;
        var requestsNs  = $"{CleanLayout.SharedNamespace(project)}.Requests";
        var crud        = cfg.Crud;

        var pkCols = entity.Table.PrimaryKey!.ColumnNames
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var needsPayload = (crud & (CrudOperation.Post | CrudOperation.Put | CrudOperation.Patch)) != 0;
        var (overrides, _) = BuildMinimallyValidOverrides(entity, pkCols, cfg);
        var initializerBody = needsPayload ? BuildInitializer(overrides, skipPropertyName: null) : string.Empty;

        var usings = new StringBuilder();
        usings.Append("using System.Net;\n");
        if (needsPayload)
        {
            usings.Append("using System.Net.Http.Json;\n");
            if (!string.Equals(requestsNs, testsNs + ".Endpoints", StringComparison.Ordinal))
                usings.Append($"using {requestsNs};\n");
        }

        var body = new StringBuilder();
        body.Append("    private readonly TestWebApplicationFactory _factory;\n");
        body.Append("\n");
        body.Append($"    public {plural}EndpointsTests(TestWebApplicationFactory factory)\n");
        body.Append("    {\n");
        body.Append("        _factory = factory;\n");
        body.Append("    }\n");

        if ((crud & CrudOperation.GetList) != 0)
        {
            body.Append("\n");
            body.Append("    [Fact]\n");
            body.Append("    public async System.Threading.Tasks.Task Get_list_returns_ok()\n");
            body.Append("    {\n");
            body.Append("        using var client = _factory.CreateClient();\n");
            body.Append($"        var response = await client.GetAsync(\"/api/{route}\").ConfigureAwait(false);\n");
            body.Append("        Assert.Equal(HttpStatusCode.OK, response.StatusCode);\n");
            body.Append("    }\n");
        }

        if ((crud & CrudOperation.GetById) != 0)
        {
            body.Append("\n");
            body.Append("    [Fact]\n");
            body.Append("    public async System.Threading.Tasks.Task Get_by_id_returns_404_when_missing()\n");
            body.Append("    {\n");
            body.Append("        using var client = _factory.CreateClient();\n");
            body.Append($"        var response = await client.GetAsync(\"/api/{route}/99999\").ConfigureAwait(false);\n");
            body.Append("        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);\n");
            body.Append("    }\n");
        }

        if ((crud & CrudOperation.Post) != 0)
        {
            body.Append("\n");
            body.Append("    [Fact]\n");
            body.Append("    public async System.Threading.Tasks.Task Post_returns_success_with_valid_payload()\n");
            body.Append("    {\n");
            body.Append("        using var client = _factory.CreateClient();\n");
            body.Append($"        var request = new Create{entityName}Request\n");
            body.Append("        {\n");
            body.Append(initializerBody.Length == 0 ? "\n" : initializerBody);
            body.Append("        };\n");
            body.Append($"        var response = await client.PostAsJsonAsync(\"/api/{route}\", request).ConfigureAwait(false);\n");
            body.Append("        Assert.True(response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.OK);\n");
            body.Append("    }\n");
        }

        if ((crud & CrudOperation.Put) != 0)
        {
            body.Append("\n");
            body.Append("    [Fact]\n");
            body.Append("    public async System.Threading.Tasks.Task Put_returns_404_when_id_missing()\n");
            body.Append("    {\n");
            body.Append("        using var client = _factory.CreateClient();\n");
            body.Append($"        var request = new Update{entityName}Request\n");
            body.Append("        {\n");
            body.Append(initializerBody.Length == 0 ? "\n" : initializerBody);
            body.Append("        };\n");
            body.Append($"        var response = await client.PutAsJsonAsync(\"/api/{route}/99999\", request).ConfigureAwait(false);\n");
            body.Append("        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);\n");
            body.Append("    }\n");
        }

        if ((crud & CrudOperation.Patch) != 0)
        {
            body.Append("\n");
            body.Append("    [Fact]\n");
            body.Append("    public async System.Threading.Tasks.Task Patch_returns_404_when_id_missing()\n");
            body.Append("    {\n");
            body.Append("        using var client = _factory.CreateClient();\n");
            body.Append($"        var request = new Update{entityName}Request\n");
            body.Append("        {\n");
            body.Append(initializerBody.Length == 0 ? "\n" : initializerBody);
            body.Append("        };\n");
            body.Append($"        var response = await client.PatchAsJsonAsync(\"/api/{route}/99999\", request).ConfigureAwait(false);\n");
            body.Append("        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);\n");
            body.Append("    }\n");
        }

        if ((crud & CrudOperation.Delete) != 0)
        {
            body.Append("\n");
            body.Append("    [Fact]\n");
            body.Append("    public async System.Threading.Tasks.Task Delete_returns_404_when_id_missing()\n");
            body.Append("    {\n");
            body.Append("        using var client = _factory.CreateClient();\n");
            body.Append($"        var response = await client.DeleteAsync(\"/api/{route}/99999\").ConfigureAwait(false);\n");
            body.Append("        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);\n");
            body.Append("    }\n");
        }

        var result = new StringBuilder();
        result.Append(usings);
        result.Append("\n");
        result.Append($"namespace {testsNs}.Endpoints;\n");
        result.Append("\n");
        result.Append($"public sealed class {plural}EndpointsTests : IClassFixture<TestWebApplicationFactory>\n");
        result.Append("{\n");
        result.Append(body);
        result.Append("}\n");
        return result.ToString();
    }

    // ── Shared helpers ─────────────────────────────────────────────────────

    private sealed record CheckOverride(string PropertyName, string PassLiteral, string ViolateLiteral);

    private static (List<(string Property, string Value)> Overrides, List<CheckOverride> CheckOverrides)
        BuildMinimallyValidOverrides(NamedEntity entity, HashSet<string> pkCols, ArtectConfig cfg)
    {
        var overrides = new List<(string Property, string Value)>();
        var overrideIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        void SetOverride(string property, string value)
        {
            if (overrideIndex.TryGetValue(property, out var idx))
                overrides[idx] = (property, value);
            else
            {
                overrideIndex[property] = overrides.Count;
                overrides.Add((property, value));
            }
        }

        // Required non-nullable strings → "x"
        foreach (var c in entity.Table.Columns)
        {
            // Skip server-generated PK columns
            if (pkCols.Contains(c.Name) && c.IsServerGenerated) continue;

            if (!c.IsNullable && c.ClrType == ClrType.String)
                SetOverride(EntityNaming.PropertyName(c), "\"x\"");
        }

        // Required FK columns → non-default literal (when ValidateForeignKeyReferences)
        if (cfg.ValidateForeignKeyReferences)
        {
            foreach (var fk in entity.Table.ForeignKeys)
            {
                foreach (var pair in fk.ColumnPairs)
                {
                    var col = entity.Table.Columns.FirstOrDefault(c =>
                        string.Equals(c.Name, pair.FromColumn, StringComparison.OrdinalIgnoreCase));
                    if (col is null || col.IsNullable) continue;
                    if (pkCols.Contains(col.Name) && col.IsServerGenerated) continue;

                    var propName = EntityNaming.PropertyName(col);
                    var literal  = NonDefaultLiteralFor(col.ClrType);
                    if (literal is not null)
                        SetOverride(propName, literal);
                }
            }
        }

        // Check constraint pass-values
        var checkOverrides = new List<CheckOverride>();
        if (entity.Table.CheckConstraints.Count > 0)
        {
            var propByCol = entity.Table.Columns
                .ToDictionary(c => c.Name, c => EntityNaming.PropertyName(c), StringComparer.OrdinalIgnoreCase);
            var seenProps = new HashSet<string>(StringComparer.Ordinal);

            foreach (var cc in entity.Table.CheckConstraints)
            {
                var (pass, violate, colName) = TryTranslateCheckConstraint(cc.Expression);
                if (colName is null) continue;

                if (!propByCol.TryGetValue(colName, out var propName)) continue;

                // Only integer columns
                var col = entity.Table.Columns.FirstOrDefault(c =>
                    string.Equals(c.Name, colName, StringComparison.OrdinalIgnoreCase));
                if (col is null || !IsIntegerClrType(col.ClrType)) continue;
                if (!seenProps.Add(propName)) continue;

                var passLit    = FormatIntegerLiteral(pass, col.ClrType);
                var violateLit = FormatIntegerLiteral(violate, col.ClrType);
                checkOverrides.Add(new CheckOverride(propName, passLit, violateLit));
                SetOverride(propName, passLit);
            }
        }

        return (overrides, checkOverrides);
    }

    static string BuildInitializer(
        IReadOnlyList<(string Property, string Value)> overrides,
        string? skipPropertyName)
    {
        var sb = new StringBuilder();
        foreach (var (property, value) in overrides)
        {
            if (skipPropertyName is not null &&
                string.Equals(property, skipPropertyName, StringComparison.Ordinal))
                continue;
            sb.Append($"            {property} = {value},\n");
        }
        return sb.ToString();
    }

    static string? NonDefaultLiteralFor(ClrType clr) => clr switch
    {
        ClrType.Int32 or ClrType.Int64 or ClrType.Int16 or ClrType.Byte => "1",
        ClrType.Guid => "new System.Guid(\"00000000-0000-0000-0000-000000000001\")",
        _ => null,
    };

    static bool IsIntegerClrType(ClrType clr) => clr is
        ClrType.Int32 or ClrType.Int64 or ClrType.Int16 or ClrType.Byte;

    static string FormatIntegerLiteral(long value, ClrType clr)
    {
        var s = value.ToString(CultureInfo.InvariantCulture);
        return clr == ClrType.Int64 ? s + "L" : s;
    }

    // ── Check constraint translation (mirrors ValidatorEmitter patterns) ───

    // Bare or bracket-quoted identifier.
    private const string IdentifierPat = @"\[?(?<col>[A-Za-z_][A-Za-z0-9_]*)\]?";
    // Signed integer, optionally wrapped in extra parens.
    private const string IntLiteralPat = @"\(*\s*(?<val>-?\d+)\s*\)*";

    private static readonly Regex ComparisonPattern = new(
        @"^\s*\(?\s*" + IdentifierPat + @"\s*(?<op>>=|<=|>|<)\s*" + IntLiteralPat + @"\s*\)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex BetweenPattern = new(
        @"^\s*\(?\s*" + IdentifierPat + @"\s+between\s+\(*\s*(?<lo>-?\d+)\s*\)*\s+and\s+\(*\s*(?<hi>-?\d+)\s*\)*\s*\)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Returns (passValue, violateValue, columnName) or (0, 0, null) if untranslatable.
    /// </summary>
    static (long Pass, long Violate, string? Column) TryTranslateCheckConstraint(string expression)
    {
        var m = ComparisonPattern.Match(expression);
        if (m.Success && long.TryParse(m.Groups["val"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cmpVal))
        {
            var col = m.Groups["col"].Value;
            var op  = m.Groups["op"].Value;
            // produce (pass, violate) pairs matching the constraint
            var (pass, violate) = op switch
            {
                ">=" => (cmpVal,     cmpVal - 1),
                ">"  => (cmpVal + 1, cmpVal),
                "<=" => (cmpVal,     cmpVal + 1),
                "<"  => (cmpVal - 1, cmpVal),
                _    => (0L, 0L),
            };
            return (pass, violate, col);
        }

        var mb = BetweenPattern.Match(expression);
        if (mb.Success &&
            long.TryParse(mb.Groups["lo"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lo) &&
            long.TryParse(mb.Groups["hi"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            var col = mb.Groups["col"].Value;
            return (lo, lo - 1, col);
        }

        return (0L, 0L, null);
    }
}
