using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// V#7: emits <c>I{Schema}DbFunctions</c> + impl stubs into
/// <c>src/&lt;Project&gt;.Infrastructure/StoredProcedures/</c>. DB functions are a
/// persistence concern and must not be exposed in Application — users define
/// business-named ports (see the V#7 README emitted by
/// <see cref="StoredProceduresEmitter"/>) and adapt them in Infrastructure.
/// Only runs when the schema graph contains at least one function. Each schema
/// gets its own interface/implementation pair.
/// </summary>
public sealed class DbFunctionsEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        if (ctx.Graph.Functions.Count == 0)
            return System.Array.Empty<EmittedFile>();

        var list    = new List<EmittedFile>();
        var project = ctx.Config.ProjectName;
        var ns      = CleanLayout.InfrastructureStoredProceduresNamespace(project);

        // Group by schema — each schema gets its own interface+impl pair.
        var bySchema = ctx.Graph.Functions
            .GroupBy(f => f.Schema, System.StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, System.StringComparer.Ordinal);

        foreach (var schemaGroup in bySchema)
        {
            var schemaPascal = CasingHelper.ToPascalCase(schemaGroup.Key, ctx.NamingCorrections);
            var ifaceName    = $"I{schemaPascal}DbFunctions";
            var implName     = $"{schemaPascal}DbFunctions";
            var functions    = schemaGroup.OrderBy(f => f.Name, System.StringComparer.Ordinal).ToList();

            // ── Interface ─────────────────────────────────────────────────────
            var ifaceSb = new StringBuilder();
            ifaceSb.AppendLine($"namespace {ns};");
            ifaceSb.AppendLine();
            ifaceSb.AppendLine($"/// <summary>Typed signatures for scalar and table-valued functions in the [{schemaGroup.Key}] schema.</summary>");
            ifaceSb.AppendLine($"public interface {ifaceName}");
            ifaceSb.AppendLine("{");

            foreach (var fn in functions)
            {
                var returnCs   = FunctionReturnCs(fn);
                var paramList  = BuildFunctionParamList(fn.Parameters, ctx.NamingCorrections);
                var methodName = CasingHelper.ToPascalCase(fn.Name, ctx.NamingCorrections);
                ifaceSb.AppendLine($"    /// <summary>Calls [{schemaGroup.Key}].[{fn.Name}].</summary>");
                ifaceSb.AppendLine($"    {returnCs} {methodName}({paramList});");
            }

            ifaceSb.AppendLine("}");

            var ifacePath = CleanLayout.InfrastructureStoredProceduresPath(project,ifaceName);
            list.Add(new EmittedFile(ifacePath, ifaceSb.ToString()));

            // ── Implementation stub ───────────────────────────────────────────
            var implSb = new StringBuilder();
            implSb.AppendLine($"namespace {ns};");
            implSb.AppendLine();
            implSb.AppendLine($"/// <summary>Implementation stub for <see cref=\"{ifaceName}\"/>. Replace with real SQL calls.</summary>");
            implSb.AppendLine($"public sealed class {implName} : {ifaceName}");
            implSb.AppendLine("{");

            foreach (var fn in functions)
            {
                var returnCs   = FunctionReturnCs(fn);
                var paramList  = BuildFunctionParamList(fn.Parameters, ctx.NamingCorrections);
                var methodName = CasingHelper.ToPascalCase(fn.Name, ctx.NamingCorrections);
                implSb.AppendLine($"    /// <inheritdoc />");
                implSb.AppendLine($"    public {returnCs} {methodName}({paramList})");
                implSb.AppendLine("        => throw new System.NotImplementedException();");
                implSb.AppendLine();
            }

            implSb.AppendLine("}");

            var implPath = CleanLayout.InfrastructureStoredProceduresPath(project,implName);
            list.Add(new EmittedFile(implPath, implSb.ToString()));
        }

        return list;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static string FunctionReturnCs(Function fn) => fn.ReturnKind switch
    {
        FunctionReturnKind.Scalar =>
            fn.ReturnClrType.HasValue
                ? $"{SqlTypeMap.ToCs(fn.ReturnClrType.Value)}?"
                : "object?",
        FunctionReturnKind.Table or FunctionReturnKind.Inline =>
            $"System.Collections.Generic.IEnumerable<{TableFunctionRowTypeName(fn)}>",
        _ => "object?",
    };

    static string TableFunctionRowTypeName(Function fn) =>
        fn.ResultColumns.Count > 0
            ? $"{CasingHelper.ToPascalCase(fn.Name)}Row"
            : "object";

    static string BuildFunctionParamList(IReadOnlyList<FunctionParameter> parameters, System.Collections.Generic.IReadOnlyDictionary<string, string> corrections)
    {
        if (parameters.Count == 0) return string.Empty;

        return string.Join(", ", parameters
            .OrderBy(p => p.Ordinal)
            .Select(p =>
            {
                var cs   = SqlTypeMap.ToCs(p.ClrType);
                var name = CasingHelper.ToCamelCase(p.Name.TrimStart('@'), corrections);
                return p.IsNullable ? $"{cs}? {name}" : $"{cs} {name}";
            }));
    }
}
