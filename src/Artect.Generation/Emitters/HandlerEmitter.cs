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
            if (entity.IsJoinTable) continue;
            if (!entity.HasPrimaryKey) continue;

            var name         = entity.EntityTypeName;
            var nonServerGen = entity.Table.Columns.Where(c => !c.IsServerGenerated).ToList();
            var allCols      = entity.Table.Columns.ToList();

            if ((crud & CrudOperation.Post) != 0)
                list.Add(BuildCreate(ctx, entity, name, nonServerGen));
            if ((crud & CrudOperation.Put) != 0)
                list.Add(BuildUpdate(ctx, entity, name, allCols, "Update"));
            if ((crud & CrudOperation.Patch) != 0)
                list.Add(BuildUpdate(ctx, entity, name, allCols, "Patch"));
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

    static EmittedFile BuildUpdate(EmitterContext ctx, NamedEntity entity, string name, IReadOnlyList<Column> allCols, string verb)
    {
        var project = ctx.Config.ProjectName;
        var corrections = ctx.NamingCorrections;

        var pk = entity.Table.PrimaryKey!;
        var pkColName = pk.ColumnNames[0];
        var pkCol = entity.Table.Columns.First(c =>
            string.Equals(c.Name, pkColName, System.StringComparison.OrdinalIgnoreCase));
        var pkProp = EntityNaming.PropertyName(pkCol, corrections);

        var ns         = CleanLayout.ApplicationFeatureNamespace(project, name);
        var entityNs   = $"{CleanLayout.DomainNamespace(project)}.Entities";
        var dtosNs     = CleanLayout.ApplicationDtosNamespace(project);
        var absNs      = CleanLayout.ApplicationFeatureAbstractionsNamespace(project, name);
        var appAbsNs   = CleanLayout.ApplicationAbstractionsNamespace(project);
        var mappingsNs = CleanLayout.ApplicationMappingsNamespace(project);

        var commandType = $"{verb}{name}Command";
        var handlerName = $"{verb}{name}Handler";

        var sb = new StringBuilder();
        sb.AppendLine($"using {entityNs};");
        sb.AppendLine($"using {dtosNs};");
        sb.AppendLine($"using {absNs};");
        sb.AppendLine($"using {appAbsNs};");
        sb.AppendLine($"using {mappingsNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"public sealed partial class {handlerName}(I{name}Repository repository, IUnitOfWork unitOfWork)");
        sb.AppendLine("{");
        sb.AppendLine($"    public async Task<{name}Dto?> HandleAsync({commandType} command, CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var existing = await repository.GetByIdAsync(command.{pkProp}, ct).ConfigureAwait(false);");
        sb.AppendLine("        if (existing is null) return null;");
        sb.AppendLine();
        sb.AppendLine($"        var replacement = new {name}");
        sb.AppendLine("        {");
        foreach (var col in allCols)
        {
            var prop = EntityNaming.PropertyName(col, corrections);
            sb.AppendLine($"            {prop} = command.{prop},");
        }
        sb.AppendLine("        };");
        sb.AppendLine();
        sb.AppendLine("        OnBeforeApplyChanges(command, existing, replacement);");
        sb.AppendLine("        repository.ApplyChanges(existing, replacement);");
        sb.AppendLine("        OnBeforeCommit(command, existing);");
        sb.AppendLine("        await unitOfWork.CommitAsync(ct).ConfigureAwait(false);");
        sb.AppendLine("        OnAfterCommit(command, existing);");
        sb.AppendLine();
        sb.AppendLine($"        return DtoMapper.Map<{name}, {name}Dto>(existing);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    partial void OnBeforeApplyChanges({commandType} command, {name} existing, {name} replacement);");
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
