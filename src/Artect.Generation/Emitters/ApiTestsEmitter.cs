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
/// Emits &lt;Project&gt;.Api.Tests project — WebApplicationFactory-based HTTP integration tests.
/// Emits one TestWebApplicationFactory and one endpoint test class per entity.
/// Gated by <c>cfg.IncludeTestsProject</c>.
/// EF path: swaps DbContext to InMemory.
/// Dapper path: emits a minimal factory with a TODO comment.
/// </summary>
public sealed class ApiTestsEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        if (!ctx.Config.IncludeTestsProject) return System.Array.Empty<EmittedFile>();

        var project = ctx.Config.ProjectName;
        var tfm = ctx.Config.TargetFramework.ToMoniker();
        var testProject = $"{project}.Api.Tests";
        var testsDir = $"tests/{testProject}";

        var list = new List<EmittedFile>
        {
            new EmittedFile($"{testsDir}/{testProject}.csproj", BuildCsproj(project, tfm)),
            new EmittedFile($"{testsDir}/TestWebApplicationFactory.cs",
                BuildFactory(project, testProject, ctx.Config.DataAccess)),
        };

        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.ShouldSkip(EntityClassification.AggregateRoot)) continue;

            var plural = entity.DbSetPropertyName;
            var route = CasingHelper.ToKebabCase(plural, ctx.NamingCorrections);

            list.Add(new EmittedFile(
                $"{testsDir}/Endpoints/{plural}EndpointsTests.cs",
                BuildEndpointTest(entity, plural, route, testProject, project, ctx.Config, ctx.NamingCorrections)));

            list.Add(new EmittedFile(
                $"{testsDir}/Mappings/{entity.EntityTypeName}MappingTests.cs",
                BuildMappingTest(entity, testProject, project, ctx.NamingCorrections)));
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
    <PackageReference Include=""Microsoft.AspNetCore.Mvc.Testing"" Version=""9.*"" />
    <PackageReference Include=""Microsoft.EntityFrameworkCore.InMemory"" Version=""9.*"" />
    <PackageReference Include=""FluentAssertions"" Version=""6.*"" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include=""../../src/{project}.Api/{project}.Api.csproj"" />
  </ItemGroup>
