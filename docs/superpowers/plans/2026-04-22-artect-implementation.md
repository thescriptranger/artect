# Artect Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build Artect, a .NET global tool that scaffolds a Clean Architecture + Minimal API solution from a SQL Server database, as specified in `docs/superpowers/specs/2026-04-22-artect-design.md`.

**Architecture:** Nine-project solution (Cli, Core, Console, Introspection, Naming, Templating, Generation, Templates, Config) mirroring ApiSmith's Clean-Architecture slice. Greenfield but faithful to ApiSmith shapes at `C:\Users\Art\Nextcloud\Art-Work\Projects\apismith-v1`. Hand-rolled everywhere: SQL introspection via `INFORMATION_SCHEMA`/`sys.*`; Handlebars-style `.artect` template DSL; YAML reader/writer; zero reflection, zero source generators.

**Tech Stack:** C# 12, .NET 9 SDK, `Microsoft.Data.SqlClient` (tool-side only). Output targets .NET 8/9 and uses EF Core or Dapper. No unit tests against the tool in V1 — validation is manual by running the tool.

**Validation posture:** Every phase ends with `dotnet build` green. The user smoke-tests the wizard + generated output against a throwaway SQL Server DB after Phase 11. No `dotnet test` in V1 (see spec §13).

---

## Phase 0 — Repo bootstrap

**Goal:** Empty solution + nine csprojs with correct reference graph, `dotnet build` green, first commit.

### Task 0.1: Create solution files

**Files:**
- Create: `Artect.sln`
- Create: `global.json`
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `NuGet.config`

- [ ] **Step 1: Create `global.json` pinning the SDK band**

```json
{
  "sdk": {
    "version": "9.0.100",
    "rollForward": "latestMinor"
  }
}
```

- [ ] **Step 2: Create `Directory.Build.props` with shared project settings**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <LangVersion>12.0</LangVersion>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <ImplicitUsings>enable</ImplicitUsings>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Create `Directory.Packages.props` with a single pinned dep**

```xml
<Project>
  <ItemGroup>
    <PackageVersion Include="Microsoft.Data.SqlClient" Version="5.2.2" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Create `NuGet.config` restricted to nuget.org**

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

- [ ] **Step 5: Create an empty `Artect.sln`**

Run: `dotnet new sln -n Artect`
Expected: `Artect.sln` created at repo root.

### Task 0.2: Create the nine projects

**Files:**
- Create: `src/Artect.Core/Artect.Core.csproj`
- Create: `src/Artect.Naming/Artect.Naming.csproj`
- Create: `src/Artect.Templating/Artect.Templating.csproj`
- Create: `src/Artect.Templates/Artect.Templates.csproj`
- Create: `src/Artect.Config/Artect.Config.csproj`
- Create: `src/Artect.Introspection/Artect.Introspection.csproj`
- Create: `src/Artect.Generation/Artect.Generation.csproj`
- Create: `src/Artect.Console/Artect.Console.csproj`
- Create: `src/Artect.Cli/Artect.Cli.csproj`

- [ ] **Step 1: Create each class-library csproj with zero package refs**

For each of `Artect.Core`, `Artect.Naming`, `Artect.Templating`, `Artect.Templates`, `Artect.Config`, `Artect.Generation`, `Artect.Console` — write a minimal csproj body:

```xml
<Project Sdk="Microsoft.NET.Sdk">
</Project>
```

- [ ] **Step 2: Write `src/Artect.Introspection/Artect.Introspection.csproj` with SqlClient**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Microsoft.Data.SqlClient" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Write `src/Artect.Cli/Artect.Cli.csproj` as a packable global tool**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>artect</ToolCommandName>
    <PackageId>Artect</PackageId>
    <Version>1.0.0</Version>
    <Authors>Art Laubach</Authors>
    <Description>Scaffolding CLI for Clean Architecture + Minimal API .NET solutions from SQL Server databases.</Description>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <RootNamespace>Artect.Cli</RootNamespace>
    <AssemblyName>artect</AssemblyName>
  </PropertyGroup>
</Project>
```

- [ ] **Step 4: Set project references (spec §4)**

Run these from the repo root. Each adds a `<ProjectReference>` to the target's csproj:

```bash
dotnet sln add src/Artect.Core/Artect.Core.csproj \
                src/Artect.Naming/Artect.Naming.csproj \
                src/Artect.Templating/Artect.Templating.csproj \
                src/Artect.Templates/Artect.Templates.csproj \
                src/Artect.Config/Artect.Config.csproj \
                src/Artect.Introspection/Artect.Introspection.csproj \
                src/Artect.Generation/Artect.Generation.csproj \
                src/Artect.Console/Artect.Console.csproj \
                src/Artect.Cli/Artect.Cli.csproj

# Naming -> Core
dotnet add src/Artect.Naming/Artect.Naming.csproj reference src/Artect.Core/Artect.Core.csproj
# Templating -> (no refs)
# Templates -> Templating
dotnet add src/Artect.Templates/Artect.Templates.csproj reference src/Artect.Templating/Artect.Templating.csproj
# Config -> Core
dotnet add src/Artect.Config/Artect.Config.csproj reference src/Artect.Core/Artect.Core.csproj
# Introspection -> Core, Naming
dotnet add src/Artect.Introspection/Artect.Introspection.csproj reference src/Artect.Core/Artect.Core.csproj
dotnet add src/Artect.Introspection/Artect.Introspection.csproj reference src/Artect.Naming/Artect.Naming.csproj
# Generation -> Core, Naming, Templating, Templates, Config
dotnet add src/Artect.Generation/Artect.Generation.csproj reference src/Artect.Core/Artect.Core.csproj
dotnet add src/Artect.Generation/Artect.Generation.csproj reference src/Artect.Naming/Artect.Naming.csproj
dotnet add src/Artect.Generation/Artect.Generation.csproj reference src/Artect.Templating/Artect.Templating.csproj
dotnet add src/Artect.Generation/Artect.Generation.csproj reference src/Artect.Templates/Artect.Templates.csproj
dotnet add src/Artect.Generation/Artect.Generation.csproj reference src/Artect.Config/Artect.Config.csproj
# Console -> Core, Config
dotnet add src/Artect.Console/Artect.Console.csproj reference src/Artect.Core/Artect.Core.csproj
dotnet add src/Artect.Console/Artect.Console.csproj reference src/Artect.Config/Artect.Config.csproj
# Cli -> Console, Config, Generation, Introspection
dotnet add src/Artect.Cli/Artect.Cli.csproj reference src/Artect.Console/Artect.Console.csproj
dotnet add src/Artect.Cli/Artect.Cli.csproj reference src/Artect.Config/Artect.Config.csproj
dotnet add src/Artect.Cli/Artect.Cli.csproj reference src/Artect.Generation/Artect.Generation.csproj
dotnet add src/Artect.Cli/Artect.Cli.csproj reference src/Artect.Introspection/Artect.Introspection.csproj
```

- [ ] **Step 5: Add a stub `Program.cs` to `Artect.Cli`**

`src/Artect.Cli/Program.cs`:
```csharp
namespace Artect.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        System.Console.WriteLine("artect placeholder — Phase 0 scaffold only.");
        return 0;
    }
}
```

- [ ] **Step 6: Verify build**

Run: `dotnet build Artect.sln -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add -- Artect.sln global.json Directory.Build.props Directory.Packages.props NuGet.config src/
git commit -m "chore(scaffold): bootstrap nine-project Artect solution"
```

---

## Phase 1 — Schema model (Artect.Core)

**Goal:** Immutable `SchemaGraph` and child records. No behavior yet — just the shape that every later phase consumes.

### Task 1.1: Schema enum primitives

**Files:**
- Create: `src/Artect.Core/Schema/ClrType.cs`
- Create: `src/Artect.Core/Schema/ForeignKeyAction.cs`
- Create: `src/Artect.Core/Schema/ResultInferenceStatus.cs`

- [ ] **Step 1: Write `ClrType.cs`**

```csharp
namespace Artect.Core.Schema;

public enum ClrType
{
    String, Int16, Int32, Int64, Byte, Boolean, Decimal, Double, Single,
    DateTime, DateTimeOffset, DateOnly, TimeOnly, Guid, ByteArray, Object
}
```

- [ ] **Step 2: Write `ForeignKeyAction.cs`**

```csharp
namespace Artect.Core.Schema;

public enum ForeignKeyAction { NoAction, Cascade, SetNull, SetDefault }
```

- [ ] **Step 3: Write `ResultInferenceStatus.cs`**

```csharp
namespace Artect.Core.Schema;

public enum ResultInferenceStatus { Resolved, Indeterminate, Empty }
```

### Task 1.2: `Column` and `Table`

**Files:**
- Create: `src/Artect.Core/Schema/Column.cs`
- Create: `src/Artect.Core/Schema/PrimaryKey.cs`
- Create: `src/Artect.Core/Schema/Table.cs`

- [ ] **Step 1: Write `Column.cs`**

```csharp
namespace Artect.Core.Schema;

public sealed record Column(
    string Name,
    int OrdinalPosition,
    string SqlType,
    ClrType ClrType,
    bool IsNullable,
    bool IsIdentity,
    bool IsComputed,
    bool IsServerGenerated,
    int? MaxLength,
    int? Precision,
    int? Scale,
    string? DefaultValue);
```

- [ ] **Step 2: Write `PrimaryKey.cs`**

```csharp
namespace Artect.Core.Schema;

public sealed record PrimaryKey(string Name, System.Collections.Generic.IReadOnlyList<string> ColumnNames);
```

- [ ] **Step 3: Write `Table.cs`**

```csharp
using System.Collections.Generic;

namespace Artect.Core.Schema;

public sealed record Table(
    string Schema,
    string Name,
    IReadOnlyList<Column> Columns,
    PrimaryKey? PrimaryKey,
    IReadOnlyList<ForeignKey> ForeignKeys,
    IReadOnlyList<UniqueConstraint> UniqueConstraints,
    IReadOnlyList<Index> Indexes,
    IReadOnlyList<CheckConstraint> CheckConstraints)
{
    public string QualifiedName => $"{Schema}.{Name}";
}
```

### Task 1.3: Constraints, indexes, views, routines

**Files:**
- Create: `src/Artect.Core/Schema/ForeignKey.cs`
- Create: `src/Artect.Core/Schema/ForeignKeyColumnPair.cs`
- Create: `src/Artect.Core/Schema/UniqueConstraint.cs`
- Create: `src/Artect.Core/Schema/Index.cs`
- Create: `src/Artect.Core/Schema/CheckConstraint.cs`
- Create: `src/Artect.Core/Schema/Sequence.cs`
- Create: `src/Artect.Core/Schema/View.cs`
- Create: `src/Artect.Core/Schema/StoredProcedure.cs`
- Create: `src/Artect.Core/Schema/StoredProcedureParameter.cs`
- Create: `src/Artect.Core/Schema/StoredProcedureResultColumn.cs`
- Create: `src/Artect.Core/Schema/Function.cs`
- Create: `src/Artect.Core/Schema/FunctionParameter.cs`

- [ ] **Step 1: Write `ForeignKeyColumnPair.cs`**

```csharp
namespace Artect.Core.Schema;

public sealed record ForeignKeyColumnPair(string FromColumn, string ToColumn);
```

- [ ] **Step 2: Write `ForeignKey.cs`**

```csharp
using System.Collections.Generic;

namespace Artect.Core.Schema;

public sealed record ForeignKey(
    string Name,
    string FromSchema,
    string FromTable,
    string ToSchema,
    string ToTable,
    IReadOnlyList<ForeignKeyColumnPair> ColumnPairs,
    ForeignKeyAction OnDelete,
    ForeignKeyAction OnUpdate);
```

- [ ] **Step 3: Write `UniqueConstraint.cs`**

```csharp
using System.Collections.Generic;

namespace Artect.Core.Schema;

public sealed record UniqueConstraint(string Name, IReadOnlyList<string> ColumnNames);
```

- [ ] **Step 4: Write `Index.cs`**

```csharp
using System.Collections.Generic;

namespace Artect.Core.Schema;

public sealed record Index(
    string Name,
    bool IsUnique,
    bool IsClustered,
    IReadOnlyList<string> KeyColumns,
    IReadOnlyList<string> IncludedColumns);
```

- [ ] **Step 5: Write `CheckConstraint.cs`**

```csharp
namespace Artect.Core.Schema;

public sealed record CheckConstraint(string Name, string TableName, string TableSchema, string? ColumnName, string Expression);
```

- [ ] **Step 6: Write `Sequence.cs`**

```csharp
namespace Artect.Core.Schema;

public sealed record Sequence(
    string Schema,
    string Name,
    string SqlType,
    long StartValue,
    long Increment,
    long? MinValue,
    long? MaxValue,
    bool IsCycling);
```

- [ ] **Step 7: Write `View.cs`**

```csharp
using System.Collections.Generic;

namespace Artect.Core.Schema;

public sealed record View(string Schema, string Name, IReadOnlyList<Column> Columns);
```

- [ ] **Step 8: Write `StoredProcedureParameter.cs`, `StoredProcedureResultColumn.cs`, `StoredProcedure.cs`**

```csharp
namespace Artect.Core.Schema;

public sealed record StoredProcedureParameter(
    string Name, int Ordinal, string SqlType, ClrType ClrType, bool IsNullable,
    bool IsOutput, int? MaxLength, int? Precision, int? Scale);
```

```csharp
namespace Artect.Core.Schema;

public sealed record StoredProcedureResultColumn(
    string Name, int Ordinal, string SqlType, ClrType ClrType, bool IsNullable);
```

```csharp
using System.Collections.Generic;

namespace Artect.Core.Schema;

public sealed record StoredProcedure(
    string Schema,
    string Name,
    IReadOnlyList<StoredProcedureParameter> Parameters,
    IReadOnlyList<StoredProcedureResultColumn> ResultColumns,
    ResultInferenceStatus ResultInference);
```

- [ ] **Step 9: Write `FunctionParameter.cs` and `Function.cs`**

```csharp
namespace Artect.Core.Schema;

public enum FunctionReturnKind { Scalar, Table, Inline }

public sealed record FunctionParameter(
    string Name, int Ordinal, string SqlType, ClrType ClrType, bool IsNullable);
```

```csharp
using System.Collections.Generic;

namespace Artect.Core.Schema;

public sealed record Function(
    string Schema,
    string Name,
    FunctionReturnKind ReturnKind,
    string? ReturnSqlType,
    ClrType? ReturnClrType,
    IReadOnlyList<FunctionParameter> Parameters,
    IReadOnlyList<Column> ResultColumns);
```

### Task 1.4: `SchemaGraph` root and `SqlTypeMap`

**Files:**
- Create: `src/Artect.Core/Schema/SchemaGraph.cs`
- Create: `src/Artect.Core/Schema/SqlTypeMap.cs`

- [ ] **Step 1: Write `SchemaGraph.cs`**

```csharp
using System.Collections.Generic;

namespace Artect.Core.Schema;

public sealed record SchemaGraph(
    IReadOnlyList<string> Schemas,
    IReadOnlyList<Table> Tables,
    IReadOnlyList<View> Views,
    IReadOnlyList<Sequence> Sequences,
    IReadOnlyList<StoredProcedure> StoredProcedures,
    IReadOnlyList<Function> Functions);
```

- [ ] **Step 2: Write `SqlTypeMap.cs` — single source of truth for SQL → CLR mapping**

```csharp
using System;

namespace Artect.Core.Schema;

public static class SqlTypeMap
{
    public static ClrType ToClr(string sqlType) => sqlType.ToLowerInvariant() switch
    {
        "bigint" => ClrType.Int64,
        "int" => ClrType.Int32,
        "smallint" => ClrType.Int16,
        "tinyint" => ClrType.Byte,
        "bit" => ClrType.Boolean,
        "decimal" or "numeric" or "money" or "smallmoney" => ClrType.Decimal,
        "float" => ClrType.Double,
        "real" => ClrType.Single,
        "datetime" or "datetime2" or "smalldatetime" => ClrType.DateTime,
        "datetimeoffset" => ClrType.DateTimeOffset,
        "date" => ClrType.DateOnly,
        "time" => ClrType.TimeOnly,
        "uniqueidentifier" => ClrType.Guid,
        "binary" or "varbinary" or "image" or "rowversion" or "timestamp" => ClrType.ByteArray,
        "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext" or "xml" => ClrType.String,
        _ => ClrType.Object,
    };

    public static string ToCs(ClrType clr) => clr switch
    {
        ClrType.String => "string",
        ClrType.Int16 => "short",
        ClrType.Int32 => "int",
        ClrType.Int64 => "long",
        ClrType.Byte => "byte",
        ClrType.Boolean => "bool",
        ClrType.Decimal => "decimal",
        ClrType.Double => "double",
        ClrType.Single => "float",
        ClrType.DateTime => "System.DateTime",
        ClrType.DateTimeOffset => "System.DateTimeOffset",
        ClrType.DateOnly => "System.DateOnly",
        ClrType.TimeOnly => "System.TimeOnly",
        ClrType.Guid => "System.Guid",
        ClrType.ByteArray => "byte[]",
        ClrType.Object or _ => "object",
    };

    public static bool IsValueType(ClrType clr) => clr is not (ClrType.String or ClrType.ByteArray or ClrType.Object);
}
```

### Task 1.5: Compile + commit Phase 1

- [ ] **Step 1: Build**

Run: `dotnet build Artect.sln -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 2: Commit**

```bash
git add -- src/Artect.Core/
git commit -m "feat(core): add SchemaGraph model and SqlTypeMap"
```

---

## Phase 2 — Naming (Artect.Naming)

**Goal:** Casing, pluralization, schema segmentation, `DbSetNaming` collision handling, `JoinTableDetector`.

### Task 2.1: Casing helpers

**Files:**
- Create: `src/Artect.Naming/CasingHelper.cs`

- [ ] **Step 1: Write `CasingHelper.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Artect.Naming;

