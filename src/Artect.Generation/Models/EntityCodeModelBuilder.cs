using System;
using System.Collections.Generic;
using System.Linq;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Generation.Emitters;
using Artect.Naming;

namespace Artect.Generation.Models;

/// <summary>
/// V#16 canonical example: pure static builder. Takes the schema-derived
/// <see cref="NamedEntity"/> + <see cref="EmitterContext"/> and returns the
/// fully-populated <see cref="EntityCodeModel"/> ready for the renderer. No I/O,
/// no template loading, no path construction — those concerns belong to
/// <see cref="EntityEmitter"/> and <see cref="CleanLayout"/> respectively.
/// </summary>
public static class EntityCodeModelBuilder
{
    public static EntityCodeModel Build(EmitterContext ctx, NamedEntity entity)
    {
        var corrections = ctx.NamingCorrections;
        var commonNs = CleanLayout.DomainCommonNamespace(ctx.Config.ProjectName);
        var visibleColumns = entity.Table.Columns
            .Where(c => !entity.ColumnHasFlag(c.Name, ColumnMetadata.Ignored))
            .ToList();
        var factoryArgs = visibleColumns
            .Where(c => !c.IsServerGenerated)
            .ToList();
        var updateArgs = entity.UpdateableColumns();

        var invariantLines = BuildInvariants(factoryArgs, corrections, commonNs);
        var updateInvariantLines = BuildInvariants(updateArgs, corrections, commonNs);

        var createArgs = factoryArgs
            .Select((c, i) => new EntityArgView(
                ClrTypeWithNullability: ClrTypeString(c),
                ParamName: CasingHelper.ToCamelCase(c.Name, corrections),
                PropertyName: EntityNaming.PropertyName(c, corrections),
                Comma: i < factoryArgs.Count - 1 ? "," : ""))
            .ToList();

        var updateArgsForTemplate = updateArgs
            .Select((c, i) => new EntityArgView(
                ClrTypeWithNullability: ClrTypeString(c),
                ParamName: CasingHelper.ToCamelCase(c.Name, corrections),
                PropertyName: EntityNaming.PropertyName(c, corrections),
                Comma: i < updateArgs.Count - 1 ? "," : ""))
            .ToList();

        var emitBehavior = entity.EmitsBehavior();

        // V#12: when the entity carries a SoftDeleteFlag column AND emits behavior, we
        // generate a SoftDelete() domain method that the repository's Remove() calls
        // instead of physically deleting. The exact assignment depends on the flag
        // column's CLR type — bool flips to true, nullable DateTime/DateTimeOffset stamps
        // UtcNow.
        string? softDeleteAssignment = null;
        if (emitBehavior)
        {
            var softDeleteCol = entity.FirstColumnWithFlag(ColumnMetadata.SoftDeleteFlag);
            if (softDeleteCol is not null)
            {
                var prop = EntityNaming.PropertyName(softDeleteCol, corrections);
                softDeleteAssignment = softDeleteCol.ClrType switch
                {
                    ClrType.Boolean        => $"{prop} = true;",
                    ClrType.DateTime       => $"{prop} = System.DateTime.UtcNow;",
                    ClrType.DateTimeOffset => $"{prop} = System.DateTimeOffset.UtcNow;",
                    _ => null,
                };
            }
        }
        var emitSoftDelete = softDeleteAssignment is not null;

        var columns = visibleColumns
            .Select(c => new EntityColumnView(
                ClrTypeWithNullability: ClrTypeString(c),
                PropertyName: EntityNaming.PropertyName(c, corrections),
                Initializer: c.ClrType == ClrType.String && !c.IsNullable ? " = default!;" : string.Empty))
            .ToList();

        var referenceNavs = entity.ReferenceNavigations
            .Select(n => new EntityReferenceNavigationView(n.TargetEntityTypeName, n.PropertyName))
            .ToList();

        var collectionNavs = entity.CollectionNavigations
            .Select(n => new EntityCollectionNavigationView(
                TypeName: n.TargetEntityTypeName,
                PropertyName: n.PropertyName,
                BackingField: BackingFieldName(n.PropertyName)))
            .ToList();

        return new EntityCodeModel(
            HasUsingNamespaces: false,
            UsingNamespaces: Array.Empty<string>(),
            Namespace: $"{CleanLayout.DomainNamespace(ctx.Config.ProjectName)}.Entities",
            DomainCommonNamespace: commonNs,
            EntityName: entity.EntityTypeName,
            EmitBehavior: emitBehavior,
            SetterModifier: emitBehavior ? "private set" : "init",
            EmitUpdateMethod: emitBehavior && updateArgs.Count > 0,
            EmitSoftDelete: emitSoftDelete,
            SoftDeleteAssignment: softDeleteAssignment ?? string.Empty,
            Columns: columns,
            HasReferenceNavigations: referenceNavs.Count > 0,
            ReferenceNavigations: referenceNavs,
            HasCollectionNavigations: collectionNavs.Count > 0,
            CollectionNavigations: collectionNavs,
            CreateArgs: createArgs,
            Invariants: invariantLines.Select(l => new InvariantLine(l)).ToList(),
            UpdateArgs: updateArgsForTemplate,
            UpdateInvariants: updateInvariantLines.Select(l => new InvariantLine(l)).ToList());
    }

