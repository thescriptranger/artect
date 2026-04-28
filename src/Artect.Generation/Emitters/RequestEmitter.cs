using System.Collections.Generic;
using System.Linq;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;
using Artect.Templating;
using Artect.Templating.Ast;

namespace Artect.Generation.Emitters;

public sealed class RequestEmitter : IEmitter
{
    enum RequestKind { Create, Update, Patch }

    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var template = TemplateParser.Parse(ctx.Templates.Load("Request.cs.artect"));
        var list = new List<EmittedFile>();
        var project = ctx.Config.ProjectName;
        var ns = $"{CleanLayout.SharedNamespace(project)}.Requests";
        var sharedCommonNs = $"{project}.Shared.Common";
        var emitPatch = (ctx.Config.Crud & CrudOperation.Patch) != 0;

        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.ShouldSkip(EntityClassification.AggregateRoot)) continue;

            var pkCols = entity.Table.PrimaryKey!.ColumnNames
                .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

            // Create request: skip server-generated PK columns.
            list.Add(BuildRequest(template, ns, "Create", entity, pkCols, RequestKind.Create, ctx.NamingCorrections, project, sharedCommonNs));

            // Update request: include PK + non-server-gen + non-protected columns.
            list.Add(BuildRequest(template, ns, "Update", entity, pkCols, RequestKind.Update, ctx.NamingCorrections, project, sharedCommonNs));

            // V#5 Patch request: same shape as Update, but every non-PK field wrapped
            // in Optional<T?> so the deserializer can distinguish "absent" from
            // "present with null" from "present with a value."
            if (emitPatch)
            {
                list.Add(BuildRequest(template, ns, "Patch", entity, pkCols, RequestKind.Patch, ctx.NamingCorrections, project, sharedCommonNs));
            }
        }

        return list;
    }

    static EmittedFile BuildRequest(
        TemplateDocument template, string ns, string kind, NamedEntity entity,
        HashSet<string> pkCols, RequestKind requestKind,
        IReadOnlyDictionary<string, string> corrections, string project, string sharedCommonNs)
    {
        var props = BuildProperties(entity, pkCols, requestKind, corrections);
        var usings = requestKind == RequestKind.Patch
            ? (IReadOnlyList<string>)new[] { sharedCommonNs }
            : System.Array.Empty<string>();
        var data = new
        {
            Namespace = ns,
            Kind = kind,
            EntityName = entity.EntityTypeName,
            Properties = props,
            HasUsings = usings.Count > 0,
            Usings = usings,
        };
        var rendered = Renderer.Render(template, data);
        var path = CleanLayout.SharedRequestPath(project, entity.EntityTypeName, kind);
        return new EmittedFile(path, rendered);
    }

    static IReadOnlyList<object> BuildProperties(NamedEntity entity, HashSet<string> pkCols, RequestKind kind, IReadOnlyDictionary<string, string> corrections)
    {
        var result = new List<object>();
        foreach (var c in entity.Table.Columns)
        {
            // Drop columns flagged Ignored in artect.yaml columnMetadata.
            if (entity.ColumnHasFlag(c.Name, ColumnMetadata.Ignored)) continue;

            var isPk = pkCols.Contains(c.Name);
            if (kind == RequestKind.Create)
            {
                // Create: skip server-generated PK (DB assigns it).
                if (isPk && c.IsServerGenerated) continue;
            }
            else
            {
                // V#3 Update / V#5 Patch: keep PK (URL identifier) + updateable columns.
                // Drop ProtectedFromUpdate (domain method controls those) and non-PK
                // server-generated columns (DB controls those — audit/concurrency).
                if (entity.ColumnHasFlag(c.Name, ColumnMetadata.ProtectedFromUpdate)) continue;
                if (!isPk && c.IsServerGenerated) continue;
            }

            var csBase = SqlTypeMap.ToCs(c.ClrType);
            string clrTypeWithNullability;
            if (c.IsNullable && SqlTypeMap.IsValueType(c.ClrType))
                clrTypeWithNullability = csBase + "?";
            else if (c.IsNullable && c.ClrType == ClrType.String)
                clrTypeWithNullability = csBase + "?";
            else
                clrTypeWithNullability = csBase;

            // V#5: For Patch, wrap non-PK fields in Optional<T?>. The inner type is
            // always nullable so JSON null is deserializable — the domain Update method
            // decides whether the supplied null is valid for that field.
            var wrapInOptional = kind == RequestKind.Patch && !isPk;
            if (wrapInOptional)
            {
                var inner = clrTypeWithNullability.EndsWith("?", System.StringComparison.Ordinal)
                    ? clrTypeWithNullability
                    : clrTypeWithNullability + "?";
                clrTypeWithNullability = $"Optional<{inner}>";
            }

            var required = !c.IsNullable && c.ClrType == ClrType.String && !wrapInOptional;
            var hasStringLength = c.ClrType == ClrType.String && c.MaxLength is > 0;
            var stringLength = hasStringLength ? c.MaxLength!.Value : 0;
            // Optional<T> defaults to HasValue=false naturally; no initializer needed.
            // Plain non-nullable strings still need = string.Empty;.
            var initializer = !wrapInOptional && !c.IsNullable && c.ClrType == ClrType.String ? " = string.Empty;" : string.Empty;

            result.Add(new
            {
                PropertyName = EntityNaming.PropertyName(c, corrections),
                ClrTypeWithNullability = clrTypeWithNullability,
                Initializer = initializer,
                Required = required,
                HasStringLength = hasStringLength,
                StringLength = stringLength,
                HasRange = false,
                RangeMin = string.Empty,
                RangeMax = string.Empty,
            });
        }
        return result;
    }
}
