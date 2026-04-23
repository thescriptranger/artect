using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits <c>IStoredProcedures</c> (and optionally per-schema variants when
/// <c>cfg.PartitionStoredProceduresBySchema</c> is true) plus a companion
/// implementation class. Only runs when the graph contains stored procedures.
///
/// <list type="bullet">
/// <item>Parameter classes are emitted inline for each procedure.</item>
/// <item>Result classes are inferred from <c>ResultColumns</c>; when
///   <c>ResultInference == Indeterminate</c> a stub result class with a
///   TODO comment is emitted.</item>
/// <item>EF Core path: implementation returns <c>Array.Empty&lt;T&gt;()</c>
///   with a TODO comment.</item>
/// <item>Dapper path: implementation uses fully parameterized Dapper calls.</item>
/// </list>
/// </summary>
public sealed class StoredProceduresEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        if (ctx.Graph.StoredProcedures.Count == 0)
            return System.Array.Empty<EmittedFile>();

        var list    = new List<EmittedFile>();
        var project = ctx.Config.ProjectName;
        var ns      = $"{CleanLayout.ApplicationNamespace(project)}.StoredProcedures";
        var da      = ctx.Config.DataAccess;

        if (ctx.Config.PartitionStoredProceduresBySchema)
        {
            var bySchema = ctx.Graph.StoredProcedures
                .GroupBy(sp => sp.Schema, System.StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, System.StringComparer.Ordinal);

            foreach (var schemaGroup in bySchema)
            {
                var schemaPascal = CasingHelper.ToPascalCase(schemaGroup.Key);
                var ifaceName    = $"I{schemaPascal}StoredProcedures";
                var implName     = $"{schemaPascal}StoredProcedures";
                var sprocs       = schemaGroup.OrderBy(sp => sp.Name, System.StringComparer.Ordinal).ToList();

                EmitPair(list, project, ns, da, ifaceName, implName, sprocs);
            }
        }
        else
        {
            var sprocs = ctx.Graph.StoredProcedures
                .OrderBy(sp => sp.Schema, System.StringComparer.Ordinal)
                .ThenBy(sp => sp.Name, System.StringComparer.Ordinal)
                .ToList();

            EmitPair(list, project, ns, da, "IStoredProcedures", "StoredProcedures", sprocs);
        }

        return list;
    }

    // ── Core emitter ─────────────────────────────────────────────────────────

    static void EmitPair(
        List<EmittedFile> list,
        string project,
        string ns,
        DataAccessKind da,
        string ifaceName,
        string implName,
        IReadOnlyList<StoredProcedure> sprocs)
    {
        // Collect distinct result-class types needed.
        var resultTypes = BuildResultTypes(sprocs);

        // ── Interface file ────────────────────────────────────────────────────
        var ifaceSb = new StringBuilder();
        ifaceSb.AppendLine($"using System.Collections.Generic;");
        ifaceSb.AppendLine($"using System.Threading;");
        ifaceSb.AppendLine($"using System.Threading.Tasks;");
        ifaceSb.AppendLine();
        ifaceSb.AppendLine($"namespace {ns};");
        ifaceSb.AppendLine();

        // Emit parameter + result record types first (they're referenced by the interface).
        foreach (var sproc in sprocs)
        {
            EmitParameterClass(ifaceSb, sproc);
            EmitResultClass(ifaceSb, sproc, resultTypes);
        }

        ifaceSb.AppendLine($"/// <summary>Typed wrappers for stored procedures.</summary>");
        ifaceSb.AppendLine($"public interface {ifaceName}");
        ifaceSb.AppendLine("{");
        foreach (var sproc in sprocs)
        {
            var methodName = MethodName(sproc);
            var paramType  = ParamTypeName(sproc);
            var resultType = ResultTypeName(sproc, resultTypes);
            ifaceSb.AppendLine($"    /// <summary>Executes [{sproc.Schema}].[{sproc.Name}].</summary>");
            ifaceSb.AppendLine($"    Task<IReadOnlyList<{resultType}>> {methodName}({paramType} parameters, CancellationToken ct = default);");
        }
        ifaceSb.AppendLine("}");

        var ifacePath = CleanLayout.SprocInterfacePath(project, ifaceName);
        list.Add(new EmittedFile(ifacePath, ifaceSb.ToString()));

        // ── Implementation file ───────────────────────────────────────────────
        var implSb = new StringBuilder();

        if (da == DataAccessKind.Dapper)
        {
            implSb.AppendLine("using Dapper;");
        }
        implSb.AppendLine("using System.Collections.Generic;");
        implSb.AppendLine("using System.Threading;");
        implSb.AppendLine("using System.Threading.Tasks;");
        implSb.AppendLine();
        implSb.AppendLine($"namespace {ns};");
        implSb.AppendLine();
        implSb.AppendLine($"/// <summary>Implementation of <see cref=\"{ifaceName}\"/>.</summary>");

        if (da == DataAccessKind.Dapper)
        {
            var infraDataNs = $"{CleanLayout.InfrastructureNamespace(project)}.Data";
            implSb.AppendLine($"// ReSharper disable once RedundantUsingDirective — IDbConnectionFactory comes from {infraDataNs}.");
            implSb.AppendLine($"public sealed class {implName} : {ifaceName}");
            implSb.AppendLine("{");
            implSb.AppendLine($"    private readonly {infraDataNs}.IDbConnectionFactory _connections;");
            implSb.AppendLine();
            implSb.AppendLine($"    public {implName}({infraDataNs}.IDbConnectionFactory connections)");
            implSb.AppendLine("        => _connections = connections;");
            implSb.AppendLine();

            foreach (var sproc in sprocs)
            {
                var methodName = MethodName(sproc);
                var paramType  = ParamTypeName(sproc);
                var resultType = ResultTypeName(sproc, resultTypes);
                implSb.AppendLine($"    /// <inheritdoc />");
                implSb.AppendLine($"    public async Task<IReadOnlyList<{resultType}>> {methodName}({paramType} parameters, CancellationToken ct = default)");
                implSb.AppendLine("    {");
                implSb.AppendLine("        using var conn = _connections.CreateOpenConnection();");
                implSb.AppendLine($"        var results = await conn.QueryAsync<{resultType}>(");
                implSb.AppendLine($"            \"[{sproc.Schema}].[{sproc.Name}]\",");
                implSb.AppendLine($"            parameters,");
                implSb.AppendLine("            commandType: System.Data.CommandType.StoredProcedure).ConfigureAwait(false);");
                implSb.AppendLine("        return results.AsList();");
                implSb.AppendLine("    }");
                implSb.AppendLine();
            }
        }
        else // EF Core
        {
            implSb.AppendLine($"public sealed class {implName} : {ifaceName}");
            implSb.AppendLine("{");

            foreach (var sproc in sprocs)
            {
                var methodName = MethodName(sproc);
                var paramType  = ParamTypeName(sproc);
                var resultType = ResultTypeName(sproc, resultTypes);
                implSb.AppendLine($"    /// <inheritdoc />");
                implSb.AppendLine($"    public Task<IReadOnlyList<{resultType}>> {methodName}({paramType} parameters, CancellationToken ct = default)");
                implSb.AppendLine("    {");
                implSb.AppendLine("        // TODO: EF Core sprocs are case-by-case — wire via FromSqlRaw or ExecuteSqlRaw.");
                implSb.AppendLine($"        return Task.FromResult<IReadOnlyList<{resultType}>>(System.Array.Empty<{resultType}>());");
                implSb.AppendLine("    }");
                implSb.AppendLine();
            }
        }

        implSb.AppendLine("}");

        var implPath = CleanLayout.SprocInterfacePath(project, implName);
        list.Add(new EmittedFile(implPath, implSb.ToString()));
    }

    // ── Result-type registry ──────────────────────────────────────────────────

    /// <summary>
    /// Maps sproc qualified name to its result-type C# class name.
    /// For sprocs with <c>ResultInference == Empty</c> we still emit a stub.
    /// </summary>
    static Dictionary<string, string> BuildResultTypes(IReadOnlyList<StoredProcedure> sprocs)
    {
        var map = new Dictionary<string, string>(System.StringComparer.Ordinal);
        foreach (var sproc in sprocs)
        {
            var key  = $"{sproc.Schema}.{sproc.Name}";
            var name = $"{CasingHelper.ToPascalCase(sproc.Name)}Result";
            map[key] = name;
        }
        return map;
    }

    static string ResultTypeName(StoredProcedure sproc, Dictionary<string, string> resultTypes)
        => resultTypes[$"{sproc.Schema}.{sproc.Name}"];

    static string ParamTypeName(StoredProcedure sproc)
        => sproc.Parameters.Count == 0
            ? "object" // no params — pass empty object
            : $"{CasingHelper.ToPascalCase(sproc.Name)}Parameters";

    static string MethodName(StoredProcedure sproc)
        => CasingHelper.ToPascalCase(sproc.Name) + "Async";

    // ── Code generators ───────────────────────────────────────────────────────

    static void EmitParameterClass(StringBuilder sb, StoredProcedure sproc)
    {
        if (sproc.Parameters.Count == 0) return; // no param class needed

        var className = $"{CasingHelper.ToPascalCase(sproc.Name)}Parameters";
        sb.AppendLine($"/// <summary>Parameters for [{sproc.Schema}].[{sproc.Name}].</summary>");
        sb.AppendLine($"public sealed class {className}");
        sb.AppendLine("{");
        foreach (var p in sproc.Parameters.OrderBy(p => p.Ordinal))
        {
            var cs   = SqlTypeMap.ToCs(p.ClrType);
            var prop = CasingHelper.ToPascalCase(p.Name.TrimStart('@'));
            var nullable = p.IsNullable || p.IsOutput;
            if (nullable)
                sb.AppendLine($"    public {cs}? {prop} {{ get; set; }}");
            else
                sb.AppendLine($"    public {cs} {prop} {{ get; set; }} = default!;");
        }
        sb.AppendLine("}");
        sb.AppendLine();
    }

    static void EmitResultClass(StringBuilder sb, StoredProcedure sproc, Dictionary<string, string> resultTypes)
    {
        var className = resultTypes[$"{sproc.Schema}.{sproc.Name}"];

        if (sproc.ResultInference == ResultInferenceStatus.Indeterminate)
        {
            sb.AppendLine($"/// <summary>Result row for [{sproc.Schema}].[{sproc.Name}]. Inference was indeterminate — fill in real columns.</summary>");
            sb.AppendLine($"public sealed class {className}");
            sb.AppendLine("{");
            sb.AppendLine("    // TODO: fill in real columns — result set could not be inferred at scaffold time.");
            sb.AppendLine("}");
            sb.AppendLine();
            return;
        }

        if (sproc.ResultColumns.Count == 0)
        {
            // Empty result set — emit a marker class.
            sb.AppendLine($"/// <summary>Result row for [{sproc.Schema}].[{sproc.Name}] (no result columns detected).</summary>");
            sb.AppendLine($"public sealed class {className} {{ }}");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"/// <summary>Result row for [{sproc.Schema}].[{sproc.Name}].</summary>");
        sb.AppendLine($"public sealed class {className}");
        sb.AppendLine("{");
        foreach (var col in sproc.ResultColumns.OrderBy(c => c.Ordinal))
        {
            var cs   = SqlTypeMap.ToCs(col.ClrType);
            var prop = CasingHelper.ToPascalCase(col.Name);
            if (col.IsNullable)
                sb.AppendLine($"    public {cs}? {prop} {{ get; set; }}");
            else
                sb.AppendLine($"    public {cs} {prop} {{ get; set; }} = default!;");
        }
        sb.AppendLine("}");
        sb.AppendLine();
    }
}