</Project>";

    static string BuildFactory(string project, string testsNs, DataAccessKind dataAccess)
    {
        var dbCtx = $"{project}DbContext";
        var infraNs = $"{CleanLayout.InfrastructureNamespace(project)}.Data";

        if (dataAccess != DataAccessKind.EfCore)
        {
            return $$"""
                using Microsoft.AspNetCore.Mvc.Testing;

                namespace {{testsNs}};

                /// <summary>
                /// Minimal factory — Dapper tests require a real DB, so this factory
                /// has no InMemory substitution. Wire up a test DB connection string
                /// in the environment or appsettings.Test.json if you want to run
                /// full integration tests.
                /// TODO: configure a real test DB connection here.
                /// </summary>
                public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
                {
                }
                """;
        }

        return $$"""
            using System.Linq;
            using Microsoft.AspNetCore.Hosting;
            using Microsoft.AspNetCore.Mvc.Testing;
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.DependencyInjection;
            using {{infraNs}};

            namespace {{testsNs}};

            /// <summary>
            /// Replaces the real SQL Server DbContext with an in-memory one so the test
            /// server can be started without network access. Each test gets its own DB name.
            /// </summary>
            public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
            {
                public string DatabaseName { get; } = "Tests-" + System.Guid.NewGuid().ToString("N")[..8];

                protected override void ConfigureWebHost(IWebHostBuilder builder)
                {
                    builder.ConfigureServices(services =>
                    {
                        var toRemove = services
                            .Where(d => d.ServiceType == typeof(DbContextOptions<{{dbCtx}}>))
                            .ToList();
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

    static string BuildEndpointTest(
        NamedEntity entity, string plural, string route,
        string testsNs, string project, ArtectConfig cfg,
        System.Collections.Generic.IReadOnlyDictionary<string, string> corrections)
    {
        var entityName = entity.EntityTypeName;
        var requestsNs = CleanLayout.SharedRequestsNamespace(project);
        var responsesNs = CleanLayout.SharedResponsesNamespace(project);
        var crud = cfg.Crud;

        var needsPayload = (crud & (CrudOperation.Post | CrudOperation.Put | CrudOperation.Patch)) != 0;

        var pkCols = entity.Table.PrimaryKey!.ColumnNames
            .ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        var nonPkNonGenCols = entity.Table.Columns
            .Where(c => !c.IsServerGenerated)
            .ToList();

        var overrides = BuildMinimallyValidOverrides(entity, pkCols, corrections);
        var initializerBody = needsPayload ? BuildInitializer(overrides) : string.Empty;

        // V#4: Put-doesn't-overwrite-protected test, gated on (>=1 visible
        // ProtectedFromUpdate column) AND single-PK AND Post + GetById + Put all in CRUD
        // (we need Post to seed, GetById to verify, Put to exercise).
        var protectedVisibleCols = entity.Table.Columns
            .Where(c => entity.ColumnHasFlag(c.Name, ColumnMetadata.ProtectedFromUpdate))
            .Where(c => !entity.ColumnHasFlag(c.Name, ColumnMetadata.Ignored))
            .Where(c => !entity.ColumnHasFlag(c.Name, ColumnMetadata.Sensitive))
            .ToList();
        var emitV4Test = protectedVisibleCols.Count > 0
            && entity.Table.PrimaryKey!.ColumnNames.Count == 1
            && (crud & CrudOperation.Post) != 0
            && (crud & CrudOperation.GetById) != 0
            && (crud & CrudOperation.Put) != 0;

        var usings = new StringBuilder();
        usings.AppendLine("using System.Net;");
        if (needsPayload || emitV4Test)
            usings.AppendLine("using System.Net.Http.Json;");
        if (needsPayload)
            usings.AppendLine($"using {requestsNs};");
        if (emitV4Test)
            usings.AppendLine($"using {responsesNs};");
        usings.AppendLine("using Xunit;");

        var body = new StringBuilder();
        body.AppendLine("    private readonly TestWebApplicationFactory _factory;");
        body.AppendLine();
        body.AppendLine($"    public {plural}EndpointsTests(TestWebApplicationFactory factory)");
        body.AppendLine("    {");
        body.AppendLine("        _factory = factory;");
        body.AppendLine("    }");

        if ((crud & CrudOperation.GetList) != 0)
        {
            body.AppendLine();
            body.AppendLine("    [Fact]");
            body.AppendLine($"    public async System.Threading.Tasks.Task Get_list_returns_200_with_empty_page()");
            body.AppendLine("    {");
            body.AppendLine("        using var client = _factory.CreateClient();");
            body.AppendLine($"        var response = await client.GetAsync(\"/api/{route}\").ConfigureAwait(false);");
            body.AppendLine("        Assert.Equal(HttpStatusCode.OK, response.StatusCode);");
            body.AppendLine("    }");
        }

        if ((crud & CrudOperation.GetById) != 0)
        {
            body.AppendLine();
            body.AppendLine("    [Fact]");
            body.AppendLine($"    public async System.Threading.Tasks.Task Get_by_id_returns_404_when_missing()");
            body.AppendLine("    {");
            body.AppendLine("        using var client = _factory.CreateClient();");
            body.AppendLine($"        var response = await client.GetAsync(\"/api/{route}/99999\").ConfigureAwait(false);");
            body.AppendLine("        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);");
            body.AppendLine("    }");
        }

        if ((crud & CrudOperation.Post) != 0)
        {
            body.AppendLine();
            body.AppendLine("    [Fact]");
            body.AppendLine($"    public async System.Threading.Tasks.Task Post_returns_201_or_200_with_created_body()");
            body.AppendLine("    {");
            body.AppendLine("        using var client = _factory.CreateClient();");
            body.AppendLine($"        var request = new Create{entityName}Request");
            body.AppendLine("        {");
            body.Append(initializerBody.Length == 0 ? "\n" : initializerBody);
            body.AppendLine("        };");
            body.AppendLine($"        var response = await client.PostAsJsonAsync(\"/api/{route}\", request).ConfigureAwait(false);");
            body.AppendLine("        Assert.True(response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.OK);");
            body.AppendLine("    }");
        }

        if ((crud & CrudOperation.Put) != 0)
        {
            body.AppendLine();
            body.AppendLine("    [Fact]");
            body.AppendLine($"    public async System.Threading.Tasks.Task Put_returns_404_when_id_missing()");
            body.AppendLine("    {");
            body.AppendLine("        using var client = _factory.CreateClient();");
            body.AppendLine($"        var request = new Update{entityName}Request");
            body.AppendLine("        {");
            body.Append(initializerBody.Length == 0 ? "\n" : initializerBody);
            body.AppendLine("        };");
            body.AppendLine($"        var response = await client.PutAsJsonAsync(\"/api/{route}/99999\", request).ConfigureAwait(false);");
            body.AppendLine("        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);");
            body.AppendLine("    }");
        }

        if (emitV4Test)
        {
            body.Append(BuildProtectedFieldsImmutabilityTest(entity, route, protectedVisibleCols, corrections));
        }

        if ((crud & CrudOperation.Delete) != 0)
        {
            body.AppendLine();
            body.AppendLine("    [Fact]");
            body.AppendLine($"    public async System.Threading.Tasks.Task Delete_returns_404_when_id_missing()");
            body.AppendLine("    {");
            body.AppendLine("        using var client = _factory.CreateClient();");
            body.AppendLine($"        var response = await client.DeleteAsync(\"/api/{route}/99999\").ConfigureAwait(false);");
            body.AppendLine("        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);");
            body.AppendLine("    }");
        }

        var result = new StringBuilder();
        result.Append(usings);
        result.AppendLine();
        result.AppendLine($"namespace {testsNs}.Endpoints;");
        result.AppendLine();
        result.AppendLine($"public sealed class {plural}EndpointsTests : IClassFixture<TestWebApplicationFactory>");
        result.AppendLine("{");
        result.Append(body);
        result.Append("}");

        return result.ToString();
    }

    static string BuildMappingTest(
        NamedEntity entity, string testsNs, string project,
        System.Collections.Generic.IReadOnlyDictionary<string, string> corrections)
    {
        var e = entity.EntityTypeName;
        var dtosNs = CleanLayout.ApplicationDtosNamespace(project);
        var apiMapNs = CleanLayout.ApiMappingsNamespace(project);

        // Pick the first non-server-generated scalar column as the assertion target.
        var assertCol = entity.Table.Columns
            .FirstOrDefault(c => !c.IsServerGenerated);
        var assertProp = assertCol is not null
            ? EntityNaming.PropertyName(assertCol, corrections)
            : null;

        // Build initializer for all non-server-generated columns.
        var initBody = string.Join(", ", entity.Table.Columns
            .Where(c => !c.IsServerGenerated)
            .Select(c => $"{EntityNaming.PropertyName(c, corrections)} = {ValidApiPlaceholder(c)}"));

        var sb = new StringBuilder();
        sb.AppendLine("using Xunit;");
        sb.AppendLine($"using {dtosNs};");
        sb.AppendLine($"using {apiMapNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {testsNs}.Mappings;");
        sb.AppendLine();
        sb.AppendLine($"public class {e}MappingTests");
        sb.AppendLine("{");
        sb.AppendLine("    [Fact]");
        sb.AppendLine($"    public void {e}Dto_ToResponse_copies_scalars()");
        sb.AppendLine("    {");
        sb.AppendLine($"        var dto = new {e}Dto {{ {initBody} }};");
        sb.AppendLine("        var response = dto.ToResponse();");
        if (assertProp is not null)
            sb.AppendLine($"        Assert.Equal(dto.{assertProp}, response.{assertProp});");
        else
            sb.AppendLine("        Assert.NotNull(response);");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static string ValidApiPlaceholder(Column c) => c.ClrType switch
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

    static List<(string Property, string Value)> BuildMinimallyValidOverrides(
        NamedEntity entity, System.Collections.Generic.HashSet<string> pkCols,
        System.Collections.Generic.IReadOnlyDictionary<string, string> corrections)
    {
        var overrides = new List<(string Property, string Value)>();
        var overrideIndex = new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.Ordinal);

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

        foreach (var c in entity.Table.Columns)
        {
            if (pkCols.Contains(c.Name) && c.IsServerGenerated) continue;
            if (!c.IsNullable && c.ClrType == ClrType.String)
                SetOverride(EntityNaming.PropertyName(c, corrections), "\"x\"");
        }

        return overrides;
    }

    static string BuildInitializer(System.Collections.Generic.List<(string Property, string Value)> overrides)
    {
        var sb = new StringBuilder();
        foreach (var (property, value) in overrides)
        {
            sb.AppendLine($"            {property} = {value},");
        }
        return sb.ToString();
    }

    /// <summary>
    /// V#4: per-entity test that proves columns flagged ProtectedFromUpdate cannot be
    /// overwritten via a PUT request, even when raw JSON contains them. Three layers of
    /// protection are exercised end-to-end: V#3's removal from the request DTO (extra
    /// JSON fields are dropped by the deserializer), V#2's omission from the entity's
    /// Update method (handler can't pass them), and V#4's EF
    /// PropertySaveBehavior.Ignore (final guard).
    /// </summary>
    static string BuildProtectedFieldsImmutabilityTest(
        NamedEntity entity, string route,
        IReadOnlyList<Column> protectedVisibleCols,
        IReadOnlyDictionary<string, string> corrections)
    {
        var entityName = entity.EntityTypeName;
        var pkCol = entity.Table.Columns.First(c =>
            string.Equals(c.Name, entity.Table.PrimaryKey!.ColumnNames[0], System.StringComparison.OrdinalIgnoreCase));
        var pkProp = EntityNaming.PropertyName(pkCol, corrections);
        var pkColNames = entity.Table.PrimaryKey!.ColumnNames
            .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("    [Fact]");
        sb.AppendLine($"    public async System.Threading.Tasks.Task Put_does_not_overwrite_protected_columns()");
        sb.AppendLine("    {");
        sb.AppendLine("        using var client = _factory.CreateClient();");
        sb.AppendLine();

        // Seed via POST with all required fields populated.
        sb.AppendLine($"        var createRequest = new Create{entityName}Request");
        sb.AppendLine("        {");
        foreach (var c in entity.Table.Columns)
        {
            if (entity.ColumnHasFlag(c.Name, ColumnMetadata.Ignored)) continue;
            if (pkColNames.Contains(c.Name) && c.IsServerGenerated) continue;
            sb.AppendLine($"            {EntityNaming.PropertyName(c, corrections)} = {ValidApiPlaceholder(c)},");
        }
        sb.AppendLine("        };");
        sb.AppendLine($"        var createResponse = await client.PostAsJsonAsync(\"/api/{route}\", createRequest).ConfigureAwait(false);");
        sb.AppendLine("        createResponse.EnsureSuccessStatusCode();");
        sb.AppendLine($"        var created = await createResponse.Content.ReadFromJsonAsync<{entityName}Response>().ConfigureAwait(false);");
        sb.AppendLine("        Assert.NotNull(created);");
        sb.AppendLine($"        var id = created!.{pkProp};");
        sb.AppendLine();

        // Capture initial protected values via GET.
        sb.AppendLine($"        var getInitial = await client.GetAsync($\"/api/{route}/{{id}}\").ConfigureAwait(false);");
        sb.AppendLine("        getInitial.EnsureSuccessStatusCode();");
        sb.AppendLine($"        var initial = await getInitial.Content.ReadFromJsonAsync<{entityName}Response>().ConfigureAwait(false);");
        sb.AppendLine("        Assert.NotNull(initial);");
        sb.AppendLine();

        // Build a valid Update request body, then inject extra JSON fields for the
        // protected columns at deliberately-different values.
        sb.AppendLine($"        var updateRequest = new Update{entityName}Request");
        sb.AppendLine("        {");
        sb.AppendLine($"            {pkProp} = id,");
        foreach (var c in entity.UpdateableColumns())
        {
            sb.AppendLine($"            {EntityNaming.PropertyName(c, corrections)} = {ValidApiPlaceholder(c)},");
        }
        sb.AppendLine("        };");
        sb.AppendLine("        var jsonNode = (System.Text.Json.Nodes.JsonObject)System.Text.Json.JsonSerializer.SerializeToNode(updateRequest)!;");
        foreach (var c in protectedVisibleCols)
        {
            var prop = EntityNaming.PropertyName(c, corrections);
            sb.AppendLine($"        jsonNode[\"{prop}\"] = {DistinctJsonValue(c)};");
        }
        sb.AppendLine("        var putContent = new System.Net.Http.StringContent(jsonNode.ToJsonString(), System.Text.Encoding.UTF8, \"application/json\");");
        sb.AppendLine($"        var putResponse = await client.PutAsync($\"/api/{route}/{{id}}\", putContent).ConfigureAwait(false);");
        sb.AppendLine("        putResponse.EnsureSuccessStatusCode();");
        sb.AppendLine();

        // GET again; assert each protected column matches its initial value.
        sb.AppendLine($"        var getAfter = await client.GetAsync($\"/api/{route}/{{id}}\").ConfigureAwait(false);");
        sb.AppendLine("        getAfter.EnsureSuccessStatusCode();");
        sb.AppendLine($"        var actual = await getAfter.Content.ReadFromJsonAsync<{entityName}Response>().ConfigureAwait(false);");
        sb.AppendLine("        Assert.NotNull(actual);");
        foreach (var c in protectedVisibleCols)
        {
            var prop = EntityNaming.PropertyName(c, corrections);
            sb.AppendLine($"        Assert.Equal(initial!.{prop}, actual!.{prop});");
        }
        sb.AppendLine("    }");
        return sb.ToString();
    }

    /// <summary>
    /// Produces a C# expression whose runtime value is JSON-serializable to a value
    /// distinct from <see cref="ValidApiPlaceholder"/>. Used by V#4's protected-fields
    /// test to inject "obviously different" values via JsonNode so any failure to
    /// preserve the original is visible in the diff.
    /// </summary>
    static string DistinctJsonValue(Column c) => c.ClrType switch
    {
        ClrType.String         => "\"OVERWRITE_ATTEMPT\"",
        ClrType.Int32          => "99999",
        ClrType.Int64          => "99999L",
        ClrType.Int16          => "(short)9999",
        ClrType.Byte           => "(byte)99",
        ClrType.Boolean        => "true",
        ClrType.Decimal        => "99999m",
        ClrType.Double         => "99999.0",
        ClrType.Single         => "99999.0f",
        ClrType.DateTime       => "\"2099-12-31T23:59:59Z\"",
        ClrType.DateTimeOffset => "\"2099-12-31T23:59:59+00:00\"",
        ClrType.Guid           => "\"00000000-0000-0000-0000-999999999999\"",
        ClrType.ByteArray      => "\"AAAA\"",
        _                      => "null",
    };
}
