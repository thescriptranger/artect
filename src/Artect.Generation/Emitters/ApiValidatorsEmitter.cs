using System.Collections.Generic;
using System.Text;
using Artect.Config;

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

        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.IsJoinTable) continue;
            if (!entity.HasPrimaryKey) continue;

            var name = entity.EntityTypeName;

            var sb = new StringBuilder();
            sb.AppendLine($"using {sharedReqNs};");
            sb.AppendLine($"using {appValidatorsNs};");
            sb.AppendLine();
            sb.AppendLine($"namespace {apiValidatorsNs};");
            sb.AppendLine();

            if ((crud & CrudOperation.Post) != 0)
                EmitValidatorClass(sb, $"Create{name}Request");
            if ((crud & CrudOperation.Put) != 0)
                EmitValidatorClass(sb, $"Update{name}Request");
            if ((crud & CrudOperation.Patch) != 0)
                EmitValidatorClass(sb, $"Patch{name}Request");

            list.Add(new EmittedFile(
                CleanLayout.ApiValidatorsPath(project, $"{name}Validators"),
                sb.ToString()));
        }
        return list;
    }

    static void EmitValidatorClass(StringBuilder sb, string requestTypeName)
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
        sb.AppendLine("        ExtendValidate(dto, result);");
        sb.AppendLine("        return result;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    partial void ExtendValidate({requestTypeName} dto, ValidationResult result);");
        sb.AppendLine("}");
        sb.AppendLine();
    }
}
