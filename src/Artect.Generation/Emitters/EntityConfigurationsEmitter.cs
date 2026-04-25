using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits one IEntityTypeConfiguration&lt;T&gt; class per entity into
/// Infrastructure/Data/Configurations/. Replaces the per-entity blocks that used to
/// live inside DbContext.OnModelCreating. The DbContext now calls
/// ApplyConfigurationsFromAssembly to pick these up.
/// </summary>
public sealed class EntityConfigurationsEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        if (ctx.Config.DataAccess != DataAccessKind.EfCore)
            return System.Array.Empty<EmittedFile>();

        var project = ctx.Config.ProjectName;
        var entityNs = $"{CleanLayout.DomainNamespace(project)}.Entities";
        var configNs = CleanLayout.InfrastructureConfigurationsNamespace(project);

        // Detect cross-schema entity name collisions to use fully-qualified type refs.
        var collidedEntityNames = ctx.Model.DbSets.EntityTypeNames.Values
            .GroupBy(v => v, System.StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(System.StringComparer.Ordinal);

        var list = new List<EmittedFile>();
        foreach (var entity in ctx.Model.Entities)
        {
            var typeName = entity.EntityTypeName;
            var typeRef = collidedEntityNames.Contains(typeName)
                ? $"{entityNs}.{typeName}"
                : typeName;

            var sb = new StringBuilder();
            sb.AppendLine("using Microsoft.EntityFrameworkCore;");
            sb.AppendLine("using Microsoft.EntityFrameworkCore.Metadata.Builders;");
            sb.AppendLine($"using {entityNs};");
            sb.AppendLine();
            sb.AppendLine($"namespace {configNs};");
            sb.AppendLine();
            sb.AppendLine($"public sealed class {typeName}Configuration : IEntityTypeConfiguration<{typeRef}>");
            sb.AppendLine("{");
            sb.AppendLine($"    public void Configure(EntityTypeBuilder<{typeRef}> builder)");
            sb.AppendLine("    {");
            EmitConfigureBody(sb, entity, ctx.Model, ctx.NamingCorrections);
            sb.AppendLine("    }");
            sb.AppendLine("}");

            list.Add(new EmittedFile(
                CleanLayout.InfrastructureConfigurationsPath(project, $"{typeName}Configuration"),
                sb.ToString()));
        }
        return list;
    }

    static void EmitConfigureBody(
        StringBuilder sb,
        NamedEntity entity,
        NamedSchemaModel model,
        IReadOnlyDictionary<string, string> corrections)
    {
        var table = entity.Table;

        sb.AppendLine($"        builder.ToTable(\"{table.Name}\", \"{table.Schema}\");");

        // Primary key
        if (table.PrimaryKey is { } pk)
        {
            if (pk.ColumnNames.Count == 1)
            {
                var pkProp = EntityNaming.PropertyName(
                    table.Columns.First(c => string.Equals(c.Name, pk.ColumnNames[0], System.StringComparison.OrdinalIgnoreCase)),
                    corrections);
                sb.AppendLine($"        builder.HasKey(e => e.{pkProp});");

                var pkCol = table.Columns.First(c =>
                    string.Equals(c.Name, pk.ColumnNames[0], System.StringComparison.OrdinalIgnoreCase));
                if (pkCol.IsServerGenerated)
                    sb.AppendLine($"        builder.Property(e => e.{pkProp}).ValueGeneratedOnAdd();");
            }
            else
            {
                var members = string.Join(", ", pk.ColumnNames.Select(n =>
                    "e." + EntityNaming.PropertyName(
                        table.Columns.First(c => string.Equals(c.Name, n, System.StringComparison.OrdinalIgnoreCase)),
                        corrections)));
                sb.AppendLine($"        builder.HasKey(e => new {{ {members} }});");
            }
        }
        else if (entity.IsJoinTable && entity.ReferenceNavigations.Count > 0)
        {
            var members = string.Join(", ", entity.ReferenceNavigations.Select(n =>
                "e." + EntityNaming.PropertyName(
                    table.Columns.First(c => string.Equals(c.Name, n.ColumnPairs[0].FromColumn, System.StringComparison.OrdinalIgnoreCase)),
                    corrections)));
            sb.AppendLine($"        builder.HasKey(e => new {{ {members} }});");
        }
        else
        {
            sb.AppendLine("        builder.HasNoKey();");
        }

        // Column mappings
        foreach (var col in table.Columns)
        {
            var prop = EntityNaming.PropertyName(col, corrections);
            sb.AppendLine($"        builder.Property(e => e.{prop}).HasColumnName(\"{col.Name}\");");
        }

        // Reference navigations / FK relationships
        foreach (var nav in entity.ReferenceNavigations)
        {
            var fkProp = EntityNaming.PropertyName(
                table.Columns.First(c => string.Equals(c.Name, nav.ColumnPairs[0].FromColumn, System.StringComparison.OrdinalIgnoreCase)),
                corrections);
            var fkCol = table.Columns.First(c =>
                string.Equals(c.Name, nav.ColumnPairs[0].FromColumn, System.StringComparison.OrdinalIgnoreCase));
            var isRequired = !fkCol.IsNullable;

            var collectionNav = model.Entities
                .FirstOrDefault(e => string.Equals(e.EntityTypeName, nav.TargetEntityTypeName, System.StringComparison.Ordinal))
                ?.CollectionNavigations
                .FirstOrDefault(cn => string.Equals(cn.SourceForeignKeyName, nav.SourceForeignKeyName, System.StringComparison.Ordinal));

            var withMany = collectionNav is not null
                ? $".WithMany(x => x.{collectionNav.PropertyName})"
                : ".WithMany()";

            sb.AppendLine($"        builder.HasOne(e => e.{nav.PropertyName})");
            sb.AppendLine($"            {withMany}");
            sb.AppendLine($"            .HasForeignKey(e => e.{fkProp})");
            sb.AppendLine($"            .IsRequired({isRequired.ToString().ToLowerInvariant()});");
        }

        // PropertyAccessMode.Field for navigations (read/write through backing field).
        // Required by Phase 1 collection-encapsulation change: collection navs are now
        // private-backed IReadOnlyCollection.
        foreach (var nav in entity.ReferenceNavigations)
            sb.AppendLine($"        builder.Navigation(e => e.{nav.PropertyName}).UsePropertyAccessMode(PropertyAccessMode.Field);");
        foreach (var nav in entity.CollectionNavigations)
            sb.AppendLine($"        builder.Navigation(e => e.{nav.PropertyName}).UsePropertyAccessMode(PropertyAccessMode.Field);");

        // Unique constraints (alternate keys)
        foreach (var uq in table.UniqueConstraints)
        {
            var members = string.Join(", ", uq.ColumnNames.Select(col =>
                "e." + PropFor(table, col, corrections)));
            sb.AppendLine($"        builder.HasAlternateKey(e => new {{ {members} }}).HasName(\"{uq.Name}\");");
        }

        // Non-unique indexes
        foreach (var ix in table.Indexes)
        {
            var members = string.Join(", ", ix.KeyColumns.Select(col =>
                "e." + PropFor(table, col, corrections)));
            var uniqueSuffix = ix.IsUnique ? ".IsUnique()" : string.Empty;
            sb.AppendLine($"        builder.HasIndex(e => new {{ {members} }}).HasDatabaseName(\"{ix.Name}\"){uniqueSuffix};");
        }

        // Check constraints
        foreach (var ck in table.CheckConstraints)
        {
            var escaped = "@\"" + ck.Expression.Replace("\"", "\"\"") + "\"";
            sb.AppendLine($"        builder.ToTable(tb => tb.HasCheckConstraint(\"{ck.Name}\", {escaped}));");
        }
    }

    static string PropFor(Table table, string colName, IReadOnlyDictionary<string, string> corrections)
    {
        var col = table.Columns.FirstOrDefault(c =>
            string.Equals(c.Name, colName, System.StringComparison.OrdinalIgnoreCase));
        return col is not null
            ? EntityNaming.PropertyName(col, corrections)
            : CasingHelper.ToPascalCase(colName, corrections);
    }
}