public static class CasingHelper
{
    public static string ToPascalCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var words = SplitWords(input);
        var sb = new StringBuilder(input.Length);
        foreach (var w in words) sb.Append(Capitalize(w));
        return sb.ToString();
    }

    public static string ToCamelCase(string input)
    {
        var pascal = ToPascalCase(input);
        if (pascal.Length == 0) return pascal;
        return char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }

    public static string ToKebabCase(string input) => JoinWords(SplitWords(input), '-');
    public static string ToSnakeCase(string input) => JoinWords(SplitWords(input), '_');

    static IReadOnlyList<string> SplitWords(string input)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c == '_' || c == '-' || c == ' ')
            {
                if (current.Length > 0) { words.Add(current.ToString()); current.Clear(); }
                continue;
            }
            if (i > 0 && char.IsUpper(c))
            {
                char prev = input[i - 1];
                bool prevLower = char.IsLower(prev);
                bool prevDigit = char.IsDigit(prev);
                bool nextLower = i + 1 < input.Length && char.IsLower(input[i + 1]);
                bool prevUpper = char.IsUpper(prev);
                if (prevLower || prevDigit || (prevUpper && nextLower))
                {
                    if (current.Length > 0) { words.Add(current.ToString()); current.Clear(); }
                }
            }
            if (i > 0 && char.IsDigit(c) && !char.IsDigit(input[i - 1]))
            {
                if (current.Length > 0) { words.Add(current.ToString()); current.Clear(); }
            }
            current.Append(c);
        }
        if (current.Length > 0) words.Add(current.ToString());
        return words;
    }

    static string JoinWords(IReadOnlyList<string> words, char sep)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < words.Count; i++)
        {
            if (i > 0) sb.Append(sep);
            sb.Append(words[i].ToLowerInvariant());
        }
        return sb.ToString();
    }

    static string Capitalize(string w) =>
        w.Length == 0 ? w :
        w.Length == 1 ? w.ToUpperInvariant() :
        char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant();
}
```

### Task 2.2: Pluralizer

**Files:**
- Create: `src/Artect.Naming/Pluralizer.cs`

- [ ] **Step 1: Write a deterministic rule-based pluralizer**

```csharp
using System;
using System.Collections.Generic;

namespace Artect.Naming;

public static class Pluralizer
{
    static readonly HashSet<string> Uncountable = new(StringComparer.OrdinalIgnoreCase)
    {
        "equipment","information","rice","money","species","series","fish","sheep","news","data"
    };
    static readonly Dictionary<string,string> Irregular = new(StringComparer.OrdinalIgnoreCase)
    {
        ["person"]="people",["man"]="men",["woman"]="women",["child"]="children",
        ["tooth"]="teeth",["foot"]="feet",["mouse"]="mice",["goose"]="geese"
    };

    public static string Pluralize(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;
        if (Uncountable.Contains(word)) return word;
        if (Irregular.TryGetValue(word, out var irr)) return PreserveCase(word, irr);
        var lower = word.ToLowerInvariant();
        string plural =
            lower.EndsWith("s") || lower.EndsWith("x") || lower.EndsWith("z") || lower.EndsWith("ch") || lower.EndsWith("sh") ? word + "es" :
            (lower.EndsWith("y") && word.Length >= 2 && !"aeiou".Contains(word[^2])) ? word[..^1] + "ies" :
            lower.EndsWith("f") ? word[..^1] + "ves" :
            lower.EndsWith("fe") ? word[..^2] + "ves" :
            word + "s";
        return plural;
    }

    public static string Singularize(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;
        if (Uncountable.Contains(word)) return word;
        foreach (var pair in Irregular)
            if (string.Equals(pair.Value, word, StringComparison.OrdinalIgnoreCase))
                return PreserveCase(word, pair.Key);
        var lower = word.ToLowerInvariant();
        if (lower.EndsWith("ies") && word.Length > 3) return word[..^3] + "y";
        if (lower.EndsWith("ves")) return word[..^3] + "f";
        if (lower.EndsWith("ses") || lower.EndsWith("xes") || lower.EndsWith("zes") || lower.EndsWith("ches") || lower.EndsWith("shes"))
            return word[..^2];
        if (lower.EndsWith("s") && !lower.EndsWith("ss")) return word[..^1];
        return word;
    }

    static string PreserveCase(string source, string replacement) =>
        char.IsUpper(source[0]) ? char.ToUpperInvariant(replacement[0]) + replacement[1..] : replacement;
}
```

### Task 2.3: Entity-name helpers

**Files:**
- Create: `src/Artect.Naming/EntityNaming.cs`

- [ ] **Step 1: Write `EntityNaming.cs` — converts a table name to entity / property names**

```csharp
using Artect.Core.Schema;

namespace Artect.Naming;

public static class EntityNaming
{
    public static string EntityClassName(Table t) => CasingHelper.ToPascalCase(Pluralizer.Singularize(t.Name));
    public static string EntityPluralName(Table t) => CasingHelper.ToPascalCase(Pluralizer.Pluralize(Pluralizer.Singularize(t.Name)));
    public static string PropertyName(Column c)
    {
        var pascal = CasingHelper.ToPascalCase(c.Name);
        // strip trailing "Id" duplication like Foo_FooId -> Foo_FooId stays; but expose "Id" -> "Id"
        return pascal;
    }
    public static string NavigationPropertyName(string targetEntity, bool collection) =>
        collection ? CasingHelper.ToPascalCase(Pluralizer.Pluralize(targetEntity)) : CasingHelper.ToPascalCase(targetEntity);
}
```

### Task 2.4: `DbSetNaming` — cross-schema collision handling

**Files:**
- Create: `src/Artect.Naming/DbSetNaming.cs`

- [ ] **Step 1: Write `DbSetNaming.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using Artect.Core.Schema;

namespace Artect.Naming;

public sealed class DbSetNaming
{
    readonly Dictionary<(string Schema, string Name), string> _dbSetByTable = new();
    readonly Dictionary<(string Schema, string Name), string> _entityByTable = new();
    public IReadOnlyDictionary<(string, string), string> DbSetNames => _dbSetByTable;
    public IReadOnlyDictionary<(string, string), string> EntityTypeNames => _entityByTable;

    public static DbSetNaming Build(SchemaGraph graph)
    {
        var result = new DbSetNaming();
        var baseEntity = graph.Tables.ToDictionary(t => (t.Schema, t.Name), t => EntityNaming.EntityClassName(t));
        var collisions = baseEntity.Values.GroupBy(v => v).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        foreach (var t in graph.Tables)
        {
            var basePluralSet = CasingHelper.ToPascalCase(Pluralizer.Pluralize(baseEntity[(t.Schema, t.Name)]));
            var entityName = baseEntity[(t.Schema, t.Name)];
            if (collisions.Contains(entityName))
            {
                var schemaPrefix = CasingHelper.ToPascalCase(t.Schema);
                result._dbSetByTable[(t.Schema, t.Name)] = schemaPrefix + basePluralSet;
                result._entityByTable[(t.Schema, t.Name)] = schemaPrefix + entityName;
            }
            else
            {
                result._dbSetByTable[(t.Schema, t.Name)] = basePluralSet;
                result._entityByTable[(t.Schema, t.Name)] = entityName;
            }
        }
        return result;
    }
}
```

### Task 2.5: `JoinTableDetector`

**Files:**
- Create: `src/Artect.Naming/JoinTableDetector.cs`

- [ ] **Step 1: Write `JoinTableDetector.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using Artect.Core.Schema;

namespace Artect.Naming;

public static class JoinTableDetector
{
    public static bool IsJoinTable(Table t)
    {
        if (t.PrimaryKey is null) return false;
        if (t.Columns.Count < 2) return false;
        var fkCols = t.ForeignKeys.SelectMany(fk => fk.ColumnPairs.Select(p => p.FromColumn)).ToHashSet();
        var nonFkCols = t.Columns.Where(c => !fkCols.Contains(c.Name)).ToList();
        return t.ForeignKeys.Count >= 2 && nonFkCols.Count == 0;
    }

    public static IReadOnlyList<Table> NonJoinTables(SchemaGraph graph) =>
        graph.Tables.Where(t => !IsJoinTable(t)).ToList();
}
```

### Task 2.6: Compile + commit Phase 2

- [ ] **Step 1: Build**

Run: `dotnet build Artect.sln`
Expected: green.

- [ ] **Step 2: Commit**

```bash
git add -- src/Artect.Naming/
git commit -m "feat(naming): casing, pluralization, DbSet naming, join detection"
```

---
## Phase 3 — Templating DSL (Artect.Templating)

**Goal:** Tokenizer, AST, Parser, Renderer, Filters, and a `TemplateLoader` abstraction. Zero external deps.

### Task 3.1: Token types

**Files:**
- Create: `src/Artect.Templating/Tokens/TokenKind.cs`
- Create: `src/Artect.Templating/Tokens/Token.cs`

- [ ] **Step 1: `TokenKind.cs`**

```csharp
namespace Artect.Templating.Tokens;

public enum TokenKind
{
    Text, Variable,
    IfStart, ElseIf, Else, IfEnd,
    ForStart, ForEnd,
    Eof
}
```

- [ ] **Step 2: `Token.cs`**

```csharp
namespace Artect.Templating.Tokens;

public sealed record Token(TokenKind Kind, string Text, int Line)
{
    public static readonly Token Eof = new(TokenKind.Eof, string.Empty, 0);
}
```

### Task 3.2: Tokenizer

**Files:**
- Create: `src/Artect.Templating/Tokens/Tokenizer.cs`

- [ ] **Step 1: Write the single-pass tokenizer**

```csharp
using System.Collections.Generic;
using System.Text;

namespace Artect.Templating.Tokens;

public static class Tokenizer
{
    public static IReadOnlyList<Token> Tokenize(string source)
    {
        var tokens = new List<Token>();
        var sb = new StringBuilder();
        int i = 0, line = 1;
        while (i < source.Length)
        {
            if (i + 1 < source.Length && source[i] == '{' && source[i + 1] == '{')
            {
                if (sb.Length > 0) { tokens.Add(new Token(TokenKind.Text, sb.ToString(), line)); sb.Clear(); }
                int end = source.IndexOf("}}", i + 2, System.StringComparison.Ordinal);
                if (end < 0) throw new TemplateException($"Unclosed tag at line {line}");
                var body = source.Substring(i + 2, end - (i + 2)).Trim();
                tokens.Add(Classify(body, line));
                i = end + 2;
                continue;
            }
            if (source[i] == '\n') line++;
            sb.Append(source[i]);
            i++;
        }
        if (sb.Length > 0) tokens.Add(new Token(TokenKind.Text, sb.ToString(), line));
        tokens.Add(Token.Eof);
        return tokens;
    }

    static Token Classify(string body, int line)
    {
        if (body.StartsWith("# if ")) return new Token(TokenKind.IfStart, body[5..].Trim(), line);
        if (body.StartsWith("# elseif ")) return new Token(TokenKind.ElseIf, body[9..].Trim(), line);
        if (body == "else" || body == "# else") return new Token(TokenKind.Else, string.Empty, line);
        if (body == "/if") return new Token(TokenKind.IfEnd, string.Empty, line);
        if (body.StartsWith("# for ")) return new Token(TokenKind.ForStart, body[6..].Trim(), line);
        if (body == "/for") return new Token(TokenKind.ForEnd, string.Empty, line);
        return new Token(TokenKind.Variable, body, line);
    }
}
```

### Task 3.3: AST nodes

**Files:**
- Create: `src/Artect.Templating/Ast/TemplateNode.cs`

- [ ] **Step 1: `TemplateNode.cs`**

```csharp
using System.Collections.Generic;

namespace Artect.Templating.Ast;

public abstract record TemplateNode;
public sealed record TextNode(string Text) : TemplateNode;
public sealed record VariableNode(string Path, IReadOnlyList<string> Filters) : TemplateNode;
public sealed record IfNode(IReadOnlyList<(string Condition, IReadOnlyList<TemplateNode> Body)> Branches, IReadOnlyList<TemplateNode>? ElseBody) : TemplateNode;
public sealed record ForNode(string ItemName, string CollectionPath, IReadOnlyList<TemplateNode> Body) : TemplateNode;
public sealed record TemplateDocument(IReadOnlyList<TemplateNode> Nodes);
```

### Task 3.4: Parser

**Files:**
- Create: `src/Artect.Templating/TemplateException.cs`
- Create: `src/Artect.Templating/TemplateParser.cs`

- [ ] **Step 1: `TemplateException.cs`**

```csharp
using System;

namespace Artect.Templating;

public sealed class TemplateException : Exception
{
    public TemplateException(string message) : base(message) { }
    public TemplateException(string message, Exception inner) : base(message, inner) { }
}
```

- [ ] **Step 2: `TemplateParser.cs`**

```csharp
using System.Collections.Generic;
using Artect.Templating.Ast;
using Artect.Templating.Tokens;

namespace Artect.Templating;

public static class TemplateParser
{
    public static TemplateDocument Parse(string source)
    {
        var tokens = Tokenizer.Tokenize(source);
        int idx = 0;
        var nodes = ParseNodes(tokens, ref idx, TokenKind.Eof);
        return new TemplateDocument(nodes);
    }

    static IReadOnlyList<TemplateNode> ParseNodes(IReadOnlyList<Token> tokens, ref int idx, params TokenKind[] terminators)
    {
        var nodes = new List<TemplateNode>();
        while (idx < tokens.Count)
        {
            var t = tokens[idx];
            foreach (var term in terminators) if (t.Kind == term) return nodes;
            switch (t.Kind)
            {
                case TokenKind.Text: nodes.Add(new TextNode(t.Text)); idx++; break;
                case TokenKind.Variable: nodes.Add(ParseVariable(t.Text)); idx++; break;
                case TokenKind.IfStart: nodes.Add(ParseIf(tokens, ref idx)); break;
                case TokenKind.ForStart: nodes.Add(ParseFor(tokens, ref idx)); break;
                default: throw new TemplateException($"Unexpected token '{t.Kind}' at line {t.Line}");
            }
        }
        return nodes;
    }

    static VariableNode ParseVariable(string body)
    {
        var parts = body.Split('|');
        var path = parts[0].Trim();
        var filters = new List<string>();
        for (int i = 1; i < parts.Length; i++) filters.Add(parts[i].Trim());
        return new VariableNode(path, filters);
    }

    static IfNode ParseIf(IReadOnlyList<Token> tokens, ref int idx)
    {
        var branches = new List<(string, IReadOnlyList<TemplateNode>)>();
        IReadOnlyList<TemplateNode>? elseBody = null;
        string condition = tokens[idx].Text; idx++;
        var body = ParseNodes(tokens, ref idx, TokenKind.ElseIf, TokenKind.Else, TokenKind.IfEnd);
        branches.Add((condition, body));
        while (idx < tokens.Count && tokens[idx].Kind == TokenKind.ElseIf)
        {
            condition = tokens[idx].Text; idx++;
            body = ParseNodes(tokens, ref idx, TokenKind.ElseIf, TokenKind.Else, TokenKind.IfEnd);
            branches.Add((condition, body));
        }
        if (idx < tokens.Count && tokens[idx].Kind == TokenKind.Else)
        {
            idx++;
            elseBody = ParseNodes(tokens, ref idx, TokenKind.IfEnd);
        }
        if (idx >= tokens.Count || tokens[idx].Kind != TokenKind.IfEnd)
            throw new TemplateException("Unterminated if-block");
        idx++;
        return new IfNode(branches, elseBody);
    }

    static ForNode ParseFor(IReadOnlyList<Token> tokens, ref int idx)
    {
        var body = tokens[idx].Text; idx++;
        var parts = body.Split(new[] { " in " }, 2, System.StringSplitOptions.None);
        if (parts.Length != 2) throw new TemplateException($"Invalid for-loop syntax: '{body}'");
        var nodes = ParseNodes(tokens, ref idx, TokenKind.ForEnd);
        if (idx >= tokens.Count || tokens[idx].Kind != TokenKind.ForEnd)
            throw new TemplateException("Unterminated for-block");
        idx++;
        return new ForNode(parts[0].Trim(), parts[1].Trim(), nodes);
    }
}
```

### Task 3.5: Filters

**Files:**
- Create: `src/Artect.Templating/Filters.cs`

- [ ] **Step 1: Write the filter registry**

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Artect.Templating;

public static class Filters
{
    static readonly Dictionary<string, Func<object?, string?, string>> Registry = new(StringComparer.Ordinal)
    {
        ["Humanize"] = (v, _) => Humanize(AsString(v)),
        ["ToPascalCase"] = (v, _) => Artect.Naming.CasingHelper.ToPascalCase(AsString(v)),
        ["ToCamelCase"] = (v, _) => Artect.Naming.CasingHelper.ToCamelCase(AsString(v)),
        ["ToKebabCase"] = (v, _) => Artect.Naming.CasingHelper.ToKebabCase(AsString(v)),
        ["ToSnakeCase"] = (v, _) => Artect.Naming.CasingHelper.ToSnakeCase(AsString(v)),
        ["Pluralize"] = (v, _) => Artect.Naming.Pluralizer.Pluralize(AsString(v)),
        ["Singularize"] = (v, _) => Artect.Naming.Pluralizer.Singularize(AsString(v)),
        ["Lower"] = (v, _) => AsString(v).ToLowerInvariant(),
        ["Upper"] = (v, _) => AsString(v).ToUpperInvariant(),
        ["Indent"] = (v, arg) => IndentText(AsString(v), int.Parse(arg ?? "4", CultureInfo.InvariantCulture)),
    };

    public static string Apply(object? value, string filterExpr)
    {
        var parenIdx = filterExpr.IndexOf('(');
        string name = parenIdx < 0 ? filterExpr : filterExpr[..parenIdx];
        string? arg = parenIdx < 0 ? null : filterExpr[(parenIdx + 1)..].TrimEnd(')');
        if (!Registry.TryGetValue(name, out var fn))
            throw new TemplateException($"Unknown filter '{name}'");
        return fn(value, arg);
    }

    static string AsString(object? v) => v?.ToString() ?? string.Empty;

    static string Humanize(string s)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (i > 0 && char.IsUpper(s[i]) && !char.IsUpper(s[i - 1])) sb.Append(' ');
            sb.Append(s[i]);
        }
        var r = sb.ToString();
        return r.Length == 0 ? r : char.ToUpperInvariant(r[0]) + r[1..].ToLowerInvariant();
    }

    static string IndentText(string text, int spaces)
    {
        var prefix = new string(' ', spaces);
        return prefix + text.Replace("\n", "\n" + prefix);
    }
}
```

