using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits per-entity-per-operation use-case interface + implementation pairs.
/// Entities with PK get full CRUD interactors; pk-less tables and views get List-only.
/// Post-Phase C: interactors take Application-internal <c>Command</c>/<c>Query</c> types and return
/// Application-internal <c>Model</c> payloads wrapped in <c>UseCaseResult&lt;T&gt;</c>. Domain factory
/// <c>&lt;Entity&gt;.Create(...)</c> is invoked on creates; repositories project to <c>&lt;Entity&gt;Model</c>.
/// No imports from <c>&lt;Project&gt;.Shared.*</c>.
/// Phase E: when <c>cfg.SplitRepositoriesByIntent == true</c>, Query interactors inject only
/// <c>I&lt;Entity&gt;ReadRepository</c>; Command interactors inject <c>I&lt;Entity&gt;WriteRepository</c>
/// (Create) or both Read + Write (Update/Patch/Delete).
/// </summary>
public sealed class UseCaseInteractorEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var list    = new List<EmittedFile>();
        var project = ctx.Config.ProjectName;
        var crud    = ctx.Config.Crud;
        var split   = ctx.Config.SplitRepositoriesByIntent;

        // Regular entities
        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.IsJoinTable) continue;

            if (entity.HasPrimaryKey)
                EmitFullInteractors(list, ctx, entity, project, crud, split);
            else
                EmitListOnlyInteractors(list, ctx, entity.EntityTypeName, entity.DbSetPropertyName, project, split);
        }

        // Views — list-only
        foreach (var view in ctx.Graph.Views)
        {
            var typeName = CasingHelper.ToPascalCase(Pluralizer.Singularize(view.Name));
            var plural   = CasingHelper.ToPascalCase(Pluralizer.Pluralize(Pluralizer.Singularize(view.Name)));
            EmitListOnlyInteractors(list, ctx, typeName, plural, project, split);
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
        CrudOperation crud,
        bool split)
    {
        var entityName = entity.EntityTypeName;
        var plural     = entity.DbSetPropertyName;
        var pkType     = PkClrType(entity.Table);

        if ((crud & CrudOperation.GetList) != 0)
            list.AddRange(EmitListInteractor(project, entityName, plural, split));

        if ((crud & CrudOperation.GetById) != 0)
            list.AddRange(EmitGetByIdInteractor(project, entityName, entity, split));

        if ((crud & CrudOperation.Post) != 0)
            list.AddRange(EmitCreateInteractor(project, entity, split));

        if ((crud & CrudOperation.Put) != 0)
            list.AddRange(EmitUpdateInteractor(project, entity, pkType, "Update", split));

        if ((crud & CrudOperation.Patch) != 0)
            list.AddRange(EmitPatchInteractor(project, entity, pkType, split));

        if ((crud & CrudOperation.Delete) != 0)
            list.AddRange(EmitDeleteInteractor(project, entity, pkType, split));
    }

    static void EmitListOnlyInteractors(
        List<EmittedFile> list,
        EmitterContext ctx,
        string typeName,
        string plural,
        string project,
        bool split)
    {
        list.AddRange(EmitListInteractor(project, typeName, plural, split));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Per-op emitters — each returns [interfaceFile, implFile]
    // ──────────────────────────────────────────────────────────────────────────

    static IEnumerable<EmittedFile> EmitListInteractor(string project, string entityName, string plural, bool split)
    {
        var opName    = $"List{plural}";
        var queryName = $"List{plural}Query";
        var ifaceNs   = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.UseCases";
        var implNs    = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";
        var repoAbsNs = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var ucNs      = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";
        var commonNs  = CleanLayout.ApplicationCommonNamespace(project);
        var modelsNs  = CleanLayout.ApplicationModelsNamespace(project);
        var queriesNs = CleanLayout.ApplicationQueriesNamespace(project);

        // Interface
        var iface = new StringBuilder();
        iface.AppendLine($"using System.Threading;");
        iface.AppendLine($"using System.Threading.Tasks;");
        iface.AppendLine($"using {commonNs};");
        iface.AppendLine($"using {modelsNs};");
        iface.AppendLine($"using {queriesNs};");
        iface.AppendLine($"using {ucNs};");
        iface.AppendLine();
        iface.AppendLine($"namespace {ifaceNs};");
        iface.AppendLine();
        iface.AppendLine($"public interface I{opName}UseCase");
        iface.AppendLine("{");
        iface.AppendLine($"    Task<UseCaseResult<PagedResult<{entityName}Model>>> ExecuteAsync({queryName} query, CancellationToken ct);");
        iface.AppendLine("}");

        // Implementation
        var repoFieldType = split ? $"I{entityName}ReadRepository" : $"I{entityName}Repository";
        var repoParamName = split ? "read" : "repo";
        var repoFieldName = split ? "_read" : "_repo";

        var impl = new StringBuilder();
        impl.AppendLine($"using System.Threading;");
        impl.AppendLine($"using System.Threading.Tasks;");
        impl.AppendLine($"using {repoAbsNs};");
        impl.AppendLine($"using {commonNs};");
        impl.AppendLine($"using {modelsNs};");
        impl.AppendLine($"using {queriesNs};");
        impl.AppendLine($"using {ifaceNs};");
        impl.AppendLine();
        impl.AppendLine($"namespace {implNs};");
        impl.AppendLine();
        impl.AppendLine($"public sealed class {opName}UseCase : I{opName}UseCase");
        impl.AppendLine("{");
        impl.AppendLine($"    readonly {repoFieldType} {repoFieldName};");
        impl.AppendLine();
        impl.AppendLine($"    public {opName}UseCase({repoFieldType} {repoParamName})");
        impl.AppendLine("    {");
        impl.AppendLine($"        {repoFieldName} = {repoParamName};");
        impl.AppendLine("    }");
        impl.AppendLine();
        impl.AppendLine($"    public async Task<UseCaseResult<PagedResult<{entityName}Model>>> ExecuteAsync({queryName} query, CancellationToken ct)");
        impl.AppendLine("    {");
        impl.AppendLine("        var page = query.Page < 1 ? 1 : query.Page;");
        impl.AppendLine("        var pageSize = query.PageSize < 1 ? 50 : query.PageSize;");
        impl.AppendLine($"        var result = await {repoFieldName}.ListAsync(page, pageSize, ct).ConfigureAwait(false);");
        impl.AppendLine($"        return new UseCaseResult<PagedResult<{entityName}Model>>.Success(result);");
        impl.AppendLine("    }");
        impl.AppendLine("}");

        yield return new EmittedFile(CleanLayout.UseCaseInterfacePath(project, opName), iface.ToString());
        yield return new EmittedFile(CleanLayout.UseCaseImplPath(project, opName), impl.ToString());
    }

    static IEnumerable<EmittedFile> EmitGetByIdInteractor(string project, string entityName, NamedEntity entity, bool split)
    {
        var opName    = $"Get{entityName}ById";
        var queryName = $"Get{entityName}ByIdQuery";
        var ifaceNs   = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.UseCases";
        var implNs    = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";
        var repoAbsNs = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var ucNs      = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";
        var commonNs  = CleanLayout.ApplicationCommonNamespace(project);
        var modelsNs  = CleanLayout.ApplicationModelsNamespace(project);
        var queriesNs = CleanLayout.ApplicationQueriesNamespace(project);

        var pk = entity.Table.PrimaryKey!;
        var pkNames = pk.ColumnNames.ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        var pkCols = entity.Table.Columns.Where(c => pkNames.Contains(c.Name)).ToList();
        var pkArgExpr = string.Join(", ", pkCols.Select(c => $"query.{EntityNaming.PropertyName(c)}"));
        var idDisplayExpr = pkCols.Count == 1
            ? $"query.{EntityNaming.PropertyName(pkCols[0])}.ToString()!"
            : "$\"(" + string.Join(", ", pkCols.Select(c => $"{{query.{EntityNaming.PropertyName(c)}}}")) + ")\"";

        // Interface
        var iface = new StringBuilder();
        iface.AppendLine($"using System.Threading;");
        iface.AppendLine($"using System.Threading.Tasks;");
        iface.AppendLine($"using {commonNs};");
        iface.AppendLine($"using {modelsNs};");
        iface.AppendLine($"using {queriesNs};");
        iface.AppendLine($"using {ucNs};");
        iface.AppendLine();
        iface.AppendLine($"namespace {ifaceNs};");
        iface.AppendLine();
        iface.AppendLine($"public interface I{opName}UseCase");
        iface.AppendLine("{");
        iface.AppendLine($"    Task<UseCaseResult<{entityName}Model>> ExecuteAsync({queryName} query, CancellationToken ct);");
        iface.AppendLine("}");

        // Implementation
        var repoFieldType = split ? $"I{entityName}ReadRepository" : $"I{entityName}Repository";
        var repoParamName = split ? "read" : "repo";
        var repoFieldName = split ? "_read" : "_repo";

        var impl = new StringBuilder();
        impl.AppendLine($"using System.Threading;");
        impl.AppendLine($"using System.Threading.Tasks;");
        impl.AppendLine($"using {repoAbsNs};");
        impl.AppendLine($"using {commonNs};");
        impl.AppendLine($"using {modelsNs};");
        impl.AppendLine($"using {queriesNs};");
        impl.AppendLine($"using {ifaceNs};");
        impl.AppendLine($"using {ucNs};");
        impl.AppendLine();
        impl.AppendLine($"namespace {implNs};");
        impl.AppendLine();
        impl.AppendLine($"public sealed class {opName}UseCase : I{opName}UseCase");
        impl.AppendLine("{");
        impl.AppendLine($"    readonly {repoFieldType} {repoFieldName};");
        impl.AppendLine();
        impl.AppendLine($"    public {opName}UseCase({repoFieldType} {repoParamName})");
        impl.AppendLine("    {");
        impl.AppendLine($"        {repoFieldName} = {repoParamName};");
        impl.AppendLine("    }");
        impl.AppendLine();
        impl.AppendLine($"    public async Task<UseCaseResult<{entityName}Model>> ExecuteAsync({queryName} query, CancellationToken ct)");
        impl.AppendLine("    {");
        impl.AppendLine($"        var model = await {repoFieldName}.GetByIdAsync({pkArgExpr}, ct).ConfigureAwait(false);");
        impl.AppendLine("        if (model is null)");
        impl.AppendLine($"            return new UseCaseResult<{entityName}Model>.NotFound(\"{entityName}\", {idDisplayExpr});");
        impl.AppendLine($"        return new UseCaseResult<{entityName}Model>.Success(model);");
        impl.AppendLine("    }");
        impl.AppendLine("}");

        yield return new EmittedFile(CleanLayout.UseCaseInterfacePath(project, opName), iface.ToString());
        yield return new EmittedFile(CleanLayout.UseCaseImplPath(project, opName), impl.ToString());
    }

    static IEnumerable<EmittedFile> EmitCreateInteractor(string project, NamedEntity entity, bool split)
    {
        var entityName = entity.EntityTypeName;
        var opName     = $"Create{entityName}";
        var commandName = $"Create{entityName}Command";
        var ifaceNs    = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.UseCases";
        var implNs     = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";
        var repoAbsNs  = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var entityNs   = $"{CleanLayout.DomainNamespace(project)}.Entities";
        var domainCommonNs = CleanLayout.DomainCommonNamespace(project);
        var ucNs       = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";
        var commonNs   = CleanLayout.ApplicationCommonNamespace(project);
        var modelsNs   = CleanLayout.ApplicationModelsNamespace(project);
        var commandsNs = CleanLayout.ApplicationCommandsNamespace(project);
        var errorsNs   = CleanLayout.ApplicationErrorsNamespace(project);
        var portsNs    = CleanLayout.PortsNamespace(project);

        var factoryArgs = entity.Table.Columns.Where(c => !c.IsServerGenerated).ToList();
        var factoryArgExpr = string.Join(", ", factoryArgs.Select(c => $"command.{EntityNaming.PropertyName(c)}"));

        // Interface
        var iface = new StringBuilder();
        iface.AppendLine($"using System.Threading;");
        iface.AppendLine($"using System.Threading.Tasks;");
        iface.AppendLine($"using {commonNs};");
        iface.AppendLine($"using {modelsNs};");
        iface.AppendLine($"using {commandsNs};");
        iface.AppendLine($"using {ucNs};");
        iface.AppendLine();
        iface.AppendLine($"namespace {ifaceNs};");
        iface.AppendLine();
        iface.AppendLine($"public interface I{opName}UseCase");
        iface.AppendLine("{");
        iface.AppendLine($"    Task<UseCaseResult<{entityName}Model>> ExecuteAsync({commandName} command, CancellationToken ct);");
        iface.AppendLine("}");

        // Implementation — Create only needs Write
        var impl = new StringBuilder();
        impl.AppendLine($"using System.Linq;");
        impl.AppendLine($"using System.Threading;");
        impl.AppendLine($"using System.Threading.Tasks;");
        impl.AppendLine($"using {portsNs};");
        impl.AppendLine($"using {repoAbsNs};");
        impl.AppendLine($"using {entityNs};");
        impl.AppendLine($"using {domainCommonNs};");
        impl.AppendLine($"using {commonNs};");
        impl.AppendLine($"using {errorsNs};");
        impl.AppendLine($"using {modelsNs};");
        impl.AppendLine($"using {commandsNs};");
        impl.AppendLine($"using {ifaceNs};");
        impl.AppendLine($"using {ucNs};");
        impl.AppendLine();
        impl.AppendLine($"namespace {implNs};");
        impl.AppendLine();
        impl.AppendLine($"public sealed class {opName}UseCase : I{opName}UseCase");
        impl.AppendLine("{");
        if (split)
        {
            impl.AppendLine($"    readonly I{entityName}WriteRepository _write;");
            impl.AppendLine("    readonly IUnitOfWork _uow;");
            impl.AppendLine();
            impl.AppendLine($"    public {opName}UseCase(I{entityName}WriteRepository write, IUnitOfWork uow)");
            impl.AppendLine("    {");
            impl.AppendLine("        _write = write;");
            impl.AppendLine("        _uow = uow;");
            impl.AppendLine("    }");
        }
        else
        {
            impl.AppendLine($"    readonly I{entityName}Repository _repo;");
            impl.AppendLine("    readonly IUnitOfWork _uow;");
            impl.AppendLine();
            impl.AppendLine($"    public {opName}UseCase(I{entityName}Repository repo, IUnitOfWork uow)");
            impl.AppendLine("    {");
            impl.AppendLine("        _repo = repo;");
            impl.AppendLine("        _uow = uow;");
            impl.AppendLine("    }");
        }
        impl.AppendLine();
        impl.AppendLine($"    public async Task<UseCaseResult<{entityName}Model>> ExecuteAsync({commandName} command, CancellationToken ct)");
        impl.AppendLine("    {");
        impl.AppendLine($"        var created = {entityName}.Create({factoryArgExpr});");
        impl.AppendLine($"        if (created is Result<{entityName}>.Failure failure)");
        impl.AppendLine("        {");
        impl.AppendLine("            var appErrors = failure.Errors");
        impl.AppendLine("                .Select(e => new ApplicationError(e.Field, e.Code, e.Message))");
        impl.AppendLine("                .ToArray();");
        impl.AppendLine($"            return new UseCaseResult<{entityName}Model>.ValidationFailed(appErrors);");
        impl.AppendLine("        }");
        impl.AppendLine($"        var entity = ((Result<{entityName}>.Success)created).Value;");
        if (split)
        {
            impl.AppendLine("        var model = await _write.CreateAsync(entity, ct).ConfigureAwait(false);");
            impl.AppendLine("        await _uow.CommitAsync(ct).ConfigureAwait(false);");
        }
        else
        {
            impl.AppendLine("        var model = await _repo.CreateAsync(entity, ct).ConfigureAwait(false);");
            impl.AppendLine("        await _uow.CommitAsync(ct).ConfigureAwait(false);");
        }
        impl.AppendLine($"        return new UseCaseResult<{entityName}Model>.Success(model);");
        impl.AppendLine("    }");
        impl.AppendLine("}");

        yield return new EmittedFile(CleanLayout.UseCaseInterfacePath(project, opName), iface.ToString());
        yield return new EmittedFile(CleanLayout.UseCaseImplPath(project, opName), impl.ToString());
    }

    static IEnumerable<EmittedFile> EmitUpdateInteractor(string project, NamedEntity entity, string pkType, string verb, bool split)
    {
        var entityName = entity.EntityTypeName;
        var opName     = $"{verb}{entityName}";
        var commandName = $"{verb}{entityName}Command";
        var ifaceNs    = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.UseCases";
        var implNs     = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";
        var repoAbsNs  = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var entityNs   = $"{CleanLayout.DomainNamespace(project)}.Entities";
        var ucNs       = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";
        var commonNs   = CleanLayout.ApplicationCommonNamespace(project);
        var commandsNs = CleanLayout.ApplicationCommandsNamespace(project);
        var portsNs    = CleanLayout.PortsNamespace(project);

        var pk = entity.Table.PrimaryKey!;
        var pkNames = pk.ColumnNames.ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        var pkCol = entity.Table.Columns.First(c => pkNames.Contains(c.Name));
        var pkProp = EntityNaming.PropertyName(pkCol);
        var allCols = entity.Table.Columns.ToList();

        // Interface
        var iface = new StringBuilder();
        iface.AppendLine($"using System.Threading;");
        iface.AppendLine($"using System.Threading.Tasks;");
        iface.AppendLine($"using {commonNs};");
        iface.AppendLine($"using {commandsNs};");
        iface.AppendLine($"using {ucNs};");
        iface.AppendLine();
        iface.AppendLine($"namespace {ifaceNs};");
        iface.AppendLine();
        iface.AppendLine($"public interface I{opName}UseCase");
        iface.AppendLine("{");
        iface.AppendLine($"    Task<UseCaseResult<Unit>> ExecuteAsync({commandName} command, CancellationToken ct);");
        iface.AppendLine("}");

        // Implementation — Update/Patch need both Read + Write
        var impl = new StringBuilder();
        impl.AppendLine($"using System.Threading;");
        impl.AppendLine($"using System.Threading.Tasks;");
        impl.AppendLine($"using {portsNs};");
        impl.AppendLine($"using {repoAbsNs};");
        impl.AppendLine($"using {entityNs};");
        impl.AppendLine($"using {commonNs};");
        impl.AppendLine($"using {commandsNs};");
        impl.AppendLine($"using {ifaceNs};");
        impl.AppendLine($"using {ucNs};");
        impl.AppendLine();
        impl.AppendLine($"namespace {implNs};");
        impl.AppendLine();
        impl.AppendLine($"public sealed class {opName}UseCase : I{opName}UseCase");
        impl.AppendLine("{");
        if (split)
        {
            impl.AppendLine($"    readonly I{entityName}ReadRepository _read;");
            impl.AppendLine($"    readonly I{entityName}WriteRepository _write;");
            impl.AppendLine("    readonly IUnitOfWork _uow;");
            impl.AppendLine();
            impl.AppendLine($"    public {opName}UseCase(I{entityName}ReadRepository read, I{entityName}WriteRepository write, IUnitOfWork uow)");
            impl.AppendLine("    {");
            impl.AppendLine("        _read = read;");
            impl.AppendLine("        _write = write;");
            impl.AppendLine("        _uow = uow;");
            impl.AppendLine("    }");
        }
        else
        {
            impl.AppendLine($"    readonly I{entityName}Repository _repo;");
            impl.AppendLine("    readonly IUnitOfWork _uow;");
            impl.AppendLine();
            impl.AppendLine($"    public {opName}UseCase(I{entityName}Repository repo, IUnitOfWork uow)");
            impl.AppendLine("    {");
            impl.AppendLine("        _repo = repo;");
            impl.AppendLine("        _uow = uow;");
            impl.AppendLine("    }");
        }
        impl.AppendLine();
        impl.AppendLine($"    public async Task<UseCaseResult<Unit>> ExecuteAsync({commandName} command, CancellationToken ct)");
        impl.AppendLine("    {");
        if (split)
        {
            impl.AppendLine($"        var existing = await _read.GetByIdAsync(command.{pkProp}, ct).ConfigureAwait(false);");
        }
        else
        {
            impl.AppendLine($"        var existing = await _repo.GetByIdAsync(command.{pkProp}, ct).ConfigureAwait(false);");
        }
        impl.AppendLine("        if (existing is null)");
        impl.AppendLine($"            return new UseCaseResult<Unit>.NotFound(\"{entityName}\", command.{pkProp}.ToString()!);");
        impl.AppendLine($"        var updated = new {entityName}");
        impl.AppendLine("        {");
        foreach (var col in allCols)
        {
            var prop = EntityNaming.PropertyName(col);
            impl.AppendLine($"            {prop} = command.{prop},");
        }
        impl.AppendLine("        };");
        if (split)
        {
            impl.AppendLine("        await _write.UpdateAsync(updated, ct).ConfigureAwait(false);");
        }
        else
        {
            impl.AppendLine("        await _repo.UpdateAsync(updated, ct).ConfigureAwait(false);");
        }
        impl.AppendLine("        await _uow.CommitAsync(ct).ConfigureAwait(false);");
        impl.AppendLine("        return new UseCaseResult<Unit>.Success(Unit.Value);");
        impl.AppendLine("    }");
        impl.AppendLine("}");

        yield return new EmittedFile(CleanLayout.UseCaseInterfacePath(project, opName), iface.ToString());
        yield return new EmittedFile(CleanLayout.UseCaseImplPath(project, opName), impl.ToString());
    }

    static IEnumerable<EmittedFile> EmitPatchInteractor(string project, NamedEntity entity, string pkType, bool split)
    {
        return EmitUpdateInteractor(project, entity, pkType, "Patch", split);
    }

    static IEnumerable<EmittedFile> EmitDeleteInteractor(string project, NamedEntity entity, string pkType, bool split)
    {
        var entityName  = entity.EntityTypeName;
        var opName      = $"Delete{entityName}";
        var commandName = $"Delete{entityName}Command";
        var ifaceNs     = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.UseCases";
        var implNs      = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";
        var repoAbsNs   = $"{CleanLayout.ApplicationNamespace(project)}.Abstractions.Repositories";
        var ucNs        = $"{CleanLayout.ApplicationNamespace(project)}.UseCases";
        var commonNs    = CleanLayout.ApplicationCommonNamespace(project);
        var commandsNs  = CleanLayout.ApplicationCommandsNamespace(project);
        var portsNs     = CleanLayout.PortsNamespace(project);

        var pk = entity.Table.PrimaryKey!;
        var pkNames = pk.ColumnNames.ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        var pkCols = entity.Table.Columns.Where(c => pkNames.Contains(c.Name)).ToList();
        var pkArgExpr = pkCols.Count == 1
            ? $"command.{EntityNaming.PropertyName(pkCols[0])}"
            : "(" + string.Join(", ", pkCols.Select(c => $"command.{EntityNaming.PropertyName(c)}")) + ")";
        var idDisplayExpr = pkCols.Count == 1
            ? $"command.{EntityNaming.PropertyName(pkCols[0])}.ToString()!"
            : "$\"(" + string.Join(", ", pkCols.Select(c => $"{{command.{EntityNaming.PropertyName(c)}}}")) + ")\"";

        // Interface
        var iface = new StringBuilder();
        iface.AppendLine($"using System.Threading;");
        iface.AppendLine($"using System.Threading.Tasks;");
        iface.AppendLine($"using {commonNs};");
        iface.AppendLine($"using {commandsNs};");
        iface.AppendLine($"using {ucNs};");
        iface.AppendLine();
        iface.AppendLine($"namespace {ifaceNs};");
        iface.AppendLine();
        iface.AppendLine($"public interface I{opName}UseCase");
        iface.AppendLine("{");
        iface.AppendLine($"    Task<UseCaseResult<Unit>> ExecuteAsync({commandName} command, CancellationToken ct);");
        iface.AppendLine("}");

        // Implementation — Delete needs both Read (existence check) + Write
        var impl = new StringBuilder();
        impl.AppendLine($"using System.Threading;");
        impl.AppendLine($"using System.Threading.Tasks;");
        impl.AppendLine($"using {portsNs};");
        impl.AppendLine($"using {repoAbsNs};");
        impl.AppendLine($"using {commonNs};");
        impl.AppendLine($"using {commandsNs};");
        impl.AppendLine($"using {ifaceNs};");
        impl.AppendLine($"using {ucNs};");
        impl.AppendLine();
        impl.AppendLine($"namespace {implNs};");
        impl.AppendLine();
        impl.AppendLine($"public sealed class {opName}UseCase : I{opName}UseCase");
        impl.AppendLine("{");
        if (split)
        {
            impl.AppendLine($"    readonly I{entityName}ReadRepository _read;");
            impl.AppendLine($"    readonly I{entityName}WriteRepository _write;");
            impl.AppendLine("    readonly IUnitOfWork _uow;");
            impl.AppendLine();
            impl.AppendLine($"    public {opName}UseCase(I{entityName}ReadRepository read, I{entityName}WriteRepository write, IUnitOfWork uow)");
            impl.AppendLine("    {");
            impl.AppendLine("        _read = read;");
            impl.AppendLine("        _write = write;");
            impl.AppendLine("        _uow = uow;");
            impl.AppendLine("    }");
        }
        else
        {
            impl.AppendLine($"    readonly I{entityName}Repository _repo;");
            impl.AppendLine("    readonly IUnitOfWork _uow;");
            impl.AppendLine();
            impl.AppendLine($"    public {opName}UseCase(I{entityName}Repository repo, IUnitOfWork uow)");
            impl.AppendLine("    {");
            impl.AppendLine("        _repo = repo;");
            impl.AppendLine("        _uow = uow;");
            impl.AppendLine("    }");
        }
        impl.AppendLine();
        impl.AppendLine($"    public async Task<UseCaseResult<Unit>> ExecuteAsync({commandName} command, CancellationToken ct)");
        impl.AppendLine("    {");
        if (split)
        {
            impl.AppendLine($"        var existing = await _read.GetByIdAsync({pkArgExpr}, ct).ConfigureAwait(false);");
        }
        else
        {
            impl.AppendLine($"        var existing = await _repo.GetByIdAsync({pkArgExpr}, ct).ConfigureAwait(false);");
        }
        impl.AppendLine("        if (existing is null)");
        impl.AppendLine($"            return new UseCaseResult<Unit>.NotFound(\"{entityName}\", {idDisplayExpr});");
        if (split)
        {
            impl.AppendLine($"        await _write.DeleteAsync({pkArgExpr}, ct).ConfigureAwait(false);");
        }
        else
        {
            impl.AppendLine($"        await _repo.DeleteAsync({pkArgExpr}, ct).ConfigureAwait(false);");
        }
        impl.AppendLine("        await _uow.CommitAsync(ct).ConfigureAwait(false);");
        impl.AppendLine("        return new UseCaseResult<Unit>.Success(Unit.Value);");
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
