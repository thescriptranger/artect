using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits per-entity, per-CRUD-op Handler classes into
/// Application/Features/&lt;Plural&gt;/. Each handler injects I&lt;Entity&gt;Repository
/// and IUnitOfWork, runs the operation, and commits exactly once. Handlers are
/// sealed partial classes so the user adds business logic via OnBefore/OnAfter
/// hooks in a sibling .Hooks.cs file without touching generated code.
///
/// V#6: Create/Update/Patch handlers implement
/// <c>ICommandHandler&lt;TCommand, TResult&gt;</c> from
/// <c>&lt;Project&gt;.Application.Abstractions</c> so cross-cutting decorators
/// (validation, transaction, audit, authorization) can wrap them uniformly.
/// Delete is currently exempt — its signature takes a primitive id, not a
/// command record; V#16 will refactor.
/// </summary>
public sealed class HandlerEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var crud = ctx.Config.Crud;
        var anyWrite = (crud & (CrudOperation.Post | CrudOperation.Put | CrudOperation.Patch | CrudOperation.Delete)) != 0;
        if (!anyWrite) return System.Array.Empty<EmittedFile>();

        var list = new List<EmittedFile>();
        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.ShouldSkip(EntityClassification.AggregateRoot)) continue;

            var name         = entity.EntityTypeName;
            var nonServerGen = entity.Table.Columns.Where(c => !c.IsServerGenerated).ToList();

            if ((crud & CrudOperation.Post) != 0)
                list.Add(BuildCreate(ctx, entity, name, nonServerGen));
            if ((crud & CrudOperation.Put) != 0)
            {
                var f = BuildUpdate(ctx, entity, name, "Update");
                if (f is not null) list.Add(f);
            }
            if ((crud & CrudOperation.Patch) != 0)
            {
                var f = BuildPatch(ctx, entity, name);
                if (f is not null) list.Add(f);
            }
            if ((crud & CrudOperation.Delete) != 0)
                list.Add(BuildDelete(ctx, entity, name));
        }
        return list;
    }

    static EmittedFile BuildCreate(EmitterContext ctx, NamedEntity entity, string name, IReadOnlyList<Column> nonServerGen)
    {
        var project = ctx.Config.ProjectName;
        var corrections = ctx.NamingCorrections;

        var ns         = CleanLayout.ApplicationFeatureNamespace(project, name);
        var entityNs   = $"{CleanLayout.DomainNamespace(project)}.Entities";
        var domainNs   = CleanLayout.DomainCommonNamespace(project);
        var dtosNs     = CleanLayout.ApplicationDtosNamespace(project);
        var absNs      = CleanLayout.ApplicationFeatureAbstractionsNamespace(project, name);
        var appAbsNs   = CleanLayout.ApplicationAbstractionsNamespace(project);
        var mappingsNs = CleanLayout.ApplicationMappingsNamespace(project);

        var sb = new StringBuilder();
        sb.AppendLine($"using {entityNs};");
        sb.AppendLine($"using {domainNs};");
        sb.AppendLine($"using {dtosNs};");
        sb.AppendLine($"using {absNs};");
        sb.AppendLine($"using {appAbsNs};");
        sb.AppendLine($"using {mappingsNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"public sealed partial class Create{name}Handler(I{name}Repository repository, IUnitOfWork unitOfWork)");
        sb.AppendLine($"    : ICommandHandler<Create{name}Command, {name}Dto>");
        sb.AppendLine("{");
        sb.AppendLine($"    public async Task<{name}Dto> HandleAsync(Create{name}Command command, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var result = {name}.Create(");
        for (int i = 0; i < nonServerGen.Count; i++)
        {
            var col = nonServerGen[i];
            var paramName = CasingHelper.ToCamelCase(col.Name, corrections);
            var prop      = EntityNaming.PropertyName(col, corrections);
            var terminator = i == nonServerGen.Count - 1 ? ");" : ",";
            sb.AppendLine($"            command.{prop}{terminator}");
            _ = paramName;
        }
        sb.AppendLine();
        sb.AppendLine($"        if (result is Result<{name}>.Failure failure)");
        sb.AppendLine("            throw new DomainValidationException(failure.Errors);");
        sb.AppendLine();
        sb.AppendLine($"        var entity = ((Result<{name}>.Success)result).Value;");
        sb.AppendLine();
        sb.AppendLine("        OnBeforeAdd(command, entity);");
        sb.AppendLine("        await repository.AddAsync(entity, ct).ConfigureAwait(false);");
        sb.AppendLine("        OnBeforeCommit(command, entity);");
        sb.AppendLine("        await unitOfWork.CommitAsync(ct).ConfigureAwait(false);");
        sb.AppendLine("        OnAfterCommit(command, entity);");
        sb.AppendLine();
        sb.AppendLine($"        return DtoMapper.Map<{name}, {name}Dto>(entity);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    partial void OnBeforeAdd(Create{name}Command command, {name} entity);");
        sb.AppendLine($"    partial void OnBeforeCommit(Create{name}Command command, {name} entity);");
        sb.AppendLine($"    partial void OnAfterCommit(Create{name}Command command, {name} entity);");
        sb.AppendLine("}");

        var path = CleanLayout.ApplicationFeaturePath(project, name, $"Create{name}Handler");
        return new EmittedFile(path, sb.ToString());
    }

    static EmittedFile? BuildUpdate(EmitterContext ctx, NamedEntity entity, string name, string verb)
    {
        var updateArgs = entity.UpdateableColumns();
        // No mutable columns -> no Update method on the entity -> no handler. V#5 will revisit
        // for PATCH-with-Optional<T>; for now PATCH and PUT share this code path.
        if (updateArgs.Count == 0) return null;

        var project = ctx.Config.ProjectName;
        var corrections = ctx.NamingCorrections;

        var pk = entity.Table.PrimaryKey!;
        var pkColName = pk.ColumnNames[0];
        var pkCol = entity.Table.Columns.First(c =>
            string.Equals(c.Name, pkColName, System.StringComparison.OrdinalIgnoreCase));
        var pkProp = EntityNaming.PropertyName(pkCol, corrections);

        var ns         = CleanLayout.ApplicationFeatureNamespace(project, name);
        var entityNs   = $"{CleanLayout.DomainNamespace(project)}.Entities";
        var domainNs   = CleanLayout.DomainCommonNamespace(project);
        var dtosNs     = CleanLayout.ApplicationDtosNamespace(project);
        var absNs      = CleanLayout.ApplicationFeatureAbstractionsNamespace(project, name);
        var appAbsNs   = CleanLayout.ApplicationAbstractionsNamespace(project);
        var mappingsNs = CleanLayout.ApplicationMappingsNamespace(project);

        var commandType = $"{verb}{name}Command";
        var handlerName = $"{verb}{name}Handler";

        var sb = new StringBuilder();
        sb.AppendLine($"using {entityNs};");
        sb.AppendLine($"using {domainNs};");
        sb.AppendLine($"using {dtosNs};");
        sb.AppendLine($"using {absNs};");
        sb.AppendLine($"using {appAbsNs};");
        sb.AppendLine($"using {mappingsNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"public sealed partial class {handlerName}(I{name}Repository repository, IUnitOfWork unitOfWork)");
        sb.AppendLine($"    : ICommandHandler<{commandType}, {name}Dto?>");
        sb.AppendLine("{");
        sb.AppendLine($"    public async Task<{name}Dto?> HandleAsync({commandType} command, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var existing = await repository.GetByIdAsync(command.{pkProp}, ct).ConfigureAwait(false);");
        sb.AppendLine("        if (existing is null) return null;");
        sb.AppendLine();
        sb.AppendLine("        OnBeforeUpdate(command, existing);");
        sb.AppendLine("        var result = existing.Update(");
        for (int i = 0; i < updateArgs.Count; i++)
        {
            var col = updateArgs[i];
            var prop = EntityNaming.PropertyName(col, corrections);
            var terminator = i == updateArgs.Count - 1 ? ");" : ",";
            sb.AppendLine($"            command.{prop}{terminator}");
        }
        sb.AppendLine();
        sb.AppendLine($"        if (result is Result<{name}>.Failure failure)");
        sb.AppendLine("            throw new DomainValidationException(failure.Errors);");
        sb.AppendLine();
        sb.AppendLine("        OnBeforeCommit(command, existing);");
        sb.AppendLine("        await unitOfWork.CommitAsync(ct).ConfigureAwait(false);");
        sb.AppendLine("        OnAfterCommit(command, existing);");
        sb.AppendLine();
        sb.AppendLine($"        return DtoMapper.Map<{name}, {name}Dto>(existing);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    partial void OnBeforeUpdate({commandType} command, {name} existing);");
        sb.AppendLine($"    partial void OnBeforeCommit({commandType} command, {name} entity);");
        sb.AppendLine($"    partial void OnAfterCommit({commandType} command, {name} entity);");
        sb.AppendLine("}");

        var path = CleanLayout.ApplicationFeaturePath(project, name, handlerName);
        return new EmittedFile(path, sb.ToString());
    }

    /// <summary>
    /// V#5: PATCH handler. Each non-PK field on the command is Optional&lt;T?&gt;.
    /// Fields with HasValue=true are applied; fields with HasValue=false fall back to
    /// the existing entity's value. The merge is built into the existing.Update(...)
    /// call so domain invariants are still validated against the final state. Skips
    /// emission when the entity has no updateable columns.
    /// </summary>
    static EmittedFile? BuildPatch(EmitterContext ctx, NamedEntity entity, string name)
    {
        var updateArgs = entity.UpdateableColumns();
        if (updateArgs.Count == 0) return null;

        var project = ctx.Config.ProjectName;
        var corrections = ctx.NamingCorrections;

        var pk = entity.Table.PrimaryKey!;
        var pkColName = pk.ColumnNames[0];
        var pkCol = entity.Table.Columns.First(c =>
            string.Equals(c.Name, pkColName, System.StringComparison.OrdinalIgnoreCase));
        var pkProp = EntityNaming.PropertyName(pkCol, corrections);

        var ns         = CleanLayout.ApplicationFeatureNamespace(project, name);
        var entityNs   = $"{CleanLayout.DomainNamespace(project)}.Entities";
        var domainNs   = CleanLayout.DomainCommonNamespace(project);
        var dtosNs     = CleanLayout.ApplicationDtosNamespace(project);
        var absNs      = CleanLayout.ApplicationFeatureAbstractionsNamespace(project, name);
        var appAbsNs   = CleanLayout.ApplicationAbstractionsNamespace(project);
        var mappingsNs = CleanLayout.ApplicationMappingsNamespace(project);
        var sharedCommonNs = $"{project}.Shared.Common";

        var commandType = $"Patch{name}Command";
        var handlerName = $"Patch{name}Handler";

        var sb = new StringBuilder();
        sb.AppendLine($"using {entityNs};");
        sb.AppendLine($"using {domainNs};");
        sb.AppendLine($"using {dtosNs};");
        sb.AppendLine($"using {absNs};");
        sb.AppendLine($"using {appAbsNs};");
        sb.AppendLine($"using {mappingsNs};");
        sb.AppendLine($"using {sharedCommonNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"public sealed partial class {handlerName}(I{name}Repository repository, IUnitOfWork unitOfWork)");
        sb.AppendLine($"    : ICommandHandler<{commandType}, {name}Dto?>");
        sb.AppendLine("{");
        sb.AppendLine($"    public async Task<{name}Dto?> HandleAsync({commandType} command, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var existing = await repository.GetByIdAsync(command.{pkProp}, ct).ConfigureAwait(false);");
        sb.AppendLine("        if (existing is null) return null;");
        sb.AppendLine();
        sb.AppendLine("        OnBeforePatch(command, existing);");
        sb.AppendLine("        var result = existing.Update(");
        for (int i = 0; i < updateArgs.Count; i++)
        {
            var col = updateArgs[i];
            var prop = EntityNaming.PropertyName(col, corrections);
            var terminator = i == updateArgs.Count - 1 ? ");" : ",";
            // Optional<T?>.Value is T?. The entity's Update parameter type is T (matches
            // the column's nullability). For non-nullable columns, ! forgives the
            // compile-time nullable annotation; if the user actually sent JSON null, the
            // domain Update method's invariant check rejects it.
            var unwrap = col.IsNullable
                ? ".Value"
                : SqlTypeMap.IsValueType(col.ClrType) ? ".Value!.Value" : ".Value!";
            sb.AppendLine($"            command.{prop}.HasValue ? command.{prop}{unwrap} : existing.{prop}{terminator}");
        }
        sb.AppendLine();
        sb.AppendLine($"        if (result is Result<{name}>.Failure failure)");
        sb.AppendLine("            throw new DomainValidationException(failure.Errors);");
        sb.AppendLine();
        sb.AppendLine("        OnBeforeCommit(command, existing);");
        sb.AppendLine("        await unitOfWork.CommitAsync(ct).ConfigureAwait(false);");
        sb.AppendLine("        OnAfterCommit(command, existing);");
        sb.AppendLine();
        sb.AppendLine($"        return DtoMapper.Map<{name}, {name}Dto>(existing);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    partial void OnBeforePatch({commandType} command, {name} existing);");
        sb.AppendLine($"    partial void OnBeforeCommit({commandType} command, {name} entity);");
        sb.AppendLine($"    partial void OnAfterCommit({commandType} command, {name} entity);");
        sb.AppendLine("}");

        var path = CleanLayout.ApplicationFeaturePath(project, name, handlerName);
        return new EmittedFile(path, sb.ToString());
    }

    static EmittedFile BuildDelete(EmitterContext ctx, NamedEntity entity, string name)
    {
        var project = ctx.Config.ProjectName;
        var corrections = ctx.NamingCorrections;

        var pk = entity.Table.PrimaryKey!;
        var pkColName = pk.ColumnNames[0];
        var pkCol = entity.Table.Columns.First(c =>
            string.Equals(c.Name, pkColName, System.StringComparison.OrdinalIgnoreCase));
        var pkType = SqlTypeMap.ToCs(pkCol.ClrType);

        var ns       = CleanLayout.ApplicationFeatureNamespace(project, name);
        var entityNs = $"{CleanLayout.DomainNamespace(project)}.Entities";
        var absNs    = CleanLayout.ApplicationFeatureAbstractionsNamespace(project, name);
        var appAbsNs = CleanLayout.ApplicationAbstractionsNamespace(project);

        var handlerName = $"Delete{name}Handler";

        var sb = new StringBuilder();
        sb.AppendLine($"using {entityNs};");
        sb.AppendLine($"using {absNs};");
        sb.AppendLine($"using {appAbsNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"public sealed partial class {handlerName}(I{name}Repository repository, IUnitOfWork unitOfWork)");
        sb.AppendLine("{");
        sb.AppendLine($"    public async Task<bool> HandleAsync({pkType} id, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine("        var existing = await repository.GetByIdAsync(id, ct).ConfigureAwait(false);");
        sb.AppendLine("        if (existing is null) return false;");
        sb.AppendLine();
        sb.AppendLine("        OnBeforeRemove(existing);");
        sb.AppendLine("        repository.Remove(existing);");
        sb.AppendLine("        OnBeforeCommit(existing);");
        sb.AppendLine("        await unitOfWork.CommitAsync(ct).ConfigureAwait(false);");
        sb.AppendLine("        OnAfterCommit(existing);");
        sb.AppendLine();
        sb.AppendLine("        return true;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    partial void OnBeforeRemove({name} entity);");
        sb.AppendLine($"    partial void OnBeforeCommit({name} entity);");
        sb.AppendLine($"    partial void OnAfterCommit({name} entity);");
        sb.AppendLine("}");

        var path = CleanLayout.ApplicationFeaturePath(project, name, handlerName);
        return new EmittedFile(path, sb.ToString());
    }
}
