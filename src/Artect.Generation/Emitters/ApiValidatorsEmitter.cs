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

            // Create request: skip server-generated PK columns (matches RequestEmitter)
            var createCols = entity.Table.Columns
                .Where(c => !(pkCols.Contains(c.Name) && c.IsServerGenerated))
                .ToList();

            // Update / Patch request: all columns
            var allCols = entity.Table.Columns.ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"using {sharedReqNs};");
            sb.AppendLine($"using {appValidatorsNs};");
            sb.AppendLine();
            sb.AppendLine($"namespace {apiValidatorsNs};");
            sb.AppendLine();

            if ((crud & CrudOperation.Post) != 0)
                EmitValidatorClass(sb, $"Create{name}Request", createCols, corrections);
            if ((crud & CrudOperation.Put) != 0)
                EmitValidatorClass(sb, $"Update{name}Request", allCols, corrections);
            if ((crud & CrudOperation.Patch) != 0)
                EmitValidatorClass(sb, $"Patch{name}Request", allCols, corrections);

            list.Add(new EmittedFile(
                CleanLayout.ApiValidatorsPath(project, $"{name}Validators"),
                sb.ToString()));
        }
        return list;
    }

    static void EmitValidatorClass(StringBuilder sb, string requestTypeName, IReadOnlyList<Column> cols,
        IReadOnlyDictionary<string, string> corrections)
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

        foreach (var col in cols)
        {
            var prop = EntityNaming.PropertyName(col, corrections);
            if (!col.IsNullable && col.ClrType == ClrType.String)
            {
                sb.AppendLine($"        if (string.IsNullOrWhiteSpace(dto.{prop}))");
                sb.AppendLine($"            result.Add(nameof(dto.{prop}), \"{col.Name} is required.\");");
            }
            if (col.ClrType == ClrType.String && col.MaxLength is int max && max > 0)
            {
                sb.AppendLine($"        if (dto.{prop} is {{ Length: > {max} }})");
                sb.AppendLine($"            result.Add(nameof(dto.{prop}), \"{col.Name} must be at most {max} characters.\");");
            }
            if (!col.IsNullable && col.ClrType == ClrType.Guid)
            {
                sb.AppendLine($"        if (dto.{prop} == System.Guid.Empty)");
                sb.AppendLine($"            result.Add(nameof(dto.{prop}), \"{col.Name} is required.\");");
            }
        }

        // Paired created/updated check
        var createdCol = cols.FirstOrDefault(c => IsDateLike(c) && IsCreatedTimestampName(c.Name));
        var updatedCol = cols.FirstOrDefault(c => IsDateLike(c) && IsUpdatedTimestampName(c.Name));
        if (createdCol is not null && updatedCol is not null)
        {
            var createdProp = EntityNaming.PropertyName(createdCol, corrections);
            var updatedProp = EntityNaming.PropertyName(updatedCol, corrections);
            sb.AppendLine($"        if (dto.{updatedProp} < dto.{createdProp})");
            sb.AppendLine($"            result.Add(nameof(dto.{updatedProp}), \"{updatedCol.Name} cannot be before {createdCol.Name}.\");");
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
