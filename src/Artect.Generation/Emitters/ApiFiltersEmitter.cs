using System.Collections.Generic;
using System.Text;
using Artect.Config;

namespace Artect.Generation.Emitters;

/// <summary>
/// V#8: emits cross-cutting endpoint filters into <c>Api/Filters/</c>. Today the
/// only filter is <c>ValidationFilter&lt;TRequest&gt;</c>, which moves the inline
/// <c>validator.Validate(...)</c> call out of every endpoint lambda. Future
/// violations (V#9 auth, V#11 read-side safeguards) may add more filters here.
/// Only emits when the user has at least one CRUD verb that produces a request
/// payload (Post/Put/Patch).
/// </summary>
public sealed class ApiFiltersEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var crud = ctx.Config.Crud;
        if ((crud & (CrudOperation.Post | CrudOperation.Put | CrudOperation.Patch)) == 0)
            return System.Array.Empty<EmittedFile>();

        var project = ctx.Config.ProjectName;
        var ns = $"{project}.Api.Filters";
        var appValidNs = CleanLayout.ApplicationValidatorsNamespace(project);
        var apiValidNs = CleanLayout.ApiValidatorsNamespace(project);

        var sb = new StringBuilder();
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Microsoft.AspNetCore.Http;");
        sb.AppendLine($"using {appValidNs};");
        sb.AppendLine($"using {apiValidNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("public sealed class ValidationFilter<TRequest>(IValidator<TRequest> validator) : IEndpointFilter");
        sb.AppendLine("{");
        sb.AppendLine("    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)");
        sb.AppendLine("    {");
        sb.AppendLine("        var request = context.Arguments.OfType<TRequest>().FirstOrDefault();");
        sb.AppendLine("        if (request is null)");
        sb.AppendLine("            return await next(context).ConfigureAwait(false);");
        sb.AppendLine();
        sb.AppendLine("        var result = validator.Validate(request);");
        sb.AppendLine("        if (!result.IsValid)");
        sb.AppendLine("            return result.ToBadRequest();");
        sb.AppendLine();
        sb.AppendLine("        return await next(context).ConfigureAwait(false);");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return new[]
        {
            new EmittedFile($"{CleanLayout.ApiDir(project)}/Filters/ValidationFilter.cs", sb.ToString()),
        };
    }
}