**Deliberate deviation from spec §4 reference graph:** the spec lists `Templating` as having zero deps, but the filter registry's casing and pluralization filters directly call `CasingHelper` and `Pluralizer` in `Artect.Naming`. Adding `Templating → Naming` is cycle-safe because `Naming → Core` is the only other edge from `Naming`. Add the reference now:

Run: `dotnet add src/Artect.Templating/Artect.Templating.csproj reference src/Artect.Naming/Artect.Naming.csproj`

### Task 3.6: Renderer

**Files:**
- Create: `src/Artect.Templating/Renderer.cs`

- [ ] **Step 1: Write the AST walker**

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Artect.Templating.Ast;

namespace Artect.Templating;

public static class Renderer
{
    public static string Render(TemplateDocument doc, object context)
    {
        var sb = new StringBuilder();
        var scope = new Dictionary<string, object?>(StringComparer.Ordinal);
        RenderNodes(doc.Nodes, context, scope, sb);
        return sb.ToString();
    }

    static void RenderNodes(IReadOnlyList<TemplateNode> nodes, object context, Dictionary<string, object?> scope, StringBuilder sb)
    {
        foreach (var node in nodes) RenderNode(node, context, scope, sb);
    }

    static void RenderNode(TemplateNode node, object context, Dictionary<string, object?> scope, StringBuilder sb)
    {
        switch (node)
        {
            case TextNode tx:
                sb.Append(tx.Text);
                break;
            case VariableNode v:
                var value = Resolve(v.Path, context, scope);
                foreach (var f in v.Filters) value = Filters.Apply(value, f);
                sb.Append(value?.ToString() ?? string.Empty);
                break;
            case IfNode i:
                bool rendered = false;
                foreach (var (cond, body) in i.Branches)
                {
                    if (IsTruthy(Resolve(cond, context, scope)))
                    {
                        RenderNodes(body, context, scope, sb);
                        rendered = true;
                        break;
                    }
                }
                if (!rendered && i.ElseBody is not null) RenderNodes(i.ElseBody, context, scope, sb);
                break;
            case ForNode f:
                var collection = Resolve(f.CollectionPath, context, scope) as IEnumerable;
                if (collection is null) break;
                foreach (var item in collection)
                {
                    scope[f.ItemName] = item;
                    RenderNodes(f.Body, context, scope, sb);
                }
                scope.Remove(f.ItemName);
                break;
        }
    }

    static bool IsTruthy(object? v) => v switch
    {
        null => false,
        bool b => b,
        string s => !string.IsNullOrEmpty(s),
        IEnumerable e => e.GetEnumerator().MoveNext(),
        _ => true,
    };

    public static object? Resolve(string path, object context, IReadOnlyDictionary<string, object?> scope)
    {
        var parts = path.Split('.');
        object? current = scope.TryGetValue(parts[0], out var scoped) ? scoped : GetMember(context, parts[0]);
        for (int i = 1; i < parts.Length; i++)
        {
            if (current is null) return null;
            current = GetMember(current, parts[i]);
        }
        return current;
    }

    static object? GetMember(object target, string name)
    {
        if (target is IDictionary<string, object?> dict)
            return dict.TryGetValue(name, out var v) ? v : null;
        var t = target.GetType();
        var prop = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (prop is not null) return prop.GetValue(target);
        var field = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
        return field?.GetValue(target);
    }
}
```

Note: `GetMember` uses reflection — the template engine is the only reflection site in the tool. It reads named members from a POCO context; it does not enumerate member lists, so it does not affect determinism.

### Task 3.7: `TemplateLoader`

**Files:**
- Create: `src/Artect.Templating/TemplateLoader.cs`

- [ ] **Step 1: Embedded-resource loader**

```csharp
using System.IO;
using System.Reflection;

namespace Artect.Templating;

public sealed class TemplateLoader
{
    readonly Assembly _assembly;
    readonly string _prefix;

    public TemplateLoader(Assembly assembly, string prefix)
    {
        _assembly = assembly;
        _prefix = prefix;
    }

