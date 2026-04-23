using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits one <c>&lt;Entity&gt;ApiMappers.cs</c> per entity into <c>src/&lt;Project&gt;.Api/Mapping/</c>.
/// Maps Shared wire contracts (Request/Response) to/from Application-internal types (Command/Query/Model).
/// </summary>
public sealed class EntityApiMappersEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var list = new List<EmittedFile>();
        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.IsJoinTable) continue;
            if (!entity.HasPrimaryKey) continue;
            list.Add(Build(ctx, entity));
        }
        return list;
    }

    static EmittedFile Build(EmitterContext ctx, NamedEntity entity)
    {
        var project = ctx.Config.ProjectName;
        var entityName = entity.EntityTypeName;
        var sharedRequestsNs = CleanLayout.SharedRequestsNamespace(project);
        var sharedResponsesNs = CleanLayout.SharedResponsesNamespace(project);
        var cmdNs = CleanLayout.ApplicationCommandsNamespace(project);
        var queryNs = CleanLayout.ApplicationQueriesNamespace(project);
        var modelNs = CleanLayout.ApplicationModelsNamespace(project);
        var apiMapperNs = CleanLayout.ApiMappingNamespace(project);

        var sb = new StringBuilder();
        sb.AppendLine($"using {sharedRequestsNs};");
        sb.AppendLine($"using {sharedResponsesNs};");
        sb.AppendLine($"using {cmdNs};");
        sb.AppendLine($"using {queryNs};");
        sb.AppendLine($"using {modelNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {apiMapperNs};");
        sb.AppendLine();
        sb.AppendLine($"internal static class {entityName}ApiMappers");
        sb.AppendLine("{");

        var corrections = ctx.NamingCorrections;
        if (ctx.Config.Crud.HasFlag(CrudOperation.Post))
            EmitCreateMapper(sb, entity, corrections);
        if (ctx.Config.Crud.HasFlag(CrudOperation.Put))
            EmitUpdateMapper(sb, entity, "Update", corrections);
        if (ctx.Config.Crud.HasFlag(CrudOperation.Patch))
            EmitUpdateMapper(sb, entity, "Patch", corrections);
        if (ctx.Config.Crud.HasFlag(CrudOperation.Delete))
            EmitDeleteMapper(sb, entity, corrections);
        if (ctx.Config.Crud.HasFlag(CrudOperation.GetById))
            EmitGetByIdMapper(sb, entity, corrections);
        if (ctx.Config.Crud.HasFlag(CrudOperation.GetList))
            EmitListMapper(sb, entity, corrections);

        EmitResponseMapper(sb, entity, corrections);

        sb.AppendLine("}");
        return new EmittedFile(
            CleanLayout.ApiMapperPath(project, $"{entityName}ApiMappers"),
            sb.ToString());
    }

    static void EmitCreateMapper(StringBuilder sb, NamedEntity entity, System.Collections.Generic.IReadOnlyDictionary<string, string> corrections)
    {
        var e = entity.EntityTypeName;
        var nonPkCols = entity.Table.Columns.Where(c => !c.IsServerGenerated).ToList();
        var assigns = string.Join(", ", nonPkCols.Select(c =>
        {
            var name = EntityNaming.PropertyName(c, corrections);
            return $"{name} = r.{name}";
        }));
        sb.AppendLine($"    public static Create{e}Command ToCommand(this Create{e}Request r) =>");
        sb.AppendLine($"        new() {{ {assigns} }};");
        sb.AppendLine();
    }

    static void EmitUpdateMapper(StringBuilder sb, NamedEntity entity, string op, System.Collections.Generic.IReadOnlyDictionary<string, string> corrections)
    {
        var e = entity.EntityTypeName;
        var pk = entity.Table.PrimaryKey!;
        var pkArgs = string.Join(", ", pk.ColumnNames.Select(n =>
        {
            var col = entity.Table.Columns.First(c => c.Name == n);
            var cs = SqlTypeMap.ToCs(col.ClrType);
            return $"{cs} {CasingHelper.ToCamelCase(n, corrections)}";
        }));
        var pkInits = string.Join(", ", pk.ColumnNames.Select(n =>
        {
            var col = entity.Table.Columns.First(c => c.Name == n);
            return $"{EntityNaming.PropertyName(col, corrections)} = {CasingHelper.ToCamelCase(n, corrections)}";
        }));
        var nonPkAssigns = string.Join(", ", entity.Table.Columns
            .Where(c => !pk.ColumnNames.Contains(c.Name))
            .Select(c =>
            {
                var name = EntityNaming.PropertyName(c, corrections);
                return $"{name} = r.{name}";
            }));
        var allInits = string.Join(", ", new[] { pkInits, nonPkAssigns }.Where(s => !string.IsNullOrEmpty(s)));
        sb.AppendLine($"    public static {op}{e}Command To{op}Command(this Update{e}Request r, {pkArgs}) =>");
        sb.AppendLine($"        new() {{ {allInits} }};");
        sb.AppendLine();
    }

    static void EmitDeleteMapper(StringBuilder sb, NamedEntity entity, System.Collections.Generic.IReadOnlyDictionary<string, string> corrections)
    {
        var e = entity.EntityTypeName;
        var pk = entity.Table.PrimaryKey!;
        var pkArgs = string.Join(", ", pk.ColumnNames.Select(n =>
        {
            var col = entity.Table.Columns.First(c => c.Name == n);
            var cs = SqlTypeMap.ToCs(col.ClrType);
            return $"{cs} {CasingHelper.ToCamelCase(n, corrections)}";
        }));
        var pkInits = string.Join(", ", pk.ColumnNames.Select(n =>
        {
            var col = entity.Table.Columns.First(c => c.Name == n);
            return $"{EntityNaming.PropertyName(col, corrections)} = {CasingHelper.ToCamelCase(n, corrections)}";
        }));
        sb.AppendLine($"    public static Delete{e}Command ToDeleteCommand({pkArgs}) =>");
        sb.AppendLine($"        new() {{ {pkInits} }};");
        sb.AppendLine();
    }

    static void EmitGetByIdMapper(StringBuilder sb, NamedEntity entity, System.Collections.Generic.IReadOnlyDictionary<string, string> corrections)
    {
        var e = entity.EntityTypeName;
        var pk = entity.Table.PrimaryKey!;
        var pkArgs = string.Join(", ", pk.ColumnNames.Select(n =>
        {
            var col = entity.Table.Columns.First(c => c.Name == n);
            var cs = SqlTypeMap.ToCs(col.ClrType);
            return $"{cs} {CasingHelper.ToCamelCase(n, corrections)}";
        }));
        var pkArgsForRecord = string.Join(", ", pk.ColumnNames.Select(n => CasingHelper.ToCamelCase(n, corrections)));
        sb.AppendLine($"    public static Get{e}ByIdQuery ToGetByIdQuery({pkArgs}) =>");
        sb.AppendLine($"        new({pkArgsForRecord});");
        sb.AppendLine();
    }

    static void EmitListMapper(StringBuilder sb, NamedEntity entity, System.Collections.Generic.IReadOnlyDictionary<string, string> corrections)
    {
        var plural = CasingHelper.ToPascalCase(Pluralizer.Pluralize(entity.EntityTypeName), corrections);
        sb.AppendLine($"    public static List{plural}Query ToListQuery(int page, int pageSize) =>");
        sb.AppendLine($"        new(page, pageSize);");
        sb.AppendLine();
    }

    static void EmitResponseMapper(StringBuilder sb, NamedEntity entity, System.Collections.Generic.IReadOnlyDictionary<string, string> corrections)
    {
        var e = entity.EntityTypeName;
        var assigns = string.Join(", ", entity.Table.Columns.Select(c =>
        {
            var name = EntityNaming.PropertyName(c, corrections);
            return $"{name} = m.{name}";
        }));
        sb.AppendLine($"    public static {e}Response ToResponse(this {e}Model m) =>");
        sb.AppendLine($"        new() {{ {assigns} }};");
    }
}
