using System.Collections.Generic;
using System.Text;

namespace Artect.Generation.Emitters;

public sealed class ValidationResultExtensionsEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var project = ctx.Config.ProjectName;
        var appValidatorsNs = CleanLayout.ApplicationValidatorsNamespace(project);
        var sharedErrorsNs = CleanLayout.SharedErrorsNamespace(project);
        var apiValidatorsNs = CleanLayout.ApiValidatorsNamespace(project);

        var sb = new StringBuilder();
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using Microsoft.AspNetCore.Http;");
        sb.AppendLine($"using {appValidatorsNs};");
        sb.AppendLine($"using SharedErrors = {sharedErrorsNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {apiValidatorsNs};");
        sb.AppendLine();
        sb.AppendLine("internal static class ValidationResultExtensions");
        sb.AppendLine("{");
        sb.AppendLine("    public static IResult ToBadRequest(this ValidationResult result) =>");
        sb.AppendLine("        Results.BadRequest(new SharedErrors.ApiProblem(");
        sb.AppendLine("            \"Validation failed\", 400, null,");
        sb.AppendLine("            result.Errors.Select(e => new SharedErrors.ValidationError(e.PropertyName, \"validation\", e.Message)).ToArray()));");
        sb.AppendLine("}");

        return new[] { new EmittedFile(CleanLayout.ApiValidatorsPath(project, "ValidationResultExtensions"), sb.ToString()) };
    }
}