    public string Load(string logicalName)
    {
        var full = $"{_prefix}.{logicalName}";
        using var stream = _assembly.GetManifestResourceStream(full)
            ?? throw new TemplateException($"Embedded template '{full}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
```

### Task 3.8: Compile + commit Phase 3

- [ ] **Step 1: Build**

Run: `dotnet build Artect.sln`
Expected: green.

- [ ] **Step 2: Commit**

```bash
git add -- src/Artect.Templating/
git commit -m "feat(templating): tokenizer, parser, renderer, filters, loader"
```

---
## Phase 4 — Config (Artect.Config)

**Goal:** `ArtectConfig` record + hand-rolled `YamlReader`/`YamlWriter`, strict (unknown keys throw).

### Task 4.1: Config enums

**Files:**
- Create: `src/Artect.Config/DataAccessKind.cs`
- Create: `src/Artect.Config/ApiVersioningKind.cs`
- Create: `src/Artect.Config/AuthKind.cs`
- Create: `src/Artect.Config/CrudOperation.cs`
- Create: `src/Artect.Config/TargetFramework.cs`

- [ ] **Step 1: Write each enum**

```csharp
namespace Artect.Config;
public enum DataAccessKind { EfCore, Dapper }
```

```csharp
namespace Artect.Config;
public enum ApiVersioningKind { None, UrlSegment, Header, QueryString }
```

```csharp
namespace Artect.Config;
public enum AuthKind { None, JwtBearer, Auth0, AzureAd, ApiKey }
```

```csharp
using System;

namespace Artect.Config;

[Flags]
public enum CrudOperation
{
    None     = 0,
    GetList  = 1 << 0,
    GetById  = 1 << 1,
    Post     = 1 << 2,
    Put      = 1 << 3,
    Patch    = 1 << 4,
    Delete   = 1 << 5,
    All      = GetList | GetById | Post | Put | Patch | Delete,
}
```

```csharp
namespace Artect.Config;
public enum TargetFramework { Net8_0, Net9_0 }

public static class TargetFrameworkExtensions
{
    public static string ToMoniker(this TargetFramework tfm) => tfm switch
    {
        TargetFramework.Net8_0 => "net8.0",
        TargetFramework.Net9_0 => "net9.0",
        _ => throw new System.ArgumentOutOfRangeException(nameof(tfm))
    };

    public static TargetFramework FromMoniker(string moniker) => moniker switch
    {
        "net8.0" => TargetFramework.Net8_0,
        "net9.0" => TargetFramework.Net9_0,
        _ => throw new System.ArgumentException($"Unsupported target framework '{moniker}'.", nameof(moniker))
    };
}
```

### Task 4.2: `ArtectConfig` record

**Files:**
- Create: `src/Artect.Config/ArtectConfig.cs`

- [ ] **Step 1: Write the config record with defaults**

```csharp
using System.Collections.Generic;

namespace Artect.Config;

public sealed record ArtectConfig(
    string ProjectName,
    string OutputDirectory,
    TargetFramework TargetFramework,
    DataAccessKind DataAccess,
    bool EmitRepositoriesAndAbstractions,
    string GeneratedByLabel,
    bool GenerateInitialMigration,
    CrudOperation Crud,
    ApiVersioningKind ApiVersioning,
    AuthKind Auth,
    bool IncludeTestsProject,
    bool IncludeDockerAssets,
    bool PartitionStoredProceduresBySchema,
    bool IncludeChildCollectionsInResponses,
    bool ValidateForeignKeyReferences,
    IReadOnlyList<string> Schemas)
{
    public static ArtectConfig Defaults() => new(
        ProjectName: "MyApi",
        OutputDirectory: "./MyApi",
        TargetFramework: TargetFramework.Net9_0,
        DataAccess: DataAccessKind.EfCore,
        EmitRepositoriesAndAbstractions: true,
        GeneratedByLabel: "Artect 1.0.0",
        GenerateInitialMigration: false,
        Crud: CrudOperation.All,
        ApiVersioning: ApiVersioningKind.None,
        Auth: AuthKind.None,
        IncludeTestsProject: true,
        IncludeDockerAssets: true,
        PartitionStoredProceduresBySchema: false,
        IncludeChildCollectionsInResponses: false,
        ValidateForeignKeyReferences: false,
        Schemas: new[] { "dbo" });
}
```

### Task 4.3: `YamlReader` — strict, minimal, hand-rolled

**Files:**
- Create: `src/Artect.Config/YamlException.cs`
- Create: `src/Artect.Config/YamlReader.cs`

- [ ] **Step 1: `YamlException.cs`**

```csharp
using System;

namespace Artect.Config;

public sealed class YamlException : Exception
{
    public YamlException(string message) : base(message) { }
    public YamlException(string message, Exception inner) : base(message, inner) { }
}
```

- [ ] **Step 2: `YamlReader.cs` — parses only the subset we emit**

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Artect.Config;

public static class YamlReader
{
    public static ArtectConfig ReadFile(string path) => Read(File.ReadAllText(path));

    public static ArtectConfig Read(string content)
    {
        var values = Parse(content);
        string Require(string key) => values.TryGetValue(key, out var v)
            ? v
            : throw new YamlException($"Missing required key '{key}' in artect.yaml.");

        var knownKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "projectName","outputDirectory","targetFramework","dataAccess","emitRepositoriesAndAbstractions",
            "generatedByLabel","generateInitialMigration","crud","apiVersioning","auth",
            "includeTestsProject","includeDockerAssets","partitionStoredProceduresBySchema",
            "includeChildCollectionsInResponses","validateForeignKeyReferences","schemas","connectionString"
        };
        foreach (var k in values.Keys)
            if (!knownKeys.Contains(k)) throw new YamlException($"Unknown key '{k}' in artect.yaml.");

        return new ArtectConfig(
            ProjectName: Require("projectName").Trim(),
            OutputDirectory: Require("outputDirectory").Trim(),
            TargetFramework: TargetFrameworkExtensions.FromMoniker(Require("targetFramework").Trim()),
            DataAccess: ParseEnum<DataAccessKind>(Require("dataAccess")),
            EmitRepositoriesAndAbstractions: ParseBool(Require("emitRepositoriesAndAbstractions")),
            GeneratedByLabel: TrimQuotes(Require("generatedByLabel")),
            GenerateInitialMigration: ParseBool(Require("generateInitialMigration")),
            Crud: ParseCrud(Require("crud")),
            ApiVersioning: ParseEnum<ApiVersioningKind>(Require("apiVersioning")),
            Auth: ParseEnum<AuthKind>(Require("auth")),
            IncludeTestsProject: ParseBool(Require("includeTestsProject")),
            IncludeDockerAssets: ParseBool(Require("includeDockerAssets")),
            PartitionStoredProceduresBySchema: ParseBool(Require("partitionStoredProceduresBySchema")),
            IncludeChildCollectionsInResponses: ParseBool(Require("includeChildCollectionsInResponses")),
            ValidateForeignKeyReferences: ParseBool(Require("validateForeignKeyReferences")),
            Schemas: ParseStringList(Require("schemas")));
    }

    static Dictionary<string, string> Parse(string content)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = content.Replace("\r\n", "\n").Split('\n');
        string? currentKey = null;
        var listAccum = new List<string>();
        foreach (var rawLine in lines)
        {
            var line = StripComment(rawLine);
            if (string.IsNullOrWhiteSpace(line))
            {
                if (currentKey is not null) { values[currentKey] = "[" + string.Join(",", listAccum) + "]"; currentKey = null; listAccum.Clear(); }
                continue;
            }
            if (line.StartsWith("  - ", StringComparison.Ordinal))
            {
                if (currentKey is null) throw new YamlException($"Stray list item: '{line}'");
                listAccum.Add(line.Substring(4).Trim());
                continue;
            }
            if (currentKey is not null)
            {
                values[currentKey] = "[" + string.Join(",", listAccum) + "]";
                currentKey = null;
                listAccum.Clear();
            }
            var colon = line.IndexOf(':');
            if (colon < 0) throw new YamlException($"Invalid line (no colon): '{line}'");
            var key = line.Substring(0, colon).Trim();
            var value = line.Substring(colon + 1).Trim();
            if (value.Length == 0)
            {
                currentKey = key;
                continue;
            }
            values[key] = value;
        }
        if (currentKey is not null) values[currentKey] = "[" + string.Join(",", listAccum) + "]";
        return values;
    }

    static string StripComment(string line)
    {
        var hash = line.IndexOf('#');
        if (hash < 0) return line;
        // keep hash if inside quotes
        bool inQuote = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') inQuote = !inQuote;
            if (line[i] == '#' && !inQuote) return line.Substring(0, i);
        }
        return line;
    }

    static bool ParseBool(string s) => s.Trim().ToLowerInvariant() switch
    {
        "true" => true,
        "false" => false,
        _ => throw new YamlException($"Invalid bool value '{s}'.")
    };

    static T ParseEnum<T>(string s) where T : struct, Enum
    {
        if (Enum.TryParse<T>(s.Trim(), ignoreCase: true, out var v)) return v;
        throw new YamlException($"Invalid value '{s}' for enum {typeof(T).Name}.");
    }

    static CrudOperation ParseCrud(string s)
    {
        var items = ParseStringList(s);
        var result = CrudOperation.None;
        foreach (var raw in items)
        {
            var trimmed = raw.Trim();
            if (!Enum.TryParse<CrudOperation>(trimmed, ignoreCase: true, out var flag))
                throw new YamlException($"Invalid CRUD operation '{trimmed}'.");
            result |= flag;
        }
        return result;
    }

    static IReadOnlyList<string> ParseStringList(string s)
    {
        var t = s.Trim();
        if (t.StartsWith("[") && t.EndsWith("]"))
        {
            var inner = t.Substring(1, t.Length - 2);
            if (inner.Length == 0) return Array.Empty<string>();
            return inner.Split(',').Select(x => TrimQuotes(x.Trim())).ToArray();
        }
        return new[] { TrimQuotes(t) };
    }

    static string TrimQuotes(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') return s.Substring(1, s.Length - 2);
        return s;
    }
}
```

### Task 4.4: `YamlWriter`

**Files:**
- Create: `src/Artect.Config/YamlWriter.cs`

- [ ] **Step 1: Write deterministic `YamlWriter`**

```csharp
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Artect.Config;

public static class YamlWriter
{
    public static void WriteFile(string path, ArtectConfig cfg) => File.WriteAllText(path, Write(cfg));

    public static string Write(ArtectConfig cfg)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"projectName: {cfg.ProjectName}");
        sb.AppendLine($"outputDirectory: {cfg.OutputDirectory}");
        sb.AppendLine($"targetFramework: {cfg.TargetFramework.ToMoniker()}");
        sb.AppendLine($"dataAccess: {cfg.DataAccess}");
        sb.AppendLine($"emitRepositoriesAndAbstractions: {Bool(cfg.EmitRepositoriesAndAbstractions)}");
        sb.AppendLine($"generatedByLabel: \"{cfg.GeneratedByLabel}\"");
        sb.AppendLine($"generateInitialMigration: {Bool(cfg.GenerateInitialMigration)}");
        sb.AppendLine($"crud: {CrudString(cfg.Crud)}");
        sb.AppendLine($"apiVersioning: {cfg.ApiVersioning}");
        sb.AppendLine($"auth: {cfg.Auth}");
        sb.AppendLine($"includeTestsProject: {Bool(cfg.IncludeTestsProject)}");
        sb.AppendLine($"includeDockerAssets: {Bool(cfg.IncludeDockerAssets)}");
        sb.AppendLine($"partitionStoredProceduresBySchema: {Bool(cfg.PartitionStoredProceduresBySchema)}");
        sb.AppendLine($"includeChildCollectionsInResponses: {Bool(cfg.IncludeChildCollectionsInResponses)}");
        sb.AppendLine($"validateForeignKeyReferences: {Bool(cfg.ValidateForeignKeyReferences)}");
        sb.AppendLine("schemas:");
        foreach (var s in cfg.Schemas) sb.AppendLine($"  - {s}");
        return sb.ToString();
    }

    static string Bool(bool b) => b ? "true" : "false";

    static string CrudString(CrudOperation ops)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (CrudOperation v in Enum.GetValues(typeof(CrudOperation)))
        {
            if (v is CrudOperation.None or CrudOperation.All) continue;
            if (ops.HasFlag(v)) parts.Add(v.ToString());
        }
        return "[" + string.Join(", ", parts) + "]";
    }
}
```

### Task 4.5: Compile + commit Phase 4

- [ ] **Step 1: Build**

Run: `dotnet build Artect.sln`
Expected: green.

- [ ] **Step 2: Commit**

```bash
git add -- src/Artect.Config/
git commit -m "feat(config): ArtectConfig + hand-rolled YAML reader/writer"
```

---
## Phase 5 — Introspection (Artect.Introspection)

**Goal:** Nine per-concept SQL readers + one orchestrator. Deterministic ordering in SQL.

### Task 5.1: Connection helper

**Files:**
- Create: `src/Artect.Introspection/IntrospectionException.cs`
- Create: `src/Artect.Introspection/SqlConnectionFactory.cs`

- [ ] **Step 1: `IntrospectionException.cs`**

```csharp
using System;

namespace Artect.Introspection;

public sealed class IntrospectionException : Exception
{
    public IntrospectionException(string message) : base(message) { }
    public IntrospectionException(string message, Exception inner) : base(message, inner) { }
}
```

- [ ] **Step 2: `SqlConnectionFactory.cs` — thin wrapper that creates & opens a `SqlConnection`**

```csharp
using Microsoft.Data.SqlClient;

namespace Artect.Introspection;

public sealed class SqlConnectionFactory
{
    readonly string _connectionString;
    public SqlConnectionFactory(string connectionString) => _connectionString = connectionString;

    public SqlConnection OpenConnection()
    {
        var conn = new SqlConnection(_connectionString);
        conn.Open();
        return conn;
    }
}
```

### Task 5.2: Schema probe

**Files:**
- Create: `src/Artect.Introspection/SchemaProbe.cs`

- [ ] **Step 1: Quick schemas-only probe used by the wizard before full introspection**

```csharp
using System.Collections.Generic;
using Microsoft.Data.SqlClient;

namespace Artect.Introspection;

public sealed class SchemaProbe
{
    readonly SqlConnectionFactory _factory;
    public SchemaProbe(SqlConnectionFactory factory) => _factory = factory;

    public IReadOnlyList<string> ListSchemas()
    {
        using var conn = _factory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT SCHEMA_NAME
FROM INFORMATION_SCHEMA.SCHEMATA
WHERE SCHEMA_NAME NOT IN ('sys','INFORMATION_SCHEMA','db_owner','db_accessadmin','db_securityadmin',
 'db_ddladmin','db_backupoperator','db_datareader','db_datawriter','db_denydatareader','db_denydatawriter','guest')
ORDER BY SCHEMA_NAME;";
        using var reader = cmd.ExecuteReader();
        var schemas = new List<string>();
        while (reader.Read()) schemas.Add(reader.GetString(0));
        return schemas;
    }
}
```

### Task 5.3: `TablesReader`

**Files:**
- Create: `src/Artect.Introspection/Readers/TablesReader.cs`

- [ ] **Step 1: Read tables + columns + PKs in three queries, assemble into `Table` records**

```csharp
using System.Collections.Generic;
using System.Linq;
using Artect.Core.Schema;
using Microsoft.Data.SqlClient;

namespace Artect.Introspection.Readers;

public static class TablesReader
{
    public static IReadOnlyList<Table> Read(SqlConnection conn, IReadOnlyList<string> schemas)
    {
        var schemaList = "'" + string.Join("','", schemas) + "'";
        var tables = new List<(string Schema, string Name)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
SELECT TABLE_SCHEMA, TABLE_NAME
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_SCHEMA IN ({schemaList})
ORDER BY TABLE_SCHEMA, TABLE_NAME;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) tables.Add((r.GetString(0), r.GetString(1)));
        }

        var columnsByTable = ReadColumns(conn, schemaList);
        var pksByTable = ReadPrimaryKeys(conn, schemaList);

        // FKs, Unique, Indexes, Checks: set empty here; ConstraintsReaders fills later.
        var result = new List<Table>(tables.Count);
        foreach (var (sch, nm) in tables)
        {
            columnsByTable.TryGetValue((sch, nm), out var cols);
            pksByTable.TryGetValue((sch, nm), out var pk);
            result.Add(new Table(
                Schema: sch, Name: nm,
                Columns: cols ?? new List<Column>(),
                PrimaryKey: pk,
                ForeignKeys: new List<ForeignKey>(),
                UniqueConstraints: new List<UniqueConstraint>(),
                Indexes: new List<Index>(),
                CheckConstraints: new List<CheckConstraint>()));
        }
        return result;
    }

    static Dictionary<(string, string), IReadOnlyList<Column>> ReadColumns(SqlConnection conn, string schemaList)
    {
        var map = new Dictionary<(string, string), List<Column>>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
SELECT
  c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME, c.ORDINAL_POSITION,
  c.DATA_TYPE, c.IS_NULLABLE,
  COLUMNPROPERTY(OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME), c.COLUMN_NAME, 'IsIdentity') AS IsIdentity,
  COLUMNPROPERTY(OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME), c.COLUMN_NAME, 'IsComputed') AS IsComputed,
  c.CHARACTER_MAXIMUM_LENGTH, c.NUMERIC_PRECISION, c.NUMERIC_SCALE, c.COLUMN_DEFAULT
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_SCHEMA IN ({schemaList})
ORDER BY c.TABLE_SCHEMA, c.TABLE_NAME, c.ORDINAL_POSITION;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var key = (r.GetString(0), r.GetString(1));
            if (!map.TryGetValue(key, out var list)) { list = new List<Column>(); map[key] = list; }
            var sqlType = r.GetString(4);
            var clr = SqlTypeMap.ToClr(sqlType);
            var isIdentity = !r.IsDBNull(6) && r.GetInt32(6) == 1;
            var isComputed = !r.IsDBNull(7) && r.GetInt32(7) == 1;
            var def = r.IsDBNull(11) ? null : r.GetString(11);
            var serverGen = isIdentity || isComputed || DefaultLooksServerGenerated(def);
            list.Add(new Column(
                Name: r.GetString(2),
                OrdinalPosition: r.GetInt32(3),
                SqlType: sqlType,
                ClrType: clr,
                IsNullable: r.GetString(5) == "YES",
                IsIdentity: isIdentity,
                IsComputed: isComputed,
                IsServerGenerated: serverGen,
                MaxLength: r.IsDBNull(8) ? null : r.GetInt32(8),
                Precision: r.IsDBNull(9) ? null : (int?)r.GetByte(9),
                Scale: r.IsDBNull(10) ? null : (int?)r.GetInt32(10),
                DefaultValue: def));
        }
        return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Column>)kv.Value);
    }

    static bool DefaultLooksServerGenerated(string? def)
    {
        if (def is null) return false;
        var lower = def.ToLowerInvariant();
        return lower.Contains("newid()") || lower.Contains("newsequentialid()") || lower.Contains("next value for ");
    }

    static Dictionary<(string, string), PrimaryKey> ReadPrimaryKeys(SqlConnection conn, string schemaList)
    {
        var map = new Dictionary<(string, string), List<(string ConstraintName, string ColumnName, int Ordinal)>>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
SELECT tc.TABLE_SCHEMA, tc.TABLE_NAME, tc.CONSTRAINT_NAME, kcu.COLUMN_NAME, kcu.ORDINAL_POSITION
FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
  ON tc.CONSTRAINT_SCHEMA = kcu.CONSTRAINT_SCHEMA AND tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY' AND tc.TABLE_SCHEMA IN ({schemaList})
ORDER BY tc.TABLE_SCHEMA, tc.TABLE_NAME, kcu.ORDINAL_POSITION;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var key = (r.GetString(0), r.GetString(1));
            if (!map.TryGetValue(key, out var list)) { list = new List<(string, string, int)>(); map[key] = list; }
            list.Add((r.GetString(2), r.GetString(3), r.GetInt32(4)));
        }
        return map.ToDictionary(
            kv => kv.Key,
            kv => new PrimaryKey(
                Name: kv.Value[0].ConstraintName,
                ColumnNames: kv.Value.OrderBy(x => x.Ordinal).Select(x => x.ColumnName).ToList()));
    }
}
```

### Task 5.4: `ForeignKeysReader`

**Files:**
- Create: `src/Artect.Introspection/Readers/ForeignKeysReader.cs`

- [ ] **Step 1:**

```csharp
using System.Collections.Generic;
using System.Linq;
using Artect.Core.Schema;
using Microsoft.Data.SqlClient;

namespace Artect.Introspection.Readers;

public static class ForeignKeysReader
{
    public static Dictionary<(string, string), IReadOnlyList<ForeignKey>> Read(SqlConnection conn, IReadOnlyList<string> schemas)
    {
        var schemaList = "'" + string.Join("','", schemas) + "'";
        var rows = new List<(string FkName, string FromSch, string FromTbl, string FromCol, string ToSch, string ToTbl, string ToCol, int Ord, string DeleteAction, string UpdateAction)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
SELECT fk.name,
       ps.name AS from_schema, pt.name AS from_table, pc.name AS from_column,
       rs.name AS to_schema, rt.name AS to_table, rc.name AS to_column,
       fkc.constraint_column_id,
       fk.delete_referential_action_desc, fk.update_referential_action_desc
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.tables pt ON pt.object_id = fk.parent_object_id
JOIN sys.schemas ps ON ps.schema_id = pt.schema_id
JOIN sys.columns pc ON pc.object_id = pt.object_id AND pc.column_id = fkc.parent_column_id
JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
JOIN sys.columns rc ON rc.object_id = rt.object_id AND rc.column_id = fkc.referenced_column_id
WHERE ps.name IN ({schemaList})
ORDER BY ps.name, pt.name, fk.name, fkc.constraint_column_id;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                rows.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                          r.GetString(4), r.GetString(5), r.GetString(6), r.GetInt32(7),
                          r.GetString(8), r.GetString(9)));
        }

        var grouped = rows.GroupBy(x => (x.FkName, x.FromSch, x.FromTbl, x.ToSch, x.ToTbl, x.DeleteAction, x.UpdateAction));
        var byTable = new Dictionary<(string, string), List<ForeignKey>>();
        foreach (var g in grouped)
        {
            var pairs = g.OrderBy(x => x.Ord)
                .Select(x => new ForeignKeyColumnPair(x.FromCol, x.ToCol))
                .ToList();
            var fk = new ForeignKey(
                Name: g.Key.FkName,
                FromSchema: g.Key.FromSch, FromTable: g.Key.FromTbl,
                ToSchema: g.Key.ToSch, ToTable: g.Key.ToTbl,
                ColumnPairs: pairs,
                OnDelete: MapAction(g.Key.DeleteAction),
                OnUpdate: MapAction(g.Key.UpdateAction));
            var tableKey = (g.Key.FromSch, g.Key.FromTbl);
            if (!byTable.TryGetValue(tableKey, out var list)) { list = new List<ForeignKey>(); byTable[tableKey] = list; }
            list.Add(fk);
        }
        return byTable.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<ForeignKey>)kv.Value.OrderBy(f => f.Name).ToList());
    }

    static ForeignKeyAction MapAction(string desc) => desc.ToUpperInvariant() switch
    {
        "CASCADE" => ForeignKeyAction.Cascade,
        "SET_NULL" => ForeignKeyAction.SetNull,
        "SET_DEFAULT" => ForeignKeyAction.SetDefault,
        _ => ForeignKeyAction.NoAction
    };
}
```

### Task 5.5: Remaining small readers

**Files:**
- Create: `src/Artect.Introspection/Readers/UniqueConstraintsReader.cs`
- Create: `src/Artect.Introspection/Readers/IndexesReader.cs`
- Create: `src/Artect.Introspection/Readers/CheckConstraintsReader.cs`
- Create: `src/Artect.Introspection/Readers/SequencesReader.cs`
- Create: `src/Artect.Introspection/Readers/ViewsReader.cs`

- [ ] **Step 1: `UniqueConstraintsReader.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using Artect.Core.Schema;
using Microsoft.Data.SqlClient;

namespace Artect.Introspection.Readers;

public static class UniqueConstraintsReader
{
    public static Dictionary<(string, string), IReadOnlyList<UniqueConstraint>> Read(SqlConnection conn, IReadOnlyList<string> schemas)
    {
        var schemaList = "'" + string.Join("','", schemas) + "'";
        var rows = new List<(string Sch, string Tbl, string Name, string Col, int Ord)>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
SELECT tc.TABLE_SCHEMA, tc.TABLE_NAME, tc.CONSTRAINT_NAME, kcu.COLUMN_NAME, kcu.ORDINAL_POSITION
FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
  ON tc.CONSTRAINT_SCHEMA = kcu.CONSTRAINT_SCHEMA AND tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
WHERE tc.CONSTRAINT_TYPE = 'UNIQUE' AND tc.TABLE_SCHEMA IN ({schemaList})
ORDER BY tc.TABLE_SCHEMA, tc.TABLE_NAME, tc.CONSTRAINT_NAME, kcu.ORDINAL_POSITION;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4)));
        return rows
            .GroupBy(x => (x.Sch, x.Tbl, x.Name))
            .GroupBy(g => (g.Key.Sch, g.Key.Tbl))
            .ToDictionary(
                gg => gg.Key,
                gg => (IReadOnlyList<UniqueConstraint>)gg.Select(g => new UniqueConstraint(
                    g.Key.Name,
                    g.OrderBy(x => x.Ord).Select(x => x.Col).ToList())).ToList());
    }
}
```

- [ ] **Step 2: `IndexesReader.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using Artect.Core.Schema;
using Microsoft.Data.SqlClient;

namespace Artect.Introspection.Readers;

public static class IndexesReader
{
    public static Dictionary<(string, string), IReadOnlyList<Index>> Read(SqlConnection conn, IReadOnlyList<string> schemas)
    {
        var schemaList = "'" + string.Join("','", schemas) + "'";
        var rows = new List<(string Sch, string Tbl, string Name, bool Unique, bool Clustered, string Col, int Ord, bool Included)>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
SELECT sch.name, t.name, i.name, i.is_unique, (CASE WHEN i.type_desc = 'CLUSTERED' THEN 1 ELSE 0 END),
       c.name, ic.key_ordinal, ic.is_included_column
FROM sys.indexes i
JOIN sys.tables t ON t.object_id = i.object_id
JOIN sys.schemas sch ON sch.schema_id = t.schema_id
JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE i.is_primary_key = 0 AND i.is_hypothetical = 0 AND i.name IS NOT NULL
  AND sch.name IN ({schemaList})
ORDER BY sch.name, t.name, i.name, ic.key_ordinal, c.name;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetBoolean(3), r.GetInt32(4) == 1,
                      r.GetString(5), r.GetByte(6), r.GetBoolean(7)));
        var grouped = rows
            .GroupBy(x => (x.Sch, x.Tbl, x.Name, x.Unique, x.Clustered));
        var byTable = new Dictionary<(string, string), List<Index>>();
        foreach (var g in grouped)
        {
            var keys = g.Where(x => !x.Included).OrderBy(x => x.Ord).Select(x => x.Col).ToList();
            var includes = g.Where(x => x.Included).Select(x => x.Col).OrderBy(s => s).ToList();
            var idx = new Index(g.Key.Name, g.Key.Unique, g.Key.Clustered, keys, includes);
            var tableKey = (g.Key.Sch, g.Key.Tbl);
            if (!byTable.TryGetValue(tableKey, out var list)) { list = new List<Index>(); byTable[tableKey] = list; }
            list.Add(idx);
        }
        return byTable.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Index>)kv.Value.OrderBy(i => i.Name).ToList());
    }
}
```

- [ ] **Step 3: `CheckConstraintsReader.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using Artect.Core.Schema;
using Microsoft.Data.SqlClient;

namespace Artect.Introspection.Readers;

public static class CheckConstraintsReader
{
    public static Dictionary<(string, string), IReadOnlyList<CheckConstraint>> Read(SqlConnection conn, IReadOnlyList<string> schemas)
    {
        var schemaList = "'" + string.Join("','", schemas) + "'";
        var rows = new List<CheckConstraint>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
SELECT sch.name, t.name, cc.name, col.name, cc.definition
FROM sys.check_constraints cc
JOIN sys.tables t ON t.object_id = cc.parent_object_id
JOIN sys.schemas sch ON sch.schema_id = t.schema_id
LEFT JOIN sys.columns col ON col.object_id = cc.parent_object_id AND col.column_id = cc.parent_column_id
WHERE sch.name IN ({schemaList})
ORDER BY sch.name, t.name, cc.name;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            rows.Add(new CheckConstraint(
                Name: r.GetString(2),
                TableSchema: r.GetString(0),
                TableName: r.GetString(1),
                ColumnName: r.IsDBNull(3) ? null : r.GetString(3),
                Expression: r.GetString(4)));
        }
        return rows.GroupBy(x => (x.TableSchema, x.TableName))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CheckConstraint>)g.OrderBy(c => c.Name).ToList());
    }
}
```

- [ ] **Step 4: `SequencesReader.cs`**

```csharp
using System.Collections.Generic;
using Artect.Core.Schema;
using Microsoft.Data.SqlClient;

namespace Artect.Introspection.Readers;

public static class SequencesReader
{
    public static IReadOnlyList<Sequence> Read(SqlConnection conn, IReadOnlyList<string> schemas)
    {
        var schemaList = "'" + string.Join("','", schemas) + "'";
        var list = new List<Sequence>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
SELECT sch.name AS schema_name, s.name AS sequence_name, tp.name AS data_type,
       CAST(s.start_value AS bigint), CAST(s.increment AS bigint),
       CAST(s.minimum_value AS bigint), CAST(s.maximum_value AS bigint), s.is_cycling
FROM sys.sequences s
JOIN sys.schemas sch ON sch.schema_id = s.schema_id
JOIN sys.types tp ON tp.user_type_id = s.user_type_id
WHERE sch.name IN ({schemaList})
ORDER BY sch.name, s.name;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Sequence(
                Schema: r.GetString(0), Name: r.GetString(1),
                SqlType: r.GetString(2),
                StartValue: r.GetInt64(3), Increment: r.GetInt64(4),
                MinValue: r.IsDBNull(5) ? null : r.GetInt64(5),
                MaxValue: r.IsDBNull(6) ? null : r.GetInt64(6),
                IsCycling: r.GetBoolean(7)));
        }
        return list;
    }
}
```

- [ ] **Step 5: `ViewsReader.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using Artect.Core.Schema;
using Microsoft.Data.SqlClient;

namespace Artect.Introspection.Readers;

public static class ViewsReader
{
    public static IReadOnlyList<View> Read(SqlConnection conn, IReadOnlyList<string> schemas)
    {
        var schemaList = "'" + string.Join("','", schemas) + "'";
        var views = new List<(string Sch, string Nm)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
SELECT TABLE_SCHEMA, TABLE_NAME
FROM INFORMATION_SCHEMA.VIEWS
WHERE TABLE_SCHEMA IN ({schemaList})
ORDER BY TABLE_SCHEMA, TABLE_NAME;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) views.Add((r.GetString(0), r.GetString(1)));
        }
        var colsByView = new Dictionary<(string, string), List<Column>>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, ORDINAL_POSITION, DATA_TYPE, IS_NULLABLE,
       CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA IN ({schemaList})
  AND TABLE_NAME IN (SELECT TABLE_NAME FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_SCHEMA IN ({schemaList}))
ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var key = (r.GetString(0), r.GetString(1));
                if (!colsByView.TryGetValue(key, out var list)) { list = new List<Column>(); colsByView[key] = list; }
                var sqlType = r.GetString(4);
                list.Add(new Column(
                    Name: r.GetString(2), OrdinalPosition: r.GetInt32(3),
                    SqlType: sqlType, ClrType: SqlTypeMap.ToClr(sqlType),
                    IsNullable: r.GetString(5) == "YES",
                    IsIdentity: false, IsComputed: false, IsServerGenerated: false,
                    MaxLength: r.IsDBNull(6) ? null : r.GetInt32(6),
                    Precision: r.IsDBNull(7) ? null : (int?)r.GetByte(7),
                    Scale: r.IsDBNull(8) ? null : (int?)r.GetInt32(8),
                    DefaultValue: null));
            }
        }
        return views.Select(v => new View(v.Sch, v.Nm,
            colsByView.TryGetValue((v.Sch, v.Nm), out var c) ? (IReadOnlyList<Column>)c : new List<Column>())).ToList();
    }
}
```

### Task 5.6: `StoredProceduresReader` + `FunctionsReader`

**Files:**
- Create: `src/Artect.Introspection/Readers/StoredProceduresReader.cs`
- Create: `src/Artect.Introspection/Readers/FunctionsReader.cs`

- [ ] **Step 1: `StoredProceduresReader.cs` — parameters via INFORMATION_SCHEMA, result shape via sp_describe_first_result_set**

```csharp
using System.Collections.Generic;
using System.Linq;
using Artect.Core.Schema;
using Microsoft.Data.SqlClient;

namespace Artect.Introspection.Readers;

public static class StoredProceduresReader
{
    public static IReadOnlyList<StoredProcedure> Read(SqlConnection conn, IReadOnlyList<string> schemas)
    {
        var schemaList = "'" + string.Join("','", schemas) + "'";
        var procs = new List<(string Sch, string Nm)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
SELECT ROUTINE_SCHEMA, ROUTINE_NAME
FROM INFORMATION_SCHEMA.ROUTINES
WHERE ROUTINE_TYPE = 'PROCEDURE' AND ROUTINE_SCHEMA IN ({schemaList})
ORDER BY ROUTINE_SCHEMA, ROUTINE_NAME;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) procs.Add((r.GetString(0), r.GetString(1)));
        }
        var parms = ReadParameters(conn, schemaList);
        var result = new List<StoredProcedure>();
        foreach (var (sch, nm) in procs)
        {
            parms.TryGetValue((sch, nm), out var ps);
            var (cols, status) = DescribeFirstResultSet(conn, sch, nm);
            result.Add(new StoredProcedure(sch, nm, ps ?? new List<StoredProcedureParameter>(), cols, status));
        }
        return result;
    }

    static Dictionary<(string, string), IReadOnlyList<StoredProcedureParameter>> ReadParameters(SqlConnection conn, string schemaList)
    {
        var map = new Dictionary<(string, string), List<StoredProcedureParameter>>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
SELECT SPECIFIC_SCHEMA, SPECIFIC_NAME, PARAMETER_NAME, ORDINAL_POSITION, DATA_TYPE,
       PARAMETER_MODE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE
FROM INFORMATION_SCHEMA.PARAMETERS
WHERE SPECIFIC_SCHEMA IN ({schemaList})
ORDER BY SPECIFIC_SCHEMA, SPECIFIC_NAME, ORDINAL_POSITION;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var key = (r.GetString(0), r.GetString(1));
            if (!map.TryGetValue(key, out var list)) { list = new List<StoredProcedureParameter>(); map[key] = list; }
            var sqlType = r.GetString(4);
            var mode = r.GetString(5);
            list.Add(new StoredProcedureParameter(
                Name: r.IsDBNull(2) ? $"arg{r.GetInt32(3)}" : r.GetString(2),
                Ordinal: r.GetInt32(3),
                SqlType: sqlType, ClrType: SqlTypeMap.ToClr(sqlType),
                IsNullable: true,
                IsOutput: mode is "OUT" or "INOUT",
                MaxLength: r.IsDBNull(6) ? null : r.GetInt32(6),
                Precision: r.IsDBNull(7) ? null : (int?)r.GetByte(7),
                Scale: r.IsDBNull(8) ? null : (int?)r.GetInt32(8)));
        }
        return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<StoredProcedureParameter>)kv.Value);
    }

    static (IReadOnlyList<StoredProcedureResultColumn>, ResultInferenceStatus) DescribeFirstResultSet(SqlConnection conn, string schema, string name)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "sys.sp_describe_first_result_set";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.Add(new SqlParameter("@tsql", $"EXEC [{schema}].[{name}]"));
            using var r = cmd.ExecuteReader();
            var cols = new List<StoredProcedureResultColumn>();
            int ordinal = 0;
            while (r.Read())
            {
                var colName = r["name"] is System.DBNull ? $"col{ordinal}" : (string)r["name"];
                var typeName = (string)r["system_type_name"];
                var isNullable = (bool)r["is_nullable"];
                var sqlBaseType = typeName.Split('(')[0].ToLowerInvariant();
                cols.Add(new StoredProcedureResultColumn(colName, ordinal++, sqlBaseType, SqlTypeMap.ToClr(sqlBaseType), isNullable));
            }
            if (cols.Count == 0) return (cols, ResultInferenceStatus.Empty);
            return (cols, ResultInferenceStatus.Resolved);
        }
        catch (SqlException)
        {
            return (new List<StoredProcedureResultColumn>(), ResultInferenceStatus.Indeterminate);
        }
    }
}
```

- [ ] **Step 2: `FunctionsReader.cs`**

```csharp
using System.Collections.Generic;
using Artect.Core.Schema;
using Microsoft.Data.SqlClient;

namespace Artect.Introspection.Readers;

public static class FunctionsReader
{
    public static IReadOnlyList<Function> Read(SqlConnection conn, IReadOnlyList<string> schemas)
    {
        var schemaList = "'" + string.Join("','", schemas) + "'";
        var list = new List<Function>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
SELECT sch.name, o.name, o.type_desc
FROM sys.objects o
JOIN sys.schemas sch ON sch.schema_id = o.schema_id
WHERE o.type IN ('FN','IF','TF') AND sch.name IN ({schemaList})
ORDER BY sch.name, o.name;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var schema = r.GetString(0);
            var name = r.GetString(1);
            var desc = r.GetString(2);
            var kind = desc switch
            {
                "SQL_SCALAR_FUNCTION" => FunctionReturnKind.Scalar,
                "SQL_INLINE_TABLE_VALUED_FUNCTION" => FunctionReturnKind.Inline,
                "SQL_TABLE_VALUED_FUNCTION" => FunctionReturnKind.Table,
                _ => FunctionReturnKind.Scalar
            };
            list.Add(new Function(schema, name, kind,
                ReturnSqlType: null, ReturnClrType: null,
                Parameters: new List<FunctionParameter>(),
                ResultColumns: new List<Column>()));
        }
        return list;
    }
}
```

### Task 5.7: Orchestrator `SqlServerSchemaReader`

**Files:**
- Create: `src/Artect.Introspection/SqlServerSchemaReader.cs`

- [ ] **Step 1:**

```csharp
using System.Collections.Generic;
using System.Linq;
using Artect.Core.Schema;
using Artect.Introspection.Readers;

namespace Artect.Introspection;

public sealed class SqlServerSchemaReader
{
    readonly SqlConnectionFactory _factory;
    public SqlServerSchemaReader(SqlConnectionFactory factory) => _factory = factory;

    public SchemaGraph Read(IReadOnlyList<string> schemas)
    {
        using var conn = _factory.OpenConnection();
        var rawTables = TablesReader.Read(conn, schemas);
        var fks = ForeignKeysReader.Read(conn, schemas);
        var uniques = UniqueConstraintsReader.Read(conn, schemas);
        var indexes = IndexesReader.Read(conn, schemas);
        var checks = CheckConstraintsReader.Read(conn, schemas);
        var views = ViewsReader.Read(conn, schemas);
        var sequences = SequencesReader.Read(conn, schemas);
        var procs = StoredProceduresReader.Read(conn, schemas);
        var functions = FunctionsReader.Read(conn, schemas);

        var tables = rawTables.Select(t => t with
        {
            ForeignKeys = fks.TryGetValue((t.Schema, t.Name), out var fkList) ? fkList : new List<ForeignKey>(),
            UniqueConstraints = uniques.TryGetValue((t.Schema, t.Name), out var uqs) ? uqs : new List<UniqueConstraint>(),
            Indexes = indexes.TryGetValue((t.Schema, t.Name), out var ixs) ? ixs : new List<Index>(),
            CheckConstraints = checks.TryGetValue((t.Schema, t.Name), out var cks) ? cks : new List<CheckConstraint>(),
        }).ToList();

        return new SchemaGraph(
            Schemas: schemas.OrderBy(s => s, System.StringComparer.Ordinal).ToList(),
            Tables: tables.OrderBy(t => t.Schema, System.StringComparer.Ordinal).ThenBy(t => t.Name, System.StringComparer.Ordinal).ToList(),
            Views: views,
            Sequences: sequences,
            StoredProcedures: procs,
            Functions: functions);
    }
}
```

### Task 5.8: Compile + commit Phase 5

- [ ] **Step 1: Build**

Run: `dotnet build Artect.sln`
Expected: green.

- [ ] **Step 2: Commit**

```bash
git add -- src/Artect.Introspection/
git commit -m "feat(introspection): SQL Server readers + schema orchestrator"
```

---
## Phase 6 — Named schema model (Artect.Core)

**Goal:** Wrap `SchemaGraph` with naming + collision decisions consumed by every emitter.

### Task 6.1: Navigation records

These live in `Artect.Naming` rather than `Artect.Core`. `NamedSchemaModel` depends on naming helpers (`CasingHelper`, `EntityNaming`, `DbSetNaming`, `JoinTableDetector`) which live in `Artect.Naming`. Placing `NamedSchemaModel` in `Core` would require a `Core → Naming` edge, creating a cycle with the existing `Naming → Core` edge. `Naming` is the natural home.

**Files:**
- Create: `src/Artect.Naming/NamedNavigation.cs`
- Create: `src/Artect.Naming/NamedEntity.cs`
- Create: `src/Artect.Naming/NamedSchemaModel.cs`

- [ ] **Step 1: `NamedNavigation.cs`**

```csharp
using System.Collections.Generic;
using Artect.Core.Schema;

namespace Artect.Naming;

public sealed record NamedNavigation(
    string PropertyName,
    string TargetEntityTypeName,
    bool IsCollection,
    string SourceForeignKeyName,
    IReadOnlyList<ForeignKeyColumnPair> ColumnPairs);
```

- [ ] **Step 2: `NamedEntity.cs`**

```csharp
using System.Collections.Generic;
using Artect.Core.Schema;

namespace Artect.Naming;

public sealed record NamedEntity(
    Table Table,
    string EntityTypeName,
    string DbSetPropertyName,
    IReadOnlyList<NamedNavigation> ReferenceNavigations,
    IReadOnlyList<NamedNavigation> CollectionNavigations,
    bool IsJoinTable,
    bool HasPrimaryKey);
```

- [ ] **Step 3: `NamedSchemaModel.cs` — central computation**

```csharp
using System.Collections.Generic;
using System.Linq;
using Artect.Core.Schema;

namespace Artect.Naming;

public sealed class NamedSchemaModel
{
    public SchemaGraph Graph { get; }
    public IReadOnlyList<NamedEntity> Entities { get; }
    public DbSetNaming DbSets { get; }

    NamedSchemaModel(SchemaGraph graph, IReadOnlyList<NamedEntity> entities, DbSetNaming dbSets)
    {
        Graph = graph;
        Entities = entities;
        DbSets = dbSets;
    }

    public static NamedSchemaModel Build(SchemaGraph graph)
    {
        var dbSets = DbSetNaming.Build(graph);
        var entities = new List<NamedEntity>(graph.Tables.Count);
        foreach (var t in graph.Tables)
        {
            var typeName = dbSets.EntityTypeNames[(t.Schema, t.Name)];
            var dbSetName = dbSets.DbSetNames[(t.Schema, t.Name)];
            entities.Add(new NamedEntity(
                Table: t,
                EntityTypeName: typeName,
                DbSetPropertyName: dbSetName,
                ReferenceNavigations: BuildReferenceNavigations(t, dbSets),
                CollectionNavigations: BuildCollectionNavigations(t, graph, dbSets),
                IsJoinTable: JoinTableDetector.IsJoinTable(t),
                HasPrimaryKey: t.PrimaryKey is not null));
        }
        return new NamedSchemaModel(graph, entities, dbSets);
    }

    static IReadOnlyList<NamedNavigation> BuildReferenceNavigations(Table t, DbSetNaming dbSets)
    {
        var navs = new List<NamedNavigation>();
        var byTarget = t.ForeignKeys.GroupBy(fk => (fk.ToSchema, fk.ToTable));
        foreach (var group in byTarget)
        {
            var fks = group.OrderBy(fk => fk.Name).ToList();
            var targetName = dbSets.EntityTypeNames.TryGetValue((group.Key.ToSchema, group.Key.ToTable), out var n)
                ? n
                : CasingHelper.ToPascalCase(group.Key.ToTable);
            for (int i = 0; i < fks.Count; i++)
            {
                var baseName = EntityNaming.NavigationPropertyName(targetName, collection: false);
                var propName = fks.Count == 1 ? baseName : baseName + "_" + SanitizeColumns(fks[i].ColumnPairs);
                navs.Add(new NamedNavigation(propName, targetName, IsCollection: false,
                    SourceForeignKeyName: fks[i].Name, ColumnPairs: fks[i].ColumnPairs));
            }
        }
        return navs;
    }

    static IReadOnlyList<NamedNavigation> BuildCollectionNavigations(Table t, SchemaGraph graph, DbSetNaming dbSets)
    {
        var navs = new List<NamedNavigation>();
        foreach (var other in graph.Tables)
        {
            foreach (var fk in other.ForeignKeys.Where(f => f.ToSchema == t.Schema && f.ToTable == t.Name))
            {
                var targetName = dbSets.EntityTypeNames[(other.Schema, other.Name)];
                var baseName = EntityNaming.NavigationPropertyName(targetName, collection: true);
                var sameTargetCount = other.ForeignKeys.Count(f => f.ToSchema == t.Schema && f.ToTable == t.Name);
                var propName = sameTargetCount == 1 ? baseName : baseName + "_" + SanitizeColumns(fk.ColumnPairs);
                navs.Add(new NamedNavigation(propName, targetName, IsCollection: true,
                    SourceForeignKeyName: fk.Name, ColumnPairs: fk.ColumnPairs));
            }
        }
        return navs.OrderBy(n => n.PropertyName, System.StringComparer.Ordinal).ToList();
    }

    static string SanitizeColumns(IReadOnlyList<ForeignKeyColumnPair> pairs) =>
        string.Join("_", pairs.Select(p => CasingHelper.ToPascalCase(p.FromColumn)));
}
```

No cross-project reference changes are needed — `Artect.Naming` already references `Artect.Core` (Phase 0), which gives the named types access to `Table`, `ForeignKey`, etc.

### Task 6.2: Compile + commit Phase 6

- [ ] **Step 1: Build**

Run: `dotnet build Artect.sln`
Expected: green.

- [ ] **Step 2: Commit**

```bash
git add -- src/Artect.Core/ src/Artect.Naming/
git commit -m "feat(naming): NamedSchemaModel with cross-schema and multi-FK naming"
```

---

## Phase 7 — Generation scaffolding (Artect.Generation)

**Goal:** Generator pipeline, `EmittedFile`, `CleanLayout`, `GeneratedByRegionWrapper`. No individual emitter logic yet.

### Task 7.1: `EmittedFile` and pipeline types

**Files:**
- Create: `src/Artect.Generation/EmittedFile.cs`
- Create: `src/Artect.Generation/EmitterContext.cs`
- Create: `src/Artect.Generation/IEmitter.cs`

- [ ] **Step 1: `EmittedFile.cs`**

```csharp
namespace Artect.Generation;

public sealed record EmittedFile(string RelativePath, string Contents);
```

- [ ] **Step 2: `EmitterContext.cs`**

```csharp
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;
using Artect.Templating;

namespace Artect.Generation;

public sealed class EmitterContext
{
    public ArtectConfig Config { get; }
    public SchemaGraph Graph { get; }
    public NamedSchemaModel Model { get; }
    public TemplateLoader Templates { get; }

    public EmitterContext(ArtectConfig cfg, SchemaGraph graph, NamedSchemaModel model, TemplateLoader templates)
    {
        Config = cfg;
        Graph = graph;
        Model = model;
        Templates = templates;
    }
}
```

- [ ] **Step 3: `IEmitter.cs`**

```csharp
using System.Collections.Generic;

namespace Artect.Generation;

public interface IEmitter
{
    IReadOnlyList<EmittedFile> Emit(EmitterContext ctx);
}
```

### Task 7.2: `CleanLayout` paths and namespaces

**Files:**
- Create: `src/Artect.Generation/CleanLayout.cs`

- [ ] **Step 1:**

```csharp
namespace Artect.Generation;

public static class CleanLayout
{
    public static string ApiProjectName(string root) => $"{root}.Api";
    public static string ApplicationProjectName(string root) => $"{root}.Application";
    public static string DomainProjectName(string root) => $"{root}.Domain";
    public static string InfrastructureProjectName(string root) => $"{root}.Infrastructure";
    public static string SharedProjectName(string root) => $"{root}.Shared";
    public static string TestsProjectName(string root) => $"{root}.IntegrationTests";

    public static string ApiDir(string root) => $"src/{ApiProjectName(root)}";
    public static string ApplicationDir(string root) => $"src/{ApplicationProjectName(root)}";
    public static string DomainDir(string root) => $"src/{DomainProjectName(root)}";
    public static string InfrastructureDir(string root) => $"src/{InfrastructureProjectName(root)}";
    public static string SharedDir(string root) => $"src/{SharedProjectName(root)}";
    public static string TestsDir(string root) => $"tests/{TestsProjectName(root)}";

    public static string EntityPath(string root, string entityName) => $"{DomainDir(root)}/Entities/{entityName}.cs";
    public static string DtoPath(string root, string entityName) => $"{ApplicationDir(root)}/Dtos/{entityName}Dto.cs";
    public static string ValidatorPath(string root, string className) => $"{ApplicationDir(root)}/Validators/{className}.cs";
    public static string MapperPath(string root, string entityName) => $"{ApplicationDir(root)}/Mappings/{entityName}Mappings.cs";
    public static string RepositoryInterfacePath(string root, string entityName) => $"{ApplicationDir(root)}/Abstractions/Repositories/I{entityName}Repository.cs";
    public static string RepositoryImplPath(string root, string entityName) => $"{InfrastructureDir(root)}/Repositories/{entityName}Repository.cs";
    public static string DbContextPath(string root, string className) => $"{InfrastructureDir(root)}/Data/{className}.cs";
    public static string ConnectionFactoryPath(string root) => $"{InfrastructureDir(root)}/Data/SqlDbConnectionFactory.cs";
    public static string EndpointPath(string root, string plural) => $"{ApiDir(root)}/Endpoints/{plural}Endpoints.cs";
    public static string ProgramCsPath(string root) => $"{ApiDir(root)}/Program.cs";
    public static string AppSettingsPath(string root) => $"{ApiDir(root)}/appsettings.json";
    public static string LaunchSettingsPath(string root) => $"{ApiDir(root)}/Properties/launchSettings.json";
    public static string SharedRequestPath(string root, string entityName, string kind) => $"{SharedDir(root)}/Requests/{kind}{entityName}Request.cs";
    public static string SharedResponsePath(string root, string entityName) => $"{SharedDir(root)}/Responses/{entityName}Response.cs";
    public static string SharedPagedResponsePath(string root) => $"{SharedDir(root)}/Responses/PagedResponse.cs";
    public static string SharedEnumPath(string root, string enumName) => $"{SharedDir(root)}/Enums/{enumName}.cs";
    public static string SharedErrorPath(string root, string className) => $"{SharedDir(root)}/Errors/{className}.cs";
    public static string SprocInterfacePath(string root, string name) => $"{ApplicationDir(root)}/StoredProcedures/{name}.cs";
    public static string DbFunctionsInterfacePath(string root) => $"{ApplicationDir(root)}/StoredProcedures/IDbFunctions.cs";

    public static string ApiNamespace(string root) => $"{root}.Api";
    public static string ApplicationNamespace(string root) => $"{root}.Application";
    public static string DomainNamespace(string root) => $"{root}.Domain";
    public static string InfrastructureNamespace(string root) => $"{root}.Infrastructure";
    public static string SharedNamespace(string root) => $"{root}.Shared";
}
```

### Task 7.3: `GeneratedByRegionWrapper`

**Files:**
- Create: `src/Artect.Generation/GeneratedByRegionWrapper.cs`

- [ ] **Step 1:**

```csharp
using System;

namespace Artect.Generation;

public static class GeneratedByRegionWrapper
{
    public static EmittedFile Wrap(EmittedFile file, string label)
    {
        if (!file.RelativePath.EndsWith(".cs", StringComparison.Ordinal)) return file;
        if (IsHookFile(file.RelativePath)) return InsertHookHeader(file);
        return new EmittedFile(file.RelativePath, WrapTypeDeclaration(file.Contents, label));
    }

    static bool IsHookFile(string path) => path.EndsWith(".Extensions.cs", StringComparison.Ordinal);

    static string InsertHookHeader(EmittedFile file)
    {
        var header = $"// User extension point. Safe to edit.{Environment.NewLine}";
        return new EmittedFile(file.RelativePath, header + file.Contents).Contents is string s
            ? s
            : file.Contents;
    }

    static string WrapTypeDeclaration(string source, string label)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        int typeStart = FindTypeDeclarationStart(lines);
        if (typeStart < 0) return source;
        var before = string.Join('\n', lines, 0, typeStart);
        var after = string.Join('\n', lines, typeStart, lines.Length - typeStart);
        var region = $"#region Generated by {label}\n{after}\n#endregion\n";
        return before + (before.EndsWith("\n") ? "" : "\n") + region;
    }

    static int FindTypeDeclarationStart(string[] lines)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("public ") || trimmed.StartsWith("internal ") || trimmed.StartsWith("sealed ") ||
                trimmed.StartsWith("partial ") || trimmed.StartsWith("static ") || trimmed.StartsWith("abstract "))
            {
                if (trimmed.Contains("class ") || trimmed.Contains("record ") || trimmed.Contains("struct ") || trimmed.Contains("interface ") || trimmed.Contains("enum "))
                    return i;
            }
        }
        return -1;
    }
}
```

### Task 7.4: `Generator`

**Files:**
- Create: `src/Artect.Generation/Generator.cs`

- [ ] **Step 1:**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;
using Artect.Templating;

namespace Artect.Generation;

public sealed class Generator
{
    readonly IReadOnlyList<IEmitter> _emitters;
    public Generator(IReadOnlyList<IEmitter> emitters) =>
        _emitters = emitters.OrderBy(e => e.GetType().Name, StringComparer.Ordinal).ToList();

    public void Generate(ArtectConfig cfg, SchemaGraph graph, string outputRoot)
    {
        var model = NamedSchemaModel.Build(graph);
        var templateAssembly = typeof(Artect.Templates.TemplatesMarker).Assembly;
        var loader = new TemplateLoader(templateAssembly, "Artect.Templates.Files");
        var ctx = new EmitterContext(cfg, graph, model, loader);

        var all = new List<EmittedFile>();
        foreach (var emitter in _emitters) all.AddRange(emitter.Emit(ctx));

        var wrapped = all.Select(f => GeneratedByRegionWrapper.Wrap(f, cfg.GeneratedByLabel)).ToList();

        foreach (var f in wrapped.OrderBy(f => f.RelativePath, StringComparer.Ordinal))
        {
            var full = Path.Combine(outputRoot, f.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, f.Contents);
        }
    }
}
```

### Task 7.5: Templates marker

**Files:**
- Create: `src/Artect.Templates/TemplatesMarker.cs`

- [ ] **Step 1:**

```csharp
namespace Artect.Templates;

public static class TemplatesMarker
{
}
```

### Task 7.6: Compile + commit Phase 7

- [ ] **Step 1: Build**

Run: `dotnet build Artect.sln`
Expected: green.

- [ ] **Step 2: Commit**

```bash
git add -- src/Artect.Generation/ src/Artect.Templates/
git commit -m "feat(generation): pipeline scaffolding, CleanLayout, region wrapper"
```

---
## Phase 8 — Embedded templates (Artect.Templates)

**Goal:** Ship the `.artect` template files. Every `.cs` template lives under `src/Artect.Templates/Files/` and is embedded as `EmbeddedResource` with its name preserved.

### Task 8.1: Wire `EmbeddedResource` for all template files

**Files:**
- Modify: `src/Artect.Templates/Artect.Templates.csproj`

- [ ] **Step 1: Update the csproj to include every `.artect` file as an embedded resource**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <EmbeddedResource Include="Files/**/*.artect" />
  </ItemGroup>
</Project>
```

### Task 8.2: Canonical template — Entity

**Files:**
- Create: `src/Artect.Templates/Files/Entity.cs.artect`

The entity template is the simplest. Others follow the same pattern.

- [ ] **Step 1: Write `Files/Entity.cs.artect`**

```
{{# if HasUsingNamespaces }}
{{# for ns in UsingNamespaces }}
using {{ ns }};
{{/for}}

{{/if}}
namespace {{ Namespace }};

public sealed class {{ EntityName }}
{
{{# for col in Columns }}
    public {{ col.ClrTypeWithNullability }} {{ col.PropertyName }} { get; set; }{{ col.Initializer }}
{{/for}}
{{# if HasReferenceNavigations }}

{{# for nav in ReferenceNavigations }}
    public {{ nav.TypeName }}? {{ nav.PropertyName }} { get; set; }
{{/for}}
{{/if}}
{{# if HasCollectionNavigations }}

{{# for nav in CollectionNavigations }}
    public System.Collections.Generic.ICollection<{{ nav.TypeName }}> {{ nav.PropertyName }} { get; set; } = new System.Collections.Generic.List<{{ nav.TypeName }}>();
{{/for}}
{{/if}}
}
```

The emitter builds a context object with the properties the template references: `HasUsingNamespaces`, `UsingNamespaces` (list of strings), `Namespace`, `EntityName`, `Columns` (each with `ClrTypeWithNullability`, `PropertyName`, `Initializer`), `HasReferenceNavigations`, `ReferenceNavigations` (each with `TypeName`, `PropertyName`), `HasCollectionNavigations`, `CollectionNavigations`.

### Task 8.3: Remaining `.artect` templates

The rest follow the same pattern. Each bullet below is one task: create the file at the given path, model it on the ApiSmith template at `C:\Users\Art\Nextcloud\Art-Work\Projects\apismith-v1\src\ApiSmith.Templates\Files\...`, strip architecture/endpoint-style branches that don't apply to Clean + Minimal, and rename the root namespace tokens from `{{ApiSmith...}}` to Artect's context-property names (`{{ Namespace }}`, `{{ EntityName }}`, etc.).

Create the following (one per step, each commit-per-step optional):

- [ ] `Files/Dto.cs.artect` — entity DTO class; mirrors Entity with `*Dto` suffix, no navigations.
- [ ] `Files/Request.cs.artect` — takes a `Kind` (Create/Update) variable; `[Required]`, `[StringLength]`, `[Range]` attributes from validation rules.
- [ ] `Files/Response.cs.artect` — plain POCO, no attributes.
- [ ] `Files/Enum.cs.artect` — single enum from CHECK IN constraint.
- [ ] `Files/Validator.cs.artect` — imperative validator body with a `Validate()` method returning `ValidationResult`.
- [ ] `Files/ValidationResult.cs.artect` — shared `ValidationResult` + `ValidationError` used by validators.
- [ ] `Files/ValidationError.cs.artect` — record `(string Field, string Code, string Message)`.
- [ ] `Files/ApiProblem.cs.artect` — record for typed 400 body.
- [ ] `Files/PagedResponse.cs.artect` — `PagedResponse<T>` with `Items`, `Page`, `PageSize`, `TotalCount`.
- [ ] `Files/Mapper.cs.artect` — static class with `ToDto`, `ToEntity`, `ToResponse`, `ToRequest` extension methods; `partial void OnMapped(...)` hooks.
- [ ] `Files/MinimalApiEndpoints.cs.artect` — static `Map<Plural>Endpoints` method with one `MapGet`/`MapPost`/etc. per enabled CRUD op; body depends on `I<Entity>Repository` when repositories are on, on `DbContext`/`IDbConnectionFactory` otherwise.
- [ ] `Files/DbContext.cs.artect` — `DbSet<T>` per entity (using schema-prefixed names from `DbSetNaming` when collided); `OnModelCreating` configures schemas, PKs, FKs, unique indexes, sequences; fully-qualified type references when entity names collide across schemas.
- [ ] `Files/SqlDbConnectionFactory.cs.artect` — thin `IDbConnectionFactory` + implementation for Dapper path.
- [ ] `Files/RepositoryInterface.cs.artect` — `I<Entity>Repository` in Application; methods `ListAsync(page, pageSize, ct)`, `GetByIdAsync(id, ct)`, `CreateAsync(entity, ct)`, `UpdateAsync(entity, ct)`, `DeleteAsync(id, ct)`. For views and pk-less tables, only `ListAsync`.
- [ ] `Files/EfRepository.cs.artect` — EF Core implementation; constructor injects `<Name>DbContext`; uses `DbSet<T>` directly.
- [ ] `Files/DapperRepository.cs.artect` — Dapper implementation; constructor injects `IDbConnectionFactory`; parameterized SQL only.
- [ ] `Files/ProgramCs.cs.artect` — `WebApplication.CreateBuilder`, OpenAPI, Scalar, DbContext/connection factory DI, repository DI (when on), validator DI, auth DI, versioning DI, `Map<Plural>Endpoints()` per entity, `app.Run()`.
- [ ] `Files/AuthJwt.cs.artect`, `Files/AuthAuth0.cs.artect`, `Files/AuthAzureAd.cs.artect`, `Files/AuthApiKey.cs.artect` — fragment templates included by ProgramCs; placeholders throw at startup when unset.
- [ ] `Files/VersioningUrlSegment.cs.artect`, `Files/VersioningHeader.cs.artect`, `Files/VersioningQueryString.cs.artect` — fragment templates included by ProgramCs.
- [ ] `Files/AppSettings.cs.artect` — `appsettings.json` + `appsettings.Development.json`; one ConnectionStrings section, one default logging section.
- [ ] `Files/LaunchSettings.cs.artect` — `launchSettings.json`; HTTP + HTTPS profiles with ports 5080/5443 or similar; `DOTNET_ENVIRONMENT=Development`.
- [ ] `Files/Csproj.cs.artect` — five csproj variants (Api, Application, Domain, Infrastructure, Shared). `TreatWarningsAsErrors=true`, `Nullable=enable`.
- [ ] `Files/Sln.cs.artect` — a `.sln` body rendered by feeding GUIDs that are deterministically derived from project paths.
- [ ] `Files/Dockerfile.cs.artect`, `Files/DockerCompose.cs.artect` — multi-stage Dockerfile (SDK → aspnet); compose with SQL Server 2022 + healthcheck + volume.
- [ ] `Files/MigrationScript.cs.artect` — PowerShell + bash variants; install `dotnet-ef`, `dotnet ef migrations add InitialCreate`, generate idempotent bootstrap SQL.
- [ ] `Files/TestsProject.cs.artect` — xUnit + `WebApplicationFactory` + EF Core InMemory scaffolding; per-entity validator tests and endpoint smokes as PRD §4.11.
- [ ] `Files/Gitignore.cs.artect`, `Files/Editorconfig.cs.artect`, `Files/Readme.cs.artect` — repo hygiene files.
- [ ] `Files/StoredProceduresInterface.cs.artect` — `IStoredProcedures` (or one per schema when `PartitionStoredProceduresBySchema`); typed parameter and result classes inline or as siblings.
- [ ] `Files/DbFunctionsInterface.cs.artect` — `IDbFunctions` with signatures; bodies throw `NotImplementedException`.

### Task 8.4: Compile + commit Phase 8

- [ ] **Step 1: Build**

Run: `dotnet build Artect.sln`
Expected: green. (Templates are data; build should not light up anything new.)

- [ ] **Step 2: Commit**

```bash
git add -- src/Artect.Templates/
git commit -m "feat(templates): embed .artect template files"
```

---

## Phase 9 — Emitters (Artect.Generation)

**Goal:** One emitter per template. Every emitter is an `IEmitter` returning `EmittedFile` records.

**Uniform pattern:** every emitter builds a context object (plain POCO or anonymous object) with the properties the template expects, loads the template via `ctx.Templates.Load("<LogicalName>.artect")`, parses it (`TemplateParser.Parse`), and renders it (`Renderer.Render`). The result is wrapped into an `EmittedFile` with a `CleanLayout`-derived path.

**Canonical example — `EntityEmitter`:**

**Files:**
- Create: `src/Artect.Generation/Emitters/EntityEmitter.cs`

```csharp
using System.Collections.Generic;
using System.Linq;
using Artect.Core.Schema;
using Artect.Templating;

namespace Artect.Generation.Emitters;

public sealed class EntityEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var template = TemplateParser.Parse(ctx.Templates.Load("Entity.cs.artect"));
        var list = new List<EmittedFile>();
        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.IsJoinTable) continue;
            if (!entity.HasPrimaryKey) continue;
            var data = new
            {
                HasUsingNamespaces = false,
                UsingNamespaces = System.Array.Empty<string>(),
                Namespace = $"{CleanLayout.DomainNamespace(ctx.Config.ProjectName)}.Entities",
                EntityName = entity.EntityTypeName,
                Columns = entity.Table.Columns.Select(c => new
                {
                    ClrTypeWithNullability = ClrTypeString(c),
                    PropertyName = Artect.Naming.EntityNaming.PropertyName(c),
                    Initializer = c.ClrType == ClrType.String && !c.IsNullable ? " = string.Empty;" : string.Empty,
                }).ToList(),
                HasReferenceNavigations = entity.ReferenceNavigations.Count > 0,
                ReferenceNavigations = entity.ReferenceNavigations.Select(n => new
                {
                    TypeName = n.TargetEntityTypeName,
                    PropertyName = n.PropertyName,
                }).ToList(),
                HasCollectionNavigations = entity.CollectionNavigations.Count > 0,
                CollectionNavigations = entity.CollectionNavigations.Select(n => new
                {
                    TypeName = n.TargetEntityTypeName,
                    PropertyName = n.PropertyName,
                }).ToList(),
            };
            var rendered = Renderer.Render(template, data);
            var path = CleanLayout.EntityPath(ctx.Config.ProjectName, entity.EntityTypeName);
            list.Add(new EmittedFile(path, rendered));
        }
        return list;
    }

    static string ClrTypeString(Column c)
    {
        var cs = SqlTypeMap.ToCs(c.ClrType);
        if (c.IsNullable && SqlTypeMap.IsValueType(c.ClrType)) return cs + "?";
        if (c.IsNullable && c.ClrType == ClrType.String) return cs + "?";
        return cs;
    }
}
```

### Task 9.1–9.N: The rest of the emitters

Each of the following is a single task. Create the emitter class in `src/Artect.Generation/Emitters/` with the same shape as `EntityEmitter` above. Load the corresponding template from Phase 8. Build a context object tailored to the template. Return the `EmittedFile`(s) at the `CleanLayout` path. The ApiSmith emitter at the cited path is the adaptation reference.

**For every emitter below, follow this protocol:**

1. Read the ApiSmith reference emitter for logic shape.
2. Read the PRD section for the functional requirements.
3. Read the corresponding Phase-8 template for the context properties it requires.
4. Implement the Artect emitter.
5. `dotnet build` — green.
6. Commit with message `feat(generation): add <EmitterName>`.

| # | Emitter | Template | Path | PRD | ApiSmith ref |
|---|---------|----------|------|-----|---|
| 9.1 | `DtoEmitter` | `Dto.cs.artect` | `CleanLayout.DtoPath` | §4.6 | `src/ApiSmith.Generation/Emitters/DtoEmitter.cs` |
| 9.2 | `RequestEmitter` | `Request.cs.artect` | `CleanLayout.SharedRequestPath` (Create+Update variants) | §4.13 | `RequestEmitter.cs` |
| 9.3 | `ResponseEmitter` | `Response.cs.artect` | `CleanLayout.SharedResponsePath` | §4.13 | `ResponseEmitter.cs` |
| 9.4 | `EnumEmitter` | `Enum.cs.artect` | `CleanLayout.SharedEnumPath` | §4.13 | `EnumEmitter.cs` |
| 9.5 | `ValidationResultEmitter` | `ValidationResult.cs.artect` | `{ApplicationDir}/Validators/ValidationResult.cs` | §4.5 | `ValidationResultEmitter.cs` |
| 9.6 | `ValidationErrorEmitter` | `ValidationError.cs.artect` | `CleanLayout.SharedErrorPath("ValidationError")` | §4.13 | `ValidationErrorEmitter.cs` |
| 9.7 | `ApiProblemEmitter` | `ApiProblem.cs.artect` | `CleanLayout.SharedErrorPath("ApiProblem")` | §4.13 | `ApiProblemEmitter.cs` |
| 9.8 | `PagedResponseEmitter` | `PagedResponse.cs.artect` | `CleanLayout.SharedPagedResponsePath` | §4.13 | `PagedResponseEmitter.cs` |
| 9.9 | `ValidatorEmitter` | `Validator.cs.artect` | `CleanLayout.ValidatorPath` (per Create/Update) | §4.5 | `ValidatorEmitter.cs` |
| 9.10 | `MapperEmitter` | `Mapper.cs.artect` | `CleanLayout.MapperPath` | §4.6 | `MapperEmitter.cs` |
| 9.11 | `MinimalApiEndpointEmitter` | `MinimalApiEndpoints.cs.artect` | `CleanLayout.EndpointPath` | §4.4 | `MinimalApiEndpointEmitter.cs` |
| 9.12 | `DbContextEmitter` | `DbContext.cs.artect` | `CleanLayout.DbContextPath` | §4.3 | `DbContextEmitter.cs` |
| 9.13 | `DapperConnectionFactoryEmitter` | `SqlDbConnectionFactory.cs.artect` | `CleanLayout.ConnectionFactoryPath` | §4.3 | `DapperConnectionFactoryEmitter.cs` |
| 9.14 | `RepositoryInterfaceEmitter` | `RepositoryInterface.cs.artect` | `CleanLayout.RepositoryInterfacePath` | §4.7 | **NEW** — not in ApiSmith yet |
| 9.15 | `EfRepositoryEmitter` | `EfRepository.cs.artect` | `CleanLayout.RepositoryImplPath` | §4.7 | **NEW** |
| 9.16 | `DapperRepositoryImplEmitter` | `DapperRepository.cs.artect` | `CleanLayout.RepositoryImplPath` | §4.7 | Extract from `DapperRepositoryEmitter.cs` |
| 9.17 | `DbFunctionsEmitter` | `DbFunctionsInterface.cs.artect` | `CleanLayout.DbFunctionsInterfacePath` | §4.9 | `DbFunctionsEmitter.cs` |
| 9.18 | `StoredProceduresEmitter` | `StoredProceduresInterface.cs.artect` | `CleanLayout.SprocInterfacePath` | §4.9 | `StoredProceduresEmitter.cs` |
| 9.19 | `ProgramCsEmitter` | `ProgramCs.cs.artect` | `CleanLayout.ProgramCsPath` | §4.4 | `ProgramCsEmitter.cs` |
| 9.20 | `AuthEmitter` | `AuthJwt.cs.artect` / `AuthAuth0.cs.artect` / `AuthAzureAd.cs.artect` / `AuthApiKey.cs.artect` | fragments consumed by `ProgramCsEmitter` | §4.1 / open-Q | `AuthEmitter.cs` |
| 9.21 | `VersioningEmitter` | `VersioningUrlSegment.cs.artect` etc. | fragments consumed by `ProgramCsEmitter` | §4.2 | `VersioningEmitter.cs` |
| 9.22 | `AppSettingsEmitter` | `AppSettings.cs.artect` | `CleanLayout.AppSettingsPath` | §4.3 | `AppSettingsEmitter.cs` |
| 9.23 | `LaunchSettingsEmitter` | `LaunchSettings.cs.artect` | `CleanLayout.LaunchSettingsPath` | §4.3 | `LaunchSettingsEmitter.cs` |
| 9.24 | `CsProjEmitter` | `Csproj.cs.artect` | five project csprojs | §4.3 | `CsProjEmitter.cs` |
| 9.25 | `SlnEmitter` | `Sln.cs.artect` | `<root>/<ProjectName>.sln` | §4.3 | `SlnEmitter.cs` |
| 9.26 | `DockerEmitter` | `Dockerfile.cs.artect`, `DockerCompose.cs.artect` | `<root>/Dockerfile`, `<root>/docker-compose.yml` | §4.12 | `DockerEmitter.cs` |
| 9.27 | `MigrationsEmitter` | `MigrationScript.cs.artect` | `<root>/scripts/add-initial-migration.ps1` + `.sh` | §4.10 | `MigrationsEmitter.cs` |
| 9.28 | `TestsProjectEmitter` | `TestsProject.cs.artect` | `CleanLayout.TestsDir` | §4.11 | `TestsProjectEmitter.cs` |
| 9.29 | `RepoHygieneEmitter` | `Gitignore.cs.artect`, `Editorconfig.cs.artect`, `Readme.cs.artect` | `<root>/.gitignore`, `<root>/.editorconfig`, `<root>/README.md` | §4.12 | `RepoHygieneEmitter.cs` |
| 9.30 | `ArtectConfigEmitter` | none (uses `YamlWriter` directly) | `<root>/artect.yaml` | §3 | `ApiSmithConfigEmitter.cs` |

**Per-emitter notes:**

- **`RequestEmitter` / `ResponseEmitter` / `EnumEmitter`:** emit into `Shared/` project, BCL-only. No `Microsoft.AspNetCore.*` anywhere in the Shared project.
- **`ValidatorEmitter`:** derive rules from schema — `NOT NULL` string → required; `nvarchar(N)` → max-length; `CHECK` translations. Untranslatable `CHECK` → `// TODO: translate check constraint '<name>': <expression>`. When `validateForeignKeyReferences` is on + required FK, add a `default`-value check with `// TODO` to wire the existence query.
- **`MinimalApiEndpointEmitter`:** emit one lambda per CRUD operation bit that is set in `cfg.Crud`. Lambdas depend on `I<Entity>Repository` when `EmitRepositoriesAndAbstractions` is true, otherwise on `DbContext` / `IDbConnectionFactory` directly. Paging defaults: `page=1`, `pageSize=50`, extension-point comment in the list lambda.
- **`RepositoryInterfaceEmitter`:** for pk-less tables and views, emit only `ListAsync`. Signature is data-access-agnostic.
- **`EfRepositoryEmitter` / `DapperRepositoryImplEmitter`:** Only one is active per scaffold — driven by `cfg.DataAccess`. The emitter short-circuits when it is not the active path.
- **`DbContextEmitter`:** EF Core only; emit nothing when `DataAccess == Dapper`. Uses `DbSetNaming` for cross-schema collision resolution; emits fully-qualified type names in `OnModelCreating` when collision is detected.
- **`AuthEmitter` / `VersioningEmitter`:** these are "fragment" emitters that don't emit standalone files; they publish rendered strings into `EmitterContext`'s per-run memo that `ProgramCsEmitter` reads. Two options: (a) keep them as IEmitters that return zero files but set a property on context; (b) let `ProgramCsEmitter` include the right fragment template directly. Option (b) is simpler — implement it by having `ProgramCsEmitter` pick one of the `Auth*.cs.artect` / `Versioning*.cs.artect` templates based on config and render it as a sub-context into the Program.cs top-level template's `{{ AuthFragment }}` / `{{ VersioningFragment }}` variables. Drop the `AuthEmitter` / `VersioningEmitter` IEmitters from the registry in that case.
- **`MigrationsEmitter`:** emit only when `cfg.GenerateInitialMigration && cfg.DataAccess == EfCore`.
- **`TestsProjectEmitter`:** emit only when `cfg.IncludeTestsProject`. Per PRD §4.11, the test project packages are xunit, Microsoft.AspNetCore.Mvc.Testing, Microsoft.EntityFrameworkCore.InMemory. Validator tests for every entity; endpoint smokes for the EF Core path only (Dapper needs a real DB, so skip for Dapper).
- **`DockerEmitter`:** emit only when `cfg.IncludeDockerAssets`.
- **`RepoHygieneEmitter`:** emits `.gitignore` + `.editorconfig` always; emits `README.md` always.
- **`ArtectConfigEmitter`:** serializes `cfg` to YAML and writes to `<root>/artect.yaml`. Never writes a `connectionString` key.

### Task 9.31: Emitter registration

**Files:**
- Create: `src/Artect.Generation/EmitterRegistry.cs`

- [ ] **Step 1: Build the static list of emitters**

```csharp
using System.Collections.Generic;
using Artect.Generation.Emitters;

namespace Artect.Generation;

public static class EmitterRegistry
{
    public static IReadOnlyList<IEmitter> All() => new IEmitter[]
    {
        new ApiProblemEmitter(),
        new AppSettingsEmitter(),
        new ArtectConfigEmitter(),
        new CsProjEmitter(),
        new DapperConnectionFactoryEmitter(),
        new DapperRepositoryImplEmitter(),
        new DbContextEmitter(),
        new DbFunctionsEmitter(),
        new DockerEmitter(),
        new DtoEmitter(),
        new EfRepositoryEmitter(),
        new EntityEmitter(),
        new EnumEmitter(),
        new LaunchSettingsEmitter(),
        new MapperEmitter(),
        new MigrationsEmitter(),
        new MinimalApiEndpointEmitter(),
        new PagedResponseEmitter(),
        new ProgramCsEmitter(),
        new RepoHygieneEmitter(),
        new RepositoryInterfaceEmitter(),
        new RequestEmitter(),
        new ResponseEmitter(),
        new SlnEmitter(),
        new StoredProceduresEmitter(),
        new TestsProjectEmitter(),
        new ValidationErrorEmitter(),
        new ValidationResultEmitter(),
        new ValidatorEmitter(),
    };
}
```

### Task 9.32: Compile + commit Phase 9

- [ ] **Step 1: Build**

Run: `dotnet build Artect.sln`
Expected: green.

- [ ] **Step 2: Final commit for this phase**

```bash
git add -- src/Artect.Generation/
git commit -m "feat(generation): all emitters"
```

---
## Phase 10 — Console (Artect.Console)

**Goal:** `ConsoleIO` abstraction (so the wizard is testable and works across Windows/Linux/macOS), ANSI helpers, and `WizardRunner` with 15 prompts.

### Task 10.1: `ConsoleIO`

**Files:**
- Create: `src/Artect.Console/IConsoleIO.cs`
- Create: `src/Artect.Console/ConsoleIO.cs`

- [ ] **Step 1: `IConsoleIO.cs`**

```csharp
namespace Artect.Console;

public interface IConsoleIO
{
    void Write(string text);
    void WriteLine(string text);
    string ReadLine();
}
```

- [ ] **Step 2: `ConsoleIO.cs`**

```csharp
namespace Artect.Console;

public sealed class ConsoleIO : IConsoleIO
{
    public void Write(string text) => System.Console.Write(text);
    public void WriteLine(string text) => System.Console.WriteLine(text);
    public string ReadLine() => System.Console.ReadLine() ?? string.Empty;
}
```

### Task 10.2: ANSI color helpers

**Files:**
- Create: `src/Artect.Console/Ansi.cs`

- [ ] **Step 1:**

```csharp
namespace Artect.Console;

public static class Ansi
{
    public const string Reset = "[0m";
    public const string Bold = "[1m";
    public const string Dim = "[2m";
    public const string Cyan = "[36m";
    public const string Green = "[32m";
    public const string Yellow = "[33m";
    public const string Red = "[31m";

    public static string Colour(string text, string colour) => colour + text + Reset;
}
```

### Task 10.3: Prompt helpers

**Files:**
- Create: `src/Artect.Console/Prompt.cs`

- [ ] **Step 1:**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Artect.Console;

public sealed class Prompt
{
    readonly IConsoleIO _io;
    public Prompt(IConsoleIO io) => _io = io;

    public string AskString(string question, string defaultValue)
    {
        _io.Write($"{Ansi.Colour(question, Ansi.Cyan)} [{defaultValue}]: ");
        var answer = _io.ReadLine().Trim();
        return string.IsNullOrEmpty(answer) ? defaultValue : answer;
    }

    public bool AskBool(string question, bool defaultValue)
    {
        var def = defaultValue ? "Y/n" : "y/N";
        _io.Write($"{Ansi.Colour(question, Ansi.Cyan)} [{def}]: ");
        var answer = _io.ReadLine().Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(answer)) return defaultValue;
        return answer is "y" or "yes" or "true" or "1";
    }

    public T AskEnum<T>(string question, T defaultValue) where T : struct, Enum
    {
        var names = Enum.GetNames<T>().OrderBy(n => n, StringComparer.Ordinal).ToList();
        _io.WriteLine(Ansi.Colour(question, Ansi.Cyan));
        for (int i = 0; i < names.Count; i++)
        {
            var marker = names[i] == defaultValue.ToString() ? " (default)" : string.Empty;
            _io.WriteLine($"  {i + 1}. {names[i]}{marker}");
        }
        _io.Write("Choice (number or name): ");
        var answer = _io.ReadLine().Trim();
        if (string.IsNullOrEmpty(answer)) return defaultValue;
        if (int.TryParse(answer, out var n) && n >= 1 && n <= names.Count)
            return Enum.Parse<T>(names[n - 1]);
        if (Enum.TryParse<T>(answer, ignoreCase: true, out var parsed)) return parsed;
        _io.WriteLine(Ansi.Colour("  Unrecognized — using default.", Ansi.Dim));
        return defaultValue;
    }

    public IReadOnlyList<T> AskMultiEnum<T>(string question, IReadOnlyList<T> defaults) where T : struct, Enum
    {
        var names = Enum.GetNames<T>()
            .Where(n => n != "None" && n != "All")
            .OrderBy(n => n, StringComparer.Ordinal).ToList();
        _io.WriteLine(Ansi.Colour(question, Ansi.Cyan));
        for (int i = 0; i < names.Count; i++) _io.WriteLine($"  {i + 1}. {names[i]}");
        _io.WriteLine($"(default: {string.Join(", ", defaults.Select(d => d.ToString()))})");
        _io.Write("Choices (comma-separated numbers or names, or blank for default): ");
        var answer = _io.ReadLine().Trim();
        if (string.IsNullOrEmpty(answer)) return defaults;
        var result = new List<T>();
        foreach (var tok in answer.Split(','))
        {
            var t = tok.Trim();
            if (int.TryParse(t, out var n) && n >= 1 && n <= names.Count)
                result.Add(Enum.Parse<T>(names[n - 1]));
            else if (Enum.TryParse<T>(t, ignoreCase: true, out var parsed))
                result.Add(parsed);
        }
        return result.Count == 0 ? defaults : result;
    }

    public IReadOnlyList<string> AskMultiString(string question, IReadOnlyList<string> options, IReadOnlyList<string> defaults)
    {
        _io.WriteLine(Ansi.Colour(question, Ansi.Cyan));
        for (int i = 0; i < options.Count; i++) _io.WriteLine($"  {i + 1}. {options[i]}");
        _io.WriteLine($"(default: {string.Join(", ", defaults)})");
        _io.Write("Choices (comma-separated numbers, or blank for default): ");
        var answer = _io.ReadLine().Trim();
        if (string.IsNullOrEmpty(answer)) return defaults;
        var result = new List<string>();
        foreach (var tok in answer.Split(','))
        {
            if (int.TryParse(tok.Trim(), out var n) && n >= 1 && n <= options.Count)
                result.Add(options[n - 1]);
        }
        return result.Count == 0 ? defaults : result;
    }
}
```

### Task 10.4: `WizardRunner`

**Files:**
- Create: `src/Artect.Console/WizardRunner.cs`

- [ ] **Step 1: Write the 15-prompt wizard that returns an `ArtectConfig`**

```csharp
using System.Collections.Generic;
using Artect.Config;

namespace Artect.Console;

public sealed class WizardRunner
{
    readonly Prompt _prompt;
    readonly IConsoleIO _io;

    public WizardRunner(IConsoleIO io)
    {
        _io = io;
        _prompt = new Prompt(io);
    }

    public ArtectConfig Run(IReadOnlyList<string> availableSchemas)
    {
        _io.WriteLine(Ansi.Colour("Artect wizard", Ansi.Bold));
        _io.WriteLine(Ansi.Colour("──────────────", Ansi.Dim));

        var defaults = ArtectConfig.Defaults();
        var name = _prompt.AskString("1. Project name", defaults.ProjectName);
        var output = _prompt.AskString("2. Output directory", $"./{name}");
        var framework = _prompt.AskEnum("3. Target framework", defaults.TargetFramework);
        var dataAccess = _prompt.AskEnum("4. Data access", defaults.DataAccess);
        var repos = _prompt.AskBool("5. Create repositories and abstractions", defaults.EmitRepositoriesAndAbstractions);
        var label = _prompt.AskString("6. Generated-by label", defaults.GeneratedByLabel);
        var migration = _prompt.AskBool("7. Generate initial migration", defaults.GenerateInitialMigration);
        var crudDefaults = new List<CrudOperation>
            { CrudOperation.GetList, CrudOperation.GetById, CrudOperation.Post, CrudOperation.Put, CrudOperation.Patch, CrudOperation.Delete };
        var crudList = _prompt.AskMultiEnum("8. CRUD operations", crudDefaults);
        CrudOperation crud = CrudOperation.None;
        foreach (var c in crudList) crud |= c;
        var versioning = _prompt.AskEnum("9. API versioning", defaults.ApiVersioning);
        var auth = _prompt.AskEnum("10. Authentication", defaults.Auth);
        var tests = _prompt.AskBool("11. Include tests project", defaults.IncludeTestsProject);
        var docker = _prompt.AskBool("12. Include Docker assets", defaults.IncludeDockerAssets);
        var partition = _prompt.AskBool("13. Partition stored-procedure interfaces by schema (Advanced)", defaults.PartitionStoredProceduresBySchema);
        var childCollections = _prompt.AskBool("14. Include one-to-many child collections in responses (Advanced)", defaults.IncludeChildCollectionsInResponses);
        var schemas = _prompt.AskMultiString("15. Schemas to include", availableSchemas, new[] { "dbo" });

        return defaults with
        {
            ProjectName = name,
            OutputDirectory = output,
            TargetFramework = framework,
            DataAccess = dataAccess,
            EmitRepositoriesAndAbstractions = repos,
            GeneratedByLabel = label,
            GenerateInitialMigration = migration,
            Crud = crud,
            ApiVersioning = versioning,
            Auth = auth,
            IncludeTestsProject = tests,
            IncludeDockerAssets = docker,
            PartitionStoredProceduresBySchema = partition,
            IncludeChildCollectionsInResponses = childCollections,
            Schemas = schemas,
        };
    }
}
```

### Task 10.5: Compile + commit Phase 10

- [ ] **Step 1: Build**

Run: `dotnet build Artect.sln`
Expected: green.

- [ ] **Step 2: Commit**

```bash
git add -- src/Artect.Console/
git commit -m "feat(console): ConsoleIO, Prompt, 15-step WizardRunner"
```

---
## Phase 11 — CLI (Artect.Cli)

**Goal:** `artect new` command with full flag parity, precedence resolution, connection-string resolution.

### Task 11.1: Argv model

**Files:**
- Create: `src/Artect.Cli/CliArguments.cs`

- [ ] **Step 1: Parse `--flag value` pairs into a dictionary**

```csharp
using System.Collections.Generic;

namespace Artect.Cli;

public sealed class CliArguments
{
    readonly Dictionary<string, string> _values;
    readonly HashSet<string> _flags;

    CliArguments(Dictionary<string, string> values, HashSet<string> flags)
    {
        _values = values;
        _flags = flags;
    }

    public static CliArguments Parse(string[] args)
    {
        var values = new Dictionary<string, string>(System.StringComparer.Ordinal);
        var flags = new HashSet<string>(System.StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--")) continue;
            var key = a.Substring(2);
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
            {
                values[key] = args[i + 1];
                i++;
            }
            else
            {
                flags.Add(key);
            }
        }
        return new CliArguments(values, flags);
    }

    public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;
    public bool Has(string key) => _values.ContainsKey(key) || _flags.Contains(key);
    public string Command => _values.TryGetValue("_command", out var c) ? c : string.Empty;
}
```

### Task 11.2: Connection resolution

**Files:**
- Create: `src/Artect.Cli/ConnectionResolver.cs`

- [ ] **Step 1:**

```csharp
using System;

namespace Artect.Cli;

public static class ConnectionResolver
{
    public static string Resolve(CliArguments args, string? fromConfigYaml)
    {
        var flag = args.Get("connection");
        if (!string.IsNullOrEmpty(flag)) return flag;
        var env = Environment.GetEnvironmentVariable("ARTECT_CONNECTION");
        if (!string.IsNullOrEmpty(env)) return env;
        if (!string.IsNullOrEmpty(fromConfigYaml)) return fromConfigYaml!;
        throw new System.InvalidOperationException(
            "No connection string provided. Pass --connection, set ARTECT_CONNECTION, or add connectionString: to artect.yaml.");
    }
}
```

### Task 11.3: Flag → `ArtectConfig` overrides

**Files:**
- Create: `src/Artect.Cli/ConfigOverrides.cs`

- [ ] **Step 1: Apply CLI flags on top of a base config**

```csharp
using System;
using System.Linq;
using Artect.Config;

namespace Artect.Cli;

public static class ConfigOverrides
{
    public static ArtectConfig Apply(CliArguments args, ArtectConfig baseline) => baseline with
    {
        ProjectName = args.Get("name") ?? baseline.ProjectName,
        OutputDirectory = args.Get("output") ?? baseline.OutputDirectory,
        TargetFramework = args.Get("framework") is { } f ? TargetFrameworkExtensions.FromMoniker(f) : baseline.TargetFramework,
        DataAccess = args.Get("data-access") is { } d ? Enum.Parse<DataAccessKind>(d, ignoreCase: true) : baseline.DataAccess,
        EmitRepositoriesAndAbstractions = ParseBool(args.Get("repositories"), baseline.EmitRepositoriesAndAbstractions),
        GeneratedByLabel = args.Get("generated-by") ?? baseline.GeneratedByLabel,
        GenerateInitialMigration = ParseBool(args.Get("generate-migration"), baseline.GenerateInitialMigration),
        Crud = args.Get("crud") is { } c ? ParseCrud(c) : baseline.Crud,
        ApiVersioning = args.Get("api-versioning") is { } v ? Enum.Parse<ApiVersioningKind>(v, ignoreCase: true) : baseline.ApiVersioning,
        Auth = args.Get("auth") is { } a ? Enum.Parse<AuthKind>(a, ignoreCase: true) : baseline.Auth,
        IncludeTestsProject = ParseBool(args.Get("tests"), baseline.IncludeTestsProject),
        IncludeDockerAssets = ParseBool(args.Get("docker"), baseline.IncludeDockerAssets),
        PartitionStoredProceduresBySchema = ParseBool(args.Get("partition-sprocs-by-schema"), baseline.PartitionStoredProceduresBySchema),
        IncludeChildCollectionsInResponses = ParseBool(args.Get("child-collections"), baseline.IncludeChildCollectionsInResponses),
        Schemas = args.Get("schemas") is { } s ? s.Split(',').Select(x => x.Trim()).ToList() : baseline.Schemas,
    };

    static bool ParseBool(string? s, bool fallback) =>
        s is null ? fallback : s.ToLowerInvariant() is "true" or "1" or "yes";

    static CrudOperation ParseCrud(string s)
    {
        var result = CrudOperation.None;
        foreach (var tok in s.Split(','))
        {
            if (Enum.TryParse<CrudOperation>(tok.Trim(), ignoreCase: true, out var v)) result |= v;
        }
        return result;
    }
}
```

### Task 11.4: `NewCommand`

**Files:**
- Create: `src/Artect.Cli/NewCommand.cs`

- [ ] **Step 1:**

```csharp
using System;
using System.IO;
using Artect.Config;
using Artect.Console;
using Artect.Generation;
using Artect.Introspection;

namespace Artect.Cli;

public sealed class NewCommand
{
    public int Run(CliArguments args)
    {
        var configPath = args.Get("config");
        ArtectConfig config;
        string? yamlConnection = null;

        if (configPath is not null)
        {
            config = YamlReader.ReadFile(configPath);
            yamlConnection = TryReadConnectionFromYaml(configPath);
        }
        else
        {
            var io = new ConsoleIO();
            var defaults = ArtectConfig.Defaults();
            var withFlags = ConfigOverrides.Apply(args, defaults);
            if (HasAllRequired(withFlags, args))
            {
                config = withFlags;
            }
            else
            {
                var connection = ConnectionResolver.Resolve(args, yamlConnection);
                var factory = new SqlConnectionFactory(connection);
                var probe = new SchemaProbe(factory);
                var available = probe.ListSchemas();
                var wizard = new WizardRunner(io);
                config = wizard.Run(available);
                config = ConfigOverrides.Apply(args, config);
            }
        }

        var connection2 = ConnectionResolver.Resolve(args, yamlConnection);
        var reader = new SqlServerSchemaReader(new SqlConnectionFactory(connection2));
        var graph = reader.Read(config.Schemas);
        var generator = new Generator(EmitterRegistry.All());
        var outputRoot = Path.GetFullPath(config.OutputDirectory);
        Directory.CreateDirectory(outputRoot);
        generator.Generate(config, graph, outputRoot);
        System.Console.WriteLine($"Generated scaffold at {outputRoot}");
        return 0;
    }

    static bool HasAllRequired(ArtectConfig cfg, CliArguments args) =>
        args.Has("name") && args.Has("output"); // minimal heuristic — expand as needed

    static string? TryReadConnectionFromYaml(string path)
    {
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.TrimStart();
            if (line.StartsWith("connectionString:", StringComparison.Ordinal))
            {
                var value = line.Substring("connectionString:".Length).Trim();
                if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                    value = value.Substring(1, value.Length - 2);
                return value;
            }
        }
        return null;
    }
}
```

### Task 11.5: `Program.cs` entry point

**Files:**
- Modify: `src/Artect.Cli/Program.cs`

- [ ] **Step 1: Replace the placeholder with real entry point**

```csharp
using System;

namespace Artect.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("artect — scaffolding CLI for Clean Architecture + Minimal API solutions.");
            Console.WriteLine("Usage: artect new [--config <path>] [--connection <string>] [flags...]");
            return 0;
        }
        var command = args[0];
        var cli = CliArguments.Parse(args);
        return command switch
        {
            "new" => new NewCommand().Run(cli),
            _ => Unknown(command),
        };
    }

    static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'. Expected 'new'.");
        return 2;
    }
}
```

### Task 11.6: Compile + commit Phase 11

- [ ] **Step 1: Build**

Run: `dotnet build Artect.sln`
Expected: green.

- [ ] **Step 2: Commit**

```bash
git add -- src/Artect.Cli/
git commit -m "feat(cli): artect new command with wizard, flags, and config replay"
```

---

## Phase 12 — CI, README, LICENSE

**Goal:** GitHub Actions workflows + top-level repo docs.

### Task 12.1: LICENSE

**Files:**
- Create: `LICENSE`

- [ ] **Step 1: MIT license**

Use the standard MIT license text with `Copyright (c) 2026 Art Laubach`. Any standard template is fine — see `spdx.org/licenses/MIT.html`.

### Task 12.2: README

**Files:**
- Create: `README.md`

- [ ] **Step 1: Short README**

```markdown
# Artect

