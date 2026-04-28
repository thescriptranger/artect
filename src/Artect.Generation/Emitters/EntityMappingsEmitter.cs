using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits the per-entity mappings file under <c>Api/Mappings/</c>:
/// - <c>Dto.ToResponse()</c> — Application-internal Dto to Shared wire Response.
/// - V#8: <c>Create&lt;Entity&gt;Request.ToCommand()</c>,
///   <c>Update&lt;Entity&gt;Request.ToCommand(id)</c>,
///   <c>Patch&lt;Entity&gt;Request.ToCommand(id)</c> when the matching CRUD verb
///   is configured. Pulled out of the endpoint lambda so endpoints stay thin and
///   the request→command translation is independently testable.
/// </summary>
public sealed class EntityMappingsEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var list = new List<EmittedFile>();
        var project = ctx.Config.ProjectName;
        var dtosNs = CleanLayout.ApplicationDtosNamespace(project);
        var sharedRespNs = CleanLayout.SharedResponsesNamespace(project);
        var sharedReqNs = CleanLayout.SharedRequestsNamespace(project);
        var apiMapNs = CleanLayout.ApiMappingsNamespace(project);
        var corrections = ctx.NamingCorrections;
        var includeChildren = ctx.Config.IncludeChildCollectionsInResponses;
        var crud = ctx.Config.Crud;

        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.ShouldSkip(EntityClassification.AggregateRoot, EntityClassification.ReadModel)) continue;

            var name = entity.EntityTypeName;
            var featureNs = CleanLayout.ApplicationFeatureNamespace(project, name);
            var emitCommandMappings = entity.Classification == EntityClassification.AggregateRoot
                && (crud & (CrudOperation.Post | CrudOperation.Put | CrudOperation.Patch)) != 0;

            // V#1 cascade fix: the Dto already filters Ignored + Sensitive columns, so the
            // Response mapping must filter the same set or it references properties that
            // don't exist on either side. Pre-V#8 this produced a latent compile error in
            // any generated solution that set Sensitive on a column.
            var visibleColumns = entity.Table.Columns
                .Where(c => !entity.ColumnHasFlag(c.Name, ColumnMetadata.Ignored))
                .Where(c => !entity.ColumnHasFlag(c.Name, ColumnMetadata.Sensitive))
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"using {dtosNs};");
            sb.AppendLine($"using {sharedRespNs};");
            if (emitCommandMappings)
            {
                sb.AppendLine($"using {sharedReqNs};");
                sb.AppendLine($"using {featureNs};");
            }
            sb.AppendLine();
            sb.AppendLine($"namespace {apiMapNs};");
            sb.AppendLine();
            sb.AppendLine($"public static class {name}Mappings");
            sb.AppendLine("{");
            // V#10: ToResponse copies every visible column including columns flagged
            // ColumnMetadata.Deprecated. The deprecated property carries [Obsolete],
            // which makes the assignment trigger CS0618 under TreatWarningsAsErrors=true.
            // Suppress around the method body — this is the one place where copying the
            // deprecated value is intentional (backward-compat).
            var hasDeprecatedVisible = visibleColumns
                .Any(c => entity.ColumnHasFlag(c.Name, ColumnMetadata.Deprecated));
            if (hasDeprecatedVisible)
            {
                sb.AppendLine("    #pragma warning disable CS0612 // intentional copy of obsolete member for backward-compat");
                sb.AppendLine("    #pragma warning disable CS0618 // intentional copy of obsolete member for backward-compat");
            }
            sb.AppendLine($"    public static {name}Response ToResponse(this {name}Dto dto) =>");
            sb.AppendLine("        new()");
            sb.AppendLine("        {");
            foreach (var col in visibleColumns)
            {
                var prop = EntityNaming.PropertyName(col, corrections);
                sb.AppendLine($"            {prop} = dto.{prop},");
            }
            if (includeChildren)
            {
                foreach (var nav in entity.CollectionNavigations)
                {
                    sb.AppendLine($"            {nav.PropertyName} = dto.{nav.PropertyName}.Select(c => c.ToResponse()).ToArray(),");
                }
            }
            sb.AppendLine("        };");
            if (hasDeprecatedVisible)
            {
                sb.AppendLine("    #pragma warning restore CS0618");
                sb.AppendLine("    #pragma warning restore CS0612");
            }

            if (emitCommandMappings)
            {
                EmitRequestToCommandMappings(sb, entity, name, crud, corrections);
            }

            sb.AppendLine("}");

            list.Add(new EmittedFile(
                CleanLayout.ApiMappingsPath(project, $"{name}Mappings"),
                sb.ToString()));
        }
        return list;
    }

    /// <summary>
    /// V#8: emits Request.ToCommand() extension methods. Endpoints call these instead of
    /// inlining the positional record construction. Mirror the command shapes produced by
    /// CommandRecordsEmitter (Create = nonServerGen; Update/Patch = PK + UpdateableColumns).
    /// V#10: when any request property carries [Obsolete] (ColumnMetadata.Deprecated),
    /// wrap the mapping in a CS0618/CS0612 pragma so the intentional read of the
    /// deprecated property doesn't break TreatWarningsAsErrors=true.
    /// </summary>
    static void EmitRequestToCommandMappings(StringBuilder sb, NamedEntity entity, string name, CrudOperation crud, IReadOnlyDictionary<string, string> corrections)
    {
        var pkCols = entity.Table.PrimaryKey!.ColumnNames
            .ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        var anyDeprecated = entity.Table.Columns
            .Any(c => entity.ColumnHasFlag(c.Name, ColumnMetadata.Deprecated)
                   && !entity.ColumnHasFlag(c.Name, ColumnMetadata.Ignored));

        if ((crud & CrudOperation.Post) != 0)
        {
            var nonServerGen = entity.Table.Columns
                .Where(c => !entity.ColumnHasFlag(c.Name, ColumnMetadata.Ignored))
                .Where(c => !c.IsServerGenerated)
                .ToList();
            sb.AppendLine();
            if (anyDeprecated)
            {
                sb.AppendLine("    #pragma warning disable CS0612 // intentional read of obsolete request member");
                sb.AppendLine("    #pragma warning disable CS0618 // intentional read of obsolete request member");
            }
            sb.AppendLine($"    public static Create{name}Command ToCommand(this Create{name}Request request) =>");
            sb.AppendLine("        new(");
            for (int i = 0; i < nonServerGen.Count; i++)
            {
                var col = nonServerGen[i];
                var prop = EntityNaming.PropertyName(col, corrections);
                var terminator = i == nonServerGen.Count - 1 ? ");" : ",";
                sb.AppendLine($"            request.{prop}{terminator}");
            }
            if (anyDeprecated)
            {
                sb.AppendLine("    #pragma warning restore CS0618");
                sb.AppendLine("    #pragma warning restore CS0612");
            }
        }

        // V#3 Update + V#5 Patch share the "PK + UpdateableColumns" shape.
        if ((crud & CrudOperation.Put) != 0)
            EmitUpdateLikeMapping(sb, entity, name, "Update", corrections, pkCols, anyDeprecated);
        if ((crud & CrudOperation.Patch) != 0)
            EmitUpdateLikeMapping(sb, entity, name, "Patch", corrections, pkCols, anyDeprecated);
    }

    static void EmitUpdateLikeMapping(StringBuilder sb, NamedEntity entity, string name, string verb, IReadOnlyDictionary<string, string> corrections, System.Collections.Generic.HashSet<string> pkCols, bool anyDeprecated)
    {
        // PK type for the route id parameter. We only support single-column PKs in
        // endpoints today (URL pattern "/{id}"); composite-key entities won't have an
        // Update endpoint generated, so this lookup is safe.
        var pkCol = entity.Table.Columns.First(c => pkCols.Contains(c.Name));
        var pkType = SqlTypeMap.ToCs(pkCol.ClrType);

        // Match CommandRecordsEmitter.UpdateCommandColumns: PK columns first (in PK order),
        // then UpdateableColumns. The endpoint passes the URL id for the PK; the body
        // supplies the rest. Body PK (if present in the request) is silently ignored.
        var pkColumnsList = entity.Table.PrimaryKey!.ColumnNames
            .Select(n => entity.Table.Columns.First(c =>
                string.Equals(c.Name, n, System.StringComparison.OrdinalIgnoreCase)))
            .Where(c => !entity.ColumnHasFlag(c.Name, ColumnMetadata.Ignored))
            .ToList();
        var commandCols = pkColumnsList.Concat(entity.UpdateableColumns()).ToList();

        sb.AppendLine();
        if (anyDeprecated)
        {
            sb.AppendLine("    #pragma warning disable CS0612 // intentional read of obsolete request member");
            sb.AppendLine("    #pragma warning disable CS0618 // intentional read of obsolete request member");
        }
        sb.AppendLine($"    public static {verb}{name}Command ToCommand(this {verb}{name}Request request, {pkType} id) =>");
        sb.AppendLine("        new(");
        for (int i = 0; i < commandCols.Count; i++)
        {
            var col = commandCols[i];
            var prop = EntityNaming.PropertyName(col, corrections);
            var value = pkCols.Contains(col.Name) ? "id" : $"request.{prop}";
            var terminator = i == commandCols.Count - 1 ? ");" : ",";
            sb.AppendLine($"            {value}{terminator}");
        }
        if (anyDeprecated)
        {
            sb.AppendLine("    #pragma warning restore CS0618");
            sb.AppendLine("    #pragma warning restore CS0612");
        }
    }
}
