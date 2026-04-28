using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

public sealed class ApiValidatorsEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var crud = ctx.Config.Crud;
        if ((crud & (CrudOperation.Post | CrudOperation.Put | CrudOperation.Patch)) == 0)
            return System.Array.Empty<EmittedFile>();

        var list = new List<EmittedFile>();
        var project = ctx.Config.ProjectName;
        var sharedReqNs = CleanLayout.SharedRequestsNamespace(project);
        var appValidatorsNs = CleanLayout.ApplicationValidatorsNamespace(project);
        var apiValidatorsNs = CleanLayout.ApiValidatorsNamespace(project);
        var corrections = ctx.NamingCorrections;

        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.ShouldSkip(EntityClassification.AggregateRoot)) continue;

            var name = entity.EntityTypeName;
            var pkCols = entity.Table.PrimaryKey!.ColumnNames.ToHashSet(System.StringComparer.OrdinalIgnoreCase);

            // Mirror RequestEmitter.BuildProperties so the validator iterates only the
            // properties that actually exist on each request type. Pre-fix this emitter
            // walked every column on the table, which produced CS1061 (no such member)
            // for properties RequestEmitter dropped (Ignored, ProtectedFromUpdate,
            // non-PK server-generated) and CS1061 on PATCH PKs (which are plain T,
            // not Optional<T?>).
            var createCols = ColumnsForKind(entity, pkCols, isCreate: true);
            var updateCols = ColumnsForKind(entity, pkCols, isCreate: false);

            var sb = new StringBuilder();
            sb.AppendLine($"using {sharedReqNs};");
            sb.AppendLine($"using {appValidatorsNs};");
            sb.AppendLine();
            sb.AppendLine($"namespace {apiValidatorsNs};");
            sb.AppendLine();

            if ((crud & CrudOperation.Post) != 0)
                EmitValidatorClass(sb, $"Create{name}Request", createCols, pkCols, corrections, isPatch: false);
            if ((crud & CrudOperation.Put) != 0)
                EmitValidatorClass(sb, $"Update{name}Request", updateCols, pkCols, corrections, isPatch: false);
            if ((crud & CrudOperation.Patch) != 0)
                EmitValidatorClass(sb, $"Patch{name}Request", updateCols, pkCols, corrections, isPatch: true);

            list.Add(new EmittedFile(
                CleanLayout.ApiValidatorsPath(project, $"{name}Validators"),
                sb.ToString()));
        }
        return list;
    }

    /// <summary>
    /// Mirrors <c>RequestEmitter.BuildProperties</c> column-filter logic so the validator
    /// only iterates properties that actually exist on the request type.
    /// </summary>
    static List<Column> ColumnsForKind(NamedEntity entity, HashSet<string> pkCols, bool isCreate)
    {
        return entity.Table.Columns.Where(c =>
        {
            if (entity.ColumnHasFlag(c.Name, ColumnMetadata.Ignored)) return false;
            var isPk = pkCols.Contains(c.Name);
            if (isCreate)
            {
                if (isPk && c.IsServerGenerated) return false;
            }
            else
            {
                if (entity.ColumnHasFlag(c.Name, ColumnMetadata.ProtectedFromUpdate)) return false;
                if (!isPk && c.IsServerGenerated) return false;
            }
            return true;
        }).ToList();
    }

    static void EmitValidatorClass(StringBuilder sb, string requestTypeName, IReadOnlyList<Column> cols,
        HashSet<string> pkCols, IReadOnlyDictionary<string, string> corrections, bool isPatch)
    {
        sb.AppendLine($"public sealed partial class {requestTypeName}Validator : IValidator<{requestTypeName}>");
        sb.AppendLine("{");
        sb.AppendLine($"    public ValidationResult Validate({requestTypeName} dto)");
        sb.AppendLine("    {");
        sb.AppendLine("        var result = new ValidationResult();");
        sb.AppendLine();
        sb.AppendLine("        if (dto is null)");
        sb.AppendLine("        {");
        sb.AppendLine("            result.Add(string.Empty, \"Request must not be null.\");");
        sb.AppendLine("            return result;");
        sb.AppendLine("        }");
        sb.AppendLine();

        // V#5 PATCH semantics: non-PK fields are wrapped in Optional<T?> by RequestEmitter,
        // but PK fields stay plain (so the URL-supplied id can be carried unchanged into the
        // command). Each column therefore needs to know whether it's wrapped before deciding
        // how to unwrap. Pattern matching against the inner T? value rejects null and binds
        // the non-null payload for further checks.
        foreach (var col in cols)
        {
            var prop = EntityNaming.PropertyName(col, corrections);
            var isPk = pkCols.Contains(col.Name);
            // Optional<T?> wrap follows RequestEmitter: PATCH + non-PK only.
            var wrapped = isPatch && !isPk;

            if (!col.IsNullable && col.ClrType == ClrType.String)
            {
                if (wrapped)
                {
                    sb.AppendLine($"        if (dto.{prop}.HasValue && string.IsNullOrWhiteSpace(dto.{prop}.Value))");
                }
                else
                {
                    sb.AppendLine($"        if (string.IsNullOrWhiteSpace(dto.{prop}))");
                }
                sb.AppendLine($"            result.Add(nameof(dto.{prop}), \"{col.Name} is required.\");");
            }
            if (col.ClrType == ClrType.String && col.MaxLength is int max && max > 0)
            {
                if (wrapped)
                {
                    sb.AppendLine($"        if (dto.{prop}.Value is {{ Length: > {max} }})");
                }
                else
                {
                    sb.AppendLine($"        if (dto.{prop} is {{ Length: > {max} }})");
                }
                sb.AppendLine($"            result.Add(nameof(dto.{prop}), \"{col.Name} must be at most {max} characters.\");");
            }
            if (!col.IsNullable && col.ClrType == ClrType.Guid)
            {
                if (wrapped)
                {
                    sb.AppendLine($"        if (dto.{prop}.Value is System.Guid {prop}_v && {prop}_v == System.Guid.Empty)");
                }
                else
                {
                    sb.AppendLine($"        if (dto.{prop} == System.Guid.Empty)");
                }
                sb.AppendLine($"            result.Add(nameof(dto.{prop}), \"{col.Name} is required.\");");
            }
        }

        // Paired created/updated check — skipped on PATCH because the client may legitimately
        // supply only one half of the pair, and Optional<DateTime?> doesn't compose with the
        // direct < operator.
        if (!isPatch)
        {
            var createdCol = cols.FirstOrDefault(c => IsDateLike(c) && IsCreatedTimestampName(c.Name));
            var updatedCol = cols.FirstOrDefault(c => IsDateLike(c) && IsUpdatedTimestampName(c.Name));
            if (createdCol is not null && updatedCol is not null)
            {
                var createdProp = EntityNaming.PropertyName(createdCol, corrections);
                var updatedProp = EntityNaming.PropertyName(updatedCol, corrections);
                sb.AppendLine($"        if (dto.{updatedProp} < dto.{createdProp})");
                sb.AppendLine($"            result.Add(nameof(dto.{updatedProp}), \"{updatedCol.Name} cannot be before {createdCol.Name}.\");");
            }
        }

        sb.AppendLine();
        sb.AppendLine("        ExtendValidate(dto, result);");
        sb.AppendLine("        return result;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    partial void ExtendValidate({requestTypeName} dto, ValidationResult result);");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    static bool IsDateLike(Column c) =>
        c.ClrType == ClrType.DateTime ||
        c.ClrType == ClrType.DateTimeOffset ||
        c.ClrType == ClrType.DateOnly;

    static bool IsCreatedTimestampName(string name) =>
        name.StartsWith("Created", System.StringComparison.OrdinalIgnoreCase);

    static bool IsUpdatedTimestampName(string name) =>
        name.StartsWith("Updated", System.StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Modified", System.StringComparison.OrdinalIgnoreCase);
}