    static List<string> BuildInvariants(
        IReadOnlyList<Column> args,
        IReadOnlyDictionary<string, string> corrections,
        string commonNs)
    {
        var lines = new List<string>();
        foreach (var col in args)
        {
            var paramName = CasingHelper.ToCamelCase(col.Name, corrections);
            if (!col.IsNullable && col.ClrType == ClrType.String)
                lines.Add($"if (string.IsNullOrWhiteSpace({paramName})) errors.Add(new {commonNs}.DomainError(\"{paramName}\", \"required\", \"{col.Name} is required.\"));");
            if (col.ClrType == ClrType.String && col.MaxLength is int max && max > 0)
                lines.Add($"if ({paramName} is {{ Length: > {max} }}) errors.Add(new {commonNs}.DomainError(\"{paramName}\", \"maxLength\", \"{col.Name} must be at most {max} characters.\"));");
            if (!col.IsNullable && col.ClrType == ClrType.Guid)
                lines.Add($"if ({paramName} == System.Guid.Empty) errors.Add(new {commonNs}.DomainError(\"{paramName}\", \"required\", \"{col.Name} is required.\"));");
        }

        // Paired created-/updated-timestamp check: if both a "Created*" and "Updated*" column
        // are present in the args and both are date-typed, emit Updated >= Created.
        var createdCol = args.FirstOrDefault(c => IsDateLike(c) && IsCreatedTimestampName(c.Name));
        var updatedCol = args.FirstOrDefault(c => IsDateLike(c) && IsUpdatedTimestampName(c.Name));
        if (createdCol is not null && updatedCol is not null)
        {
            var createdParam = CasingHelper.ToCamelCase(createdCol.Name, corrections);
            var updatedParam = CasingHelper.ToCamelCase(updatedCol.Name, corrections);
            lines.Add($"if ({updatedParam} < {createdParam}) errors.Add(new {commonNs}.DomainError(\"{updatedParam}\", \"invalid_date\", \"{updatedCol.Name} cannot be before {createdCol.Name}.\"));");
        }
        return lines;
    }

    static string ClrTypeString(Column c)
    {
        var cs = SqlTypeMap.ToCs(c.ClrType);
        if (c.IsNullable && SqlTypeMap.IsValueType(c.ClrType)) return cs + "?";
        if (c.IsNullable && c.ClrType == ClrType.String) return cs + "?";
        return cs;
    }

    static bool IsDateLike(Column c) =>
        c.ClrType == ClrType.DateTime ||
        c.ClrType == ClrType.DateTimeOffset ||
        c.ClrType == ClrType.DateOnly;

    /// <summary>
    /// Builds the backing-field name for a collection navigation so that EF Core's
    /// backing-field convention discovery matches the property when
    /// <c>PropertyAccessMode.Field</c> is set. EF looks for <c>_&lt;camelCase&gt;</c>
    /// where camelCase = lowercase the first letter and preserve the rest verbatim.
    /// We must NOT route through <see cref="CasingHelper.ToCamelCase"/>
    /// because it splits on underscores and would lose them — disambiguated names
    /// like <c>BillingProfiles_ChargesBillToProfileId</c> would no longer match.
    /// </summary>
    static string BackingFieldName(string propertyName) =>
        propertyName.Length == 0
            ? "_"
            : "_" + char.ToLowerInvariant(propertyName[0]) + propertyName[1..];

    static bool IsCreatedTimestampName(string name) =>
        name.StartsWith("Created", StringComparison.OrdinalIgnoreCase);

    static bool IsUpdatedTimestampName(string name) =>
        name.StartsWith("Updated", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Modified", StringComparison.OrdinalIgnoreCase);
}