Artect is a .NET global tool that scaffolds a Clean Architecture + Minimal API solution from an existing SQL Server database.

## Install

    dotnet tool install -g Artect

## Usage

    artect new --connection "Server=.;Database=MyDb;Trusted_Connection=True;TrustServerCertificate=True"

or replay a saved configuration:

    artect new --config artect.yaml --connection "..."

## Documentation

- Product spec: [`docs/can-you-write-a-soft-sprout.md`](docs/can-you-write-a-soft-sprout.md)
- Design: [`docs/superpowers/specs/2026-04-22-artect-design.md`](docs/superpowers/specs/2026-04-22-artect-design.md)

## License

MIT
```

### Task 12.3: CI workflow

**Files:**
- Create: `.github/workflows/ci.yml`

- [ ] **Step 1:**

```yaml
name: ci
on:
  push:
    branches: [main]
  pull_request:

jobs:
  build:
    strategy:
      fail-fast: false
      matrix:
        os: [ubuntu-latest, windows-latest, macos-latest]
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: |
            8.0.x
            9.0.x
      - run: dotnet restore
      - run: dotnet build --no-restore -c Release
```

### Task 12.4: Release workflow

**Files:**
- Create: `.github/workflows/release.yml`

- [ ] **Step 1:**

```yaml
name: release
on:
  push:
    tags: ['v*.*.*']

