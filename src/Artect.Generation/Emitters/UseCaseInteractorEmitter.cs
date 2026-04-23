using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits per-entity-per-operation use-case interface + implementation pairs.
/// Only runs when <c>cfg.EmitUseCaseInteractors == true</c>.
/// Entities with PK get full CRUD interactors; pk-less tables and views get List-only.
/// </summary>
public sealed class UseCaseInteractorEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        if (!ctx.Config.EmitUseCaseInteractors)
            return System.Array.Empty<EmittedFile>();

        var list    = new List<EmittedFile>();
        var project = ctx.Config.ProjectName;
        var crud    = ctx.Config.Crud;

        // Regular entities
        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.IsJoinTable) continue;

            if (entity.HasPrimaryKey)
                EmitFullInteractors(list, ctx, entity, project, crud);
            else
                EmitListOnlyInteractors(list, ctx, entity.EntityTypeName, entity.DbSetPropertyName, project);
        }

        // Views — list-only
        foreach (var view in ctx.Graph.Views)
        {
            var typeName = CasingHelper.ToPascalCase(Pluralizer.Singularize(view.Name));
            var plural   = CasingHelper.ToPascalCase(Pluralizer.Pluralize(Pluralizer.Singularize(view.Name)));
            EmitListOnlyInteractors(list, ctx, typeName, plural, project);
        }

        return list;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Full CRUD interactors (entity has PK)
    // ──────────────────────────────────────────────────────────────────────────

    static void EmitFullInteractors(
        List<EmittedFile> list,
        EmitterContext ctx,
        NamedEntity entity,
        string project,
        CrudOperation crud)
    {
        var entityName = entity.EntityTypeName;
        var plural     = entity.DbSetPropertyName;
        var pkType     = PkClrType(entity.Table);

        if ((crud & CrudOperation.GetList) != 0)
            list.AddRange(EmitListInteractor(project, entityName, plural));

        if ((crud & CrudOperation.GetById) != 0)
            list.AddRange(EmitGetByIdInteractor(project, entityName, pkType));

        if ((crud & CrudOperation.Post) != 0)
            list.AddRange(EmitCreateInteractor(project, entityName));

        if ((crud & CrudOperation.Put) != 0)
            list.AddRange(EmitUpdateInteractor(project, entityName, pkType, "Update"));

        if ((crud & CrudOperation.Patch) != 0)
            list.AddRange(EmitPatchInteractor(project, entityName, pkType));

        if ((crud & CrudOperation.Delete) != 0)
            list.AddRange(EmitDeleteInteractor(project, entityName, pkType));
    }

    static void EmitListOnlyInteractors(
        List<EmittedFile> list,
        EmitterContext ctx,
        string typeName,
        string plural,
        string project)
    {
        list.AddRange(EmitListInteractor(project, typeName, plural));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Per-op emitters — each returns [interfaceFile, implFile]
    // ──────────────────────────────────────────────────────────────────────────

    static IEnumerable<EmittedFile> EmitListInteractor(string project, string entityName, string plural)
    {
        var opName    = $"List{plural}";
        var ifaceNs   = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.UseCases";
        var implNs    = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";
        var repoAbsNs = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var respNs    = $"{CleanLayout.SharedNamespace(project)}.Responses";
        var ucNs      = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";

        // Interface
        var iface = new StringBuilder();
        iface.AppendLine($"using System.Threading;");
        iface.AppendLine($"using System.Threading.Tasks;");
        iface.AppendLine($"using {respNs};");
        iface.AppendLine($"using {ucNs};");
        iface.AppendLine();
        iface.AppendLine($"namespace {ifaceNs};");
        iface.AppendLine();
        iface.AppendLine($"public interface I{opName}UseCase");
        iface.AppendLine("{");
        iface.AppendLine($"    Task<UseCaseResult<PagedResponse<{entityName}Response>>> ExecuteAsync(int page, int pageSize, CancellationToken ct);");
        iface.AppendLine("}");

        var mapNs = $"{CleanLayout.ApplicationNamespace(project)}.Mappings";
        var dtoNs = $"{CleanLayout.ApplicationNamespace(project)}.Dtos";

        // Implementation
        var impl = new StringBuilder();
        impl.AppendLine($"using System.Linq;");
        impl.AppendLine($"using System.Threading;");
        impl.AppendLine($"using System.Threading.Tasks;");
        impl.AppendLine($"using {repoAbsNs};");
        impl.AppendLine($"using {mapNs};");
        impl.AppendLine($"using {respNs};");
        impl.AppendLine($"using {ifaceNs};");
        impl.AppendLine();
        impl.AppendLine($"namespace {implNs};");
        impl.AppendLine();
        impl.AppendLine($"public sealed class {opName}UseCase : I{opName}UseCase");
        impl.AppendLine("{");
        impl.AppendLine($"    readonly I{entityName}Repository _repo;");
        impl.AppendLine();
        impl.AppendLine($"    public {opName}UseCase(I{entityName}Repository repo)");
        impl.AppendLine("    {");
        impl.AppendLine("        _repo = repo;");
        impl.AppendLine("    }");
        impl.AppendLine();
        impl.AppendLine($"    public async Task<UseCaseResult<PagedResponse<{entityName}Response>>> ExecuteAsync(int page, int pageSize, CancellationToken ct)");
        impl.AppendLine("    {");
        impl.AppendLine("        if (page < 1) page = 1;");
        impl.AppendLine("        if (pageSize < 1) pageSize = 50;");
        impl.AppendLine("        var dtoPage = await _repo.ListAsync(page, pageSize, ct).ConfigureAwait(false);");
        impl.AppendLine($"        var responsePage = new PagedResponse<{entityName}Response>");
        impl.AppendLine("        {");
        impl.AppendLine("            Items = dtoPage.Items.Select(d => d.ToResponse()).ToList(),");
        impl.AppendLine("            Page = dtoPage.Page,");
        impl.AppendLine("            PageSize = dtoPage.PageSize,");
        impl.AppendLine("            TotalCount = dtoPage.TotalCount,");
        impl.AppendLine("        };");
        impl.AppendLine($"        return new UseCaseResult<PagedResponse<{entityName}Response>>.Success(responsePage);");
        impl.AppendLine("    }");
        impl.AppendLine("}");

        yield return new EmittedFile(CleanLayout.UseCaseInterfacePath(project, opName), iface.ToString());
        yield return new EmittedFile(CleanLayout.UseCaseImplPath(project, opName), impl.ToString());
    }

    static IEnumerable<EmittedFile> EmitGetByIdInteractor(string project, string entityName, string pkType)
    {
        var opName    = $"Get{entityName}ById";
        var ifaceNs   = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.UseCases";
        var implNs    = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";
        var repoAbsNs = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var mapNs     = $"{CleanLayout.ApplicationNamespace(project)}.Mappings";
        var respNs    = $"{CleanLayout.SharedNamespace(project)}.Responses";
        var ucNs      = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";

        // Interface
        var iface = new StringBuilder();
        iface.AppendLine($"using System.Threading;");
        iface.AppendLine($"using System.Threading.Tasks;");
        iface.AppendLine($"using {respNs};");
        iface.AppendLine($"using {ucNs};");
        iface.AppendLine();
        iface.AppendLine($"namespace {ifaceNs};");
        iface.AppendLine();
        iface.AppendLine($"public interface I{opName}UseCase");
        iface.AppendLine("{");
        iface.AppendLine($"    Task<UseCaseResult<{entityName}Response>> ExecuteAsync({pkType} id, CancellationToken ct);");
        iface.AppendLine("}");

        // Implementation
        var impl = new StringBuilder();
        impl.AppendLine($"using System.Threading;");
        impl.AppendLine($"using System.Threading.Tasks;");
        impl.AppendLine($"using {repoAbsNs};");
        impl.AppendLine($"using {mapNs};");
        impl.AppendLine($"using {respNs};");
        impl.AppendLine($"using {ifaceNs};");
        impl.AppendLine($"using {ucNs};");
        impl.AppendLine();
        impl.AppendLine($"namespace {implNs};");
        impl.AppendLine();
        impl.AppendLine($"public sealed class {opName}UseCase : I{opName}UseCase");
        impl.AppendLine("{");
        impl.AppendLine($"    readonly I{entityName}Repository _repo;");
        impl.AppendLine();
        impl.AppendLine($"    public {opName}UseCase(I{entityName}Repository repo)");
        impl.AppendLine("    {");
        impl.AppendLine("        _repo = repo;");
        impl.AppendLine("    }");
        impl.AppendLine();
        impl.AppendLine($"    public async Task<UseCaseResult<{entityName}Response>> ExecuteAsync({pkType} id, CancellationToken ct)");
        impl.AppendLine("    {");
        impl.AppendLine("        var dto = await _repo.GetByIdAsync(id, ct).ConfigureAwait(false);");
        impl.AppendLine("        if (dto is null)");
        impl.AppendLine($"            return new UseCaseResult<{entityName}Response>.NotFound(\"{entityName}\", id.ToString()!);");
        impl.AppendLine($"        return new UseCaseResult<{entityName}Response>.Success(dto.ToResponse());");
        impl.AppendLine("    }");
        impl.AppendLine("}");

        yield return new EmittedFile(CleanLayout.UseCaseInterfacePath(project, opName), iface.ToString());
        yield return new EmittedFile(CleanLayout.UseCaseImplPath(project, opName), impl.ToString());
    }

    static IEnumerable<EmittedFile> EmitCreateInteractor(string project, string entityName)
    {
        var opName    = $"Create{entityName}";
        var ifaceNs   = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.UseCases";
        var implNs    = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";
        var repoAbsNs = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var respNs    = $"{CleanLayout.SharedNamespace(project)}.Responses";
        var reqNs     = $"{CleanLayout.SharedNamespace(project)}.Requests";
        var ucNs      = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";
        var mapNs     = $"{CleanLayout.ApplicationNamespace(project)}.Mappings";

        // Interface
        var iface = new StringBuilder();
        iface.AppendLine($"using System.Threading;");
        iface.AppendLine($"using System.Threading.Tasks;");
        iface.AppendLine($"using {reqNs};");
        iface.AppendLine($"using {respNs};");
        iface.AppendLine($"using {ucNs};");
        iface.AppendLine();
        iface.AppendLine($"namespace {ifaceNs};");
        iface.AppendLine();
        iface.AppendLine($"public interface I{opName}UseCase");
        iface.AppendLine("{");
        iface.AppendLine($"    Task<UseCaseResult<{entityName}Response>> ExecuteAsync(Create{entityName}Request request, CancellationToken ct);");
        iface.AppendLine("}");

        var valNs = $"{CleanLayout.ApplicationNamespace(project)}.Validators";

        // Implementation
        var impl = new StringBuilder();
        impl.AppendLine($"using System.Threading;");
        impl.AppendLine($"using System.Threading.Tasks;");
        impl.AppendLine($"using {repoAbsNs};");
        impl.AppendLine($"using {reqNs};");
        impl.AppendLine($"using {respNs};");
        impl.AppendLine($"using {mapNs};");
        impl.AppendLine($"using {valNs};");
        impl.AppendLine($"using {ifaceNs};");
        impl.AppendLine($"using {ucNs};");
        impl.AppendLine();
        impl.AppendLine($"namespace {implNs};");
        impl.AppendLine();
        impl.AppendLine($"public sealed class {opName}UseCase : I{opName}UseCase");
        impl.AppendLine("{");
        impl.AppendLine($"    readonly I{entityName}Repository _repo;");
        impl.AppendLine();
        impl.AppendLine($"    public {opName}UseCase(I{entityName}Repository repo)");
        impl.AppendLine("    {");
        impl.AppendLine("        _repo = repo;");
        impl.AppendLine("    }");
        impl.AppendLine();
        impl.AppendLine($"    public async Task<UseCaseResult<{entityName}Response>> ExecuteAsync(Create{entityName}Request request, CancellationToken ct)");
        impl.AppendLine("    {");
        impl.AppendLine($"        var validator = new {entityName}RequestValidators.Create{entityName}Validator();");
        impl.AppendLine("        var validationResult = validator.Validate(request);");
        impl.AppendLine("        if (!validationResult.IsValid)");
        impl.AppendLine($"            return new UseCaseResult<{entityName}Response>.ValidationFailed(validationResult.Errors);");
        impl.AppendLine("        var dto = await _repo.CreateAsync(request.ToDto(), ct).ConfigureAwait(false);");
        impl.AppendLine($"        return new UseCaseResult<{entityName}Response>.Success(dto.ToResponse());");
        impl.AppendLine("    }");
        impl.AppendLine("}");

        yield return new EmittedFile(CleanLayout.UseCaseInterfacePath(project, opName), iface.ToString());
        yield return new EmittedFile(CleanLayout.UseCaseImplPath(project, opName), impl.ToString());
    }

    static IEnumerable<EmittedFile> EmitUpdateInteractor(string project, string entityName, string pkType, string verb)
    {
        var opName    = $"{verb}{entityName}";
        var ifaceNs   = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.UseCases";
        var implNs    = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";
        var repoAbsNs = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var reqNs     = $"{CleanLayout.SharedNamespace(project)}.Requests";
        var ucNs      = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";
        var mapNs     = $"{CleanLayout.ApplicationNamespace(project)}.Mappings";

        // Interface
        var iface = new StringBuilder();
        iface.AppendLine($"using System.Threading;");
        iface.AppendLine($"using System.Threading.Tasks;");
        iface.AppendLine($"using {reqNs};");
        iface.AppendLine($"using {ucNs};");
        iface.AppendLine();
        iface.AppendLine($"namespace {ifaceNs};");
        iface.AppendLine();
        iface.AppendLine($"public interface I{opName}UseCase");
        iface.AppendLine("{");
        iface.AppendLine($"    Task<UseCaseResult> ExecuteAsync({pkType} id, Update{entityName}Request request, CancellationToken ct);");
        iface.AppendLine("}");

        var valNs2 = $"{CleanLayout.ApplicationNamespace(project)}.Validators";

        // Implementation
        var impl = new StringBuilder();
        impl.AppendLine($"using System.Threading;");
        impl.AppendLine($"using System.Threading.Tasks;");
        impl.AppendLine($"using {repoAbsNs};");
        impl.AppendLine($"using {reqNs};");
        impl.AppendLine($"using {mapNs};");
        impl.AppendLine($"using {valNs2};");
        impl.AppendLine($"using {ifaceNs};");
        impl.AppendLine($"using {ucNs};");
        impl.AppendLine();
        impl.AppendLine($"namespace {implNs};");
        impl.AppendLine();
        impl.AppendLine($"public sealed class {opName}UseCase : I{opName}UseCase");
        impl.AppendLine("{");
        impl.AppendLine($"    readonly I{entityName}Repository _repo;");
        impl.AppendLine();
        impl.AppendLine($"    public {opName}UseCase(I{entityName}Repository repo)");
        impl.AppendLine("    {");
        impl.AppendLine("        _repo = repo;");
        impl.AppendLine("    }");
        impl.AppendLine();
        impl.AppendLine($"    public async Task<UseCaseResult> ExecuteAsync({pkType} id, Update{entityName}Request request, CancellationToken ct)");
        impl.AppendLine("    {");
        impl.AppendLine($"        var validator = new {entityName}RequestValidators.Update{entityName}Validator();");
        impl.AppendLine("        var validationResult = validator.Validate(request);");
        impl.AppendLine("        if (!validationResult.IsValid)");
        impl.AppendLine("            return new UseCaseResult.ValidationFailed(validationResult.Errors);");
        impl.AppendLine("        var existing = await _repo.GetByIdAsync(id, ct).ConfigureAwait(false);");
        impl.AppendLine("        if (existing is null)");
        impl.AppendLine($"            return new UseCaseResult.NotFound(\"{entityName}\", id.ToString()!);");
        impl.AppendLine("        existing.ApplyUpdate(request);");
        impl.AppendLine("        await _repo.UpdateAsync(existing, ct).ConfigureAwait(false);");
        impl.AppendLine("        return new UseCaseResult.Success();");
        impl.AppendLine("    }");
        impl.AppendLine("}");

        yield return new EmittedFile(CleanLayout.UseCaseInterfacePath(project, opName), iface.ToString());
        yield return new EmittedFile(CleanLayout.UseCaseImplPath(project, opName), impl.ToString());
    }

    static IEnumerable<EmittedFile> EmitPatchInteractor(string project, string entityName, string pkType)
    {
        // Patch follows the same pattern as Update but uses "Patch" as the verb.
        return EmitUpdateInteractor(project, entityName, pkType, "Patch");
    }

    static IEnumerable<EmittedFile> EmitDeleteInteractor(string project, string entityName, string pkType)
    {
        var opName    = $"Delete{entityName}";
        var ifaceNs   = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.UseCases";
        var implNs    = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";
        var repoAbsNs = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var ucNs      = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";

        // Interface
        var iface = new StringBuilder();
        iface.AppendLine($"using System.Threading;");
        iface.AppendLine($"using System.Threading.Tasks;");
        iface.AppendLine($"using {ucNs};");
        iface.AppendLine();
        iface.AppendLine($"namespace {ifaceNs};");
        iface.AppendLine();
        iface.AppendLine($"public interface I{opName}UseCase");
        iface.AppendLine("{");
        iface.AppendLine($"    Task<UseCaseResult> ExecuteAsync({pkType} id, CancellationToken ct);");
        iface.AppendLine("}");

        // Implementation
        var impl = new StringBuilder();
        impl.AppendLine($"using System.Threading;");
        impl.AppendLine($"using System.Threading.Tasks;");
        impl.AppendLine($"using {repoAbsNs};");
        impl.AppendLine($"using {ifaceNs};");
        impl.AppendLine($"using {ucNs};");
        impl.AppendLine();
        impl.AppendLine($"namespace {implNs};");
        impl.AppendLine();
        impl.AppendLine($"public sealed class {opName}UseCase : I{opName}UseCase");
        impl.AppendLine("{");
        impl.AppendLine($"    readonly I{entityName}Repository _repo;");
        impl.AppendLine();
        impl.AppendLine($"    public {opName}UseCase(I{entityName}Repository repo)");
        impl.AppendLine("    {");
        impl.AppendLine("        _repo = repo;");
        impl.AppendLine("    }");
        impl.AppendLine();
        impl.AppendLine($"    public async Task<UseCaseResult> ExecuteAsync({pkType} id, CancellationToken ct)");
        impl.AppendLine("    {");
        impl.AppendLine("        var existing = await _repo.GetByIdAsync(id, ct).ConfigureAwait(false);");
        impl.AppendLine("        if (existing is null)");
        impl.AppendLine($"            return new UseCaseResult.NotFound(\"{entityName}\", id.ToString()!);");
        impl.AppendLine("        await _repo.DeleteAsync(id, ct).ConfigureAwait(false);");
        impl.AppendLine("        return new UseCaseResult.Success();");
        impl.AppendLine("    }");
        impl.AppendLine("}");

        yield return new EmittedFile(CleanLayout.UseCaseInterfacePath(project, opName), iface.ToString());
        yield return new EmittedFile(CleanLayout.UseCaseImplPath(project, opName), impl.ToString());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    static string PkClrType(Table table)
    {
        var pk = table.PrimaryKey!;
        if (pk.ColumnNames.Count == 1)
        {
            var col = table.Columns.First(c =>
                string.Equals(c.Name, pk.ColumnNames[0], System.StringComparison.OrdinalIgnoreCase));
            return SqlTypeMap.ToCs(col.ClrType);
        }
        // Composite key — emit tuple syntax; note: composite-key routing is a TODO.
        var parts = pk.ColumnNames.Select(n =>
        {
            var c = table.Columns.First(col =>
                string.Equals(col.Name, n, System.StringComparison.OrdinalIgnoreCase));
            return SqlTypeMap.ToCs(c.ClrType);
        });
        return "(" + string.Join(", ", parts) + ")";
    }
}