jobs:
  pack:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 9.0.x
      - run: dotnet restore
      - run: dotnet build --no-restore -c Release
      - run: dotnet pack src/Artect.Cli/Artect.Cli.csproj -c Release -o ./artifacts
      - run: dotnet nuget push ./artifacts/*.nupkg --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json
```

### Task 12.5: Compile + commit Phase 12

- [ ] **Step 1: Commit**

```bash
git add -- LICENSE README.md .github/
git commit -m "chore: add LICENSE, README, and GitHub Actions workflows"
```

---

## Phase 13 — Smoke test against a throwaway DB

**Goal:** Run the tool end-to-end against a real SQL Server schema, verify the output compiles.

### Task 13.1: Start a SQL Server container

- [ ] **Step 1: Launch a local SQL Server 2022 container for testing**

```bash
docker run --rm -d --name artect-smoke -p 14336:1433 \
  -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=ArtectSmoke!23 \
  mcr.microsoft.com/mssql/server:2022-latest
```

### Task 13.2: Seed a small schema

**Files:**
- Create: `tools/smoke-seed.sql` (temporary scratch, not committed)

- [ ] **Step 1: Create a Users + Posts fixture**

```sql
CREATE DATABASE ArtectSmoke;
GO
USE ArtectSmoke;
GO
CREATE TABLE dbo.Users (
  Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
  Email nvarchar(256) NOT NULL,
  DisplayName nvarchar(128) NULL,
  CreatedUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE TABLE dbo.Posts (
  Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
  AuthorId int NOT NULL,
  Title nvarchar(256) NOT NULL,
  Body nvarchar(max) NULL,
  Status nvarchar(16) NOT NULL DEFAULT 'draft'
    CHECK (Status IN ('draft','published','archived')),
  CreatedUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
  CONSTRAINT FK_Posts_Users FOREIGN KEY (AuthorId) REFERENCES dbo.Users(Id)
);
```

Run with: `sqlcmd -S localhost,14336 -U sa -P 'ArtectSmoke!23' -i tools/smoke-seed.sql`

### Task 13.3: Run the tool

- [ ] **Step 1: Scripted run against the fixture**

```bash
dotnet run --project src/Artect.Cli -- new \
  --name SmokeApi --output ./out/SmokeApi \
  --connection "Server=localhost,14336;Database=ArtectSmoke;User Id=sa;Password=ArtectSmoke!23;TrustServerCertificate=True" \
  --framework net9.0 --data-access EfCore --repositories true \
  --api-versioning None --auth None --tests true --docker true \
  --schemas dbo
```

Expected: a scaffold appears at `./out/SmokeApi/` with the nine-project tree from PRD §4.3.

### Task 13.4: Verify output compiles

- [ ] **Step 1: Build the generated scaffold**

```bash
dotnet build ./out/SmokeApi/SmokeApi.sln -c Release
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

### Task 13.5: Replay determinism (manual check)

- [ ] **Step 1: Run a second time into a different directory**

```bash
dotnet run --project src/Artect.Cli -- new \
  --config ./out/SmokeApi/artect.yaml \
  --connection "Server=localhost,14336;Database=ArtectSmoke;User Id=sa;Password=ArtectSmoke!23;TrustServerCertificate=True" \
  --output ./out/SmokeApi2
```

- [ ] **Step 2: Diff the two outputs**

```bash
diff -r ./out/SmokeApi ./out/SmokeApi2
```

Expected: empty diff (or differences limited to path-prefixed strings that were correctly parameterized on the output path — see spec Appendix B).

### Task 13.6: Clean up

- [ ] **Step 1: Stop the container**

```bash
docker stop artect-smoke
```

### Task 13.7: Commit

- [ ] **Step 1: Commit the plan's final state**

```bash
git add -- docs/superpowers/plans/
git commit -m "docs: add implementation plan"
```

---

## Closing notes

- Execution order: Phases 0–12 are strictly sequential. Phase 13 is optional verification.
- Each phase's commits are independent — pausing between phases is safe.
- If a phase gets too big, split its tasks across multiple sessions. The `- [ ]` checkboxes make resumption straightforward.
- Prefer completing a phase's `dotnet build` before moving on. A red build at the end of a phase means something in that phase is wrong, not something in the next phase.
- When implementing Phase 9's emitters, the ApiSmith source is the authoritative reference for shape details that this plan doesn't spell out. The PRD is authoritative for behavior.
