using System.Collections.Generic;
using System.Linq;
using System.Text;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;
// Artect.Core.Schema.Index collides with System.Index — alias to the schema type.
using Index = Artect.Core.Schema.Index;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits a single <c>&lt;Project&gt;DbContext</c> class in
/// <c>&lt;Project&gt;.Infrastructure.Data</c>.
/// Only runs when <c>cfg.DataAccess == EfCore</c>.
/// </summary>
public sealed class DbContextEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        if (ctx.Config.DataAccess != DataAccessKind.EfCore)
            return System.Array.Empty<EmittedFile>();

        var project = ctx.Config.ProjectName;
        var content = Build(ctx, project);
        var path    = CleanLayout.DbContextPath(project, $"{project}DbContext");
        return new[] { new EmittedFile(path, content) };
    }

    static string Build(EmitterContext ctx, string project)
    {
        var model     = ctx.Model;
        var graph     = ctx.Graph;
        var dbCtx     = $"{project}DbContext";
        var entityNs  = $"{CleanLayout.DomainNamespace(project)}.Entities";
        var infraNs   = $"{CleanLayout.InfrastructureNamespace(project)}.Data";

        // Detect cross-schema entity name collisions (same entity name from different schemas).
        // For collided names the DbSet property uses the prefixed name stored in DbSetNaming.
        var collidedEntityNames = model.DbSets.EntityTypeNames.Values
            .GroupBy(v => v, System.StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(System.StringComparer.Ordinal);

        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        sb.AppendLine($"using {entityNs};");
        sb.AppendLine();
        sb.AppendLine($"namespace {infraNs};");
        sb.AppendLine();
        sb.AppendLine($"public sealed class {dbCtx} : DbContext");
        sb.AppendLine("{");
        sb.AppendLine($"    public {dbCtx}(DbContextOptions<{dbCtx}> options) : base(options) {{ }}");
        sb.AppendLine();

        // ── DbSet properties ──────────────────────────────────────────────
        foreach (var entity in model.Entities)
        {
            var propertyName = entity.DbSetPropertyName;    // already collision-resolved
            var typeName     = entity.EntityTypeName;       // already collision-resolved

            // For collided entities, use fully-qualified type to avoid CS0104.
            var typeRef = collidedEntityNames.Contains(typeName)
                ? $"{entityNs}.{typeName}"
                : typeName;

            sb.AppendLine($"    public DbSet<{typeRef}> {propertyName} {{ get; set; }} = default!;");
        }

        sb.AppendLine();
        sb.AppendLine("    protected override void OnModelCreating(ModelBuilder modelBuilder)");
        sb.AppendLine("    {");

        // ── Sequences ─────────────────────────────────────────────────────
        var orderedSequences = graph.Sequences
            .OrderBy(s => s.Schema, System.StringComparer.Ordinal)
            .ThenBy(s => s.Name, System.StringComparer.Ordinal);

        foreach (var seq in orderedSequences)
        {
            var clrType = SequenceClrType(seq.SqlType);
            sb.AppendLine($"        modelBuilder.HasSequence<{clrType}>(\"{seq.Name}\", \"{seq.Schema}\")");
            sb.AppendLine($"            .StartsAt({seq.StartValue})");
            sb.AppendLine($"            .IncrementsBy({seq.Increment});");
        }

        // ── Entity configurations ─────────────────────────────────────────
        foreach (var entity in model.Entities)
        {
            var typeName = entity.EntityTypeName;
            var typeRef  = collidedEntityNames.Contains(typeName)
                ? $"{entityNs}.{typeName}"
                : typeName;

            EmitEntityConfig(sb, entity, typeRef, model, entityNs, collidedEntityNames);
        }

        // ── View configurations ───────────────────────────────────────────
        foreach (var view in graph.Views)
        {
            var viewTypeName = CasingHelper.ToPascalCase(Pluralizer.Singularize(view.Name));
            sb.AppendLine($"        modelBuilder.Entity<{viewTypeName}>(b =>");
            sb.AppendLine("        {");
            sb.AppendLine($"            b.ToView(\"{view.Name}\", \"{view.Schema}\");");
            sb.AppendLine("            b.HasNoKey();");
            foreach (var c in view.Columns)
            {
                var prop = EntityNaming.PropertyName(c);
                sb.AppendLine($"            b.Property(e => e.{prop}).HasColumnName(\"{c.Name}\");");
            }
            sb.AppendLine("        });");
            sb.AppendLine();
        }

        sb.AppendLine("        base.OnModelCreating(modelBuilder);");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static void EmitEntityConfig(
        StringBuilder sb,
        NamedEntity entity,
        string typeRef,
        NamedSchemaModel model,
        string entityNs,
        HashSet<string> collidedEntityNames)
    {
        var table  = entity.Table;

        sb.AppendLine($"        modelBuilder.Entity<{typeRef}>(b =>");
        sb.AppendLine("        {");
        sb.AppendLine($"            b.ToTable(\"{table.Name}\", \"{table.Schema}\");");

        // Primary key
        if (table.PrimaryKey is { } pk)
        {
            if (pk.ColumnNames.Count == 1)
            {
                var pkProp = EntityNaming.PropertyName(
                    table.Columns.First(c =>
                        string.Equals(c.Name, pk.ColumnNames[0], System.StringComparison.OrdinalIgnoreCase)));
                sb.AppendLine($"            b.HasKey(e => e.{pkProp});");

                // Server-generated PK → ValueGeneratedOnAdd
                var pkCol = table.Columns.First(c =>
                    string.Equals(c.Name, pk.ColumnNames[0], System.StringComparison.OrdinalIgnoreCase));
                if (pkCol.IsServerGenerated)
                    sb.AppendLine($"            b.Property(e => e.{pkProp}).ValueGeneratedOnAdd();");
            }
            else
            {
                // Composite PK
                var members = string.Join(", ", pk.ColumnNames.Select(n =>
                    "e." + EntityNaming.PropertyName(
                        table.Columns.First(c =>
                            string.Equals(c.Name, n, System.StringComparison.OrdinalIgnoreCase)))));
                sb.AppendLine($"            b.HasKey(e => new {{ {members} }});");
            }
        }
        else if (entity.IsJoinTable && entity.ReferenceNavigations.Count > 0)
        {
            // Join table without declared PK — use FK columns as composite key
            var members = string.Join(", ", entity.ReferenceNavigations.Select(n =>
                "e." + EntityNaming.PropertyName(
                    table.Columns.First(c =>
                        string.Equals(c.Name, n.ColumnPairs[0].FromColumn, System.StringComparison.OrdinalIgnoreCase)))));
            sb.AppendLine($"            b.HasKey(e => new {{ {members} }});");
        }
        else
        {
            sb.AppendLine("            b.HasNoKey();");
        }

        // Column mappings
        foreach (var col in table.Columns)
        {
            var prop = EntityNaming.PropertyName(col);
            sb.AppendLine($"            b.Property(e => e.{prop}).HasColumnName(\"{col.Name}\");");
        }

        // Navigation / FK relationships
        foreach (var nav in entity.ReferenceNavigations)
        {
            // Find corresponding collection nav on the target entity
            var targetEntity = model.Entities.FirstOrDefault(e =>
                e.Table.ForeignKeys.Any(fk => string.Equals(fk.Name, nav.SourceForeignKeyName, System.StringComparison.OrdinalIgnoreCase)) == false
                && string.Equals(e.EntityTypeName,
                    model.DbSets.EntityTypeNames.TryGetValue(
                        (nav.ColumnPairs[0].ToColumn, nav.PropertyName), out _)
                        ? nav.TargetEntityTypeName : nav.TargetEntityTypeName,
                    System.StringComparison.Ordinal));

            // Find the FK column property name
            var fkProp = EntityNaming.PropertyName(
                table.Columns.First(c =>
                    string.Equals(c.Name, nav.ColumnPairs[0].FromColumn, System.StringComparison.OrdinalIgnoreCase)));

            // Determine whether the FK column is nullable → optional relationship
            var fkCol = table.Columns.First(c =>
                string.Equals(c.Name, nav.ColumnPairs[0].FromColumn, System.StringComparison.OrdinalIgnoreCase));
            var isRequired = !fkCol.IsNullable;

            // Find matching collection nav on the target entity
            var collectionNav = model.Entities
                .FirstOrDefault(e => string.Equals(e.EntityTypeName, nav.TargetEntityTypeName, System.StringComparison.Ordinal))
                ?.CollectionNavigations
                .FirstOrDefault(cn => string.Equals(cn.SourceForeignKeyName, nav.SourceForeignKeyName, System.StringComparison.Ordinal));

            var withMany = collectionNav is not null
                ? $".WithMany(x => x.{collectionNav.PropertyName})"
                : ".WithMany()";

            sb.AppendLine($"            b.HasOne(e => e.{nav.PropertyName})");
            sb.AppendLine($"                {withMany}");
            sb.AppendLine($"                .HasForeignKey(e => e.{fkProp})");
            sb.AppendLine($"                .IsRequired({isRequired.ToString().ToLowerInvariant()});");
        }

        // PropertyAccessMode.Field for all navigations (reference + collection)
        foreach (var nav in entity.ReferenceNavigations)
        {
            sb.AppendLine($"            b.Navigation(e => e.{nav.PropertyName}).UsePropertyAccessMode(PropertyAccessMode.Field);");
        }
        foreach (var nav in entity.CollectionNavigations)
        {
            sb.AppendLine($"            b.Navigation(e => e.{nav.PropertyName}).UsePropertyAccessMode(PropertyAccessMode.Field);");
        }

        // Unique constraints (alternate keys)
        foreach (var uq in table.UniqueConstraints)
        {
            var members = string.Join(", ", uq.ColumnNames.Select(col =>
                "e." + PropFor(table, col)));
            sb.AppendLine($"            b.HasAlternateKey(e => new {{ {members} }}).HasName(\"{uq.Name}\");");
        }

        // Non-unique indexes
        foreach (var ix in table.Indexes)
        {
            var members = string.Join(", ", ix.KeyColumns.Select(col =>
                "e." + PropFor(table, col)));
            var uniqueSuffix = ix.IsUnique ? ".IsUnique()" : string.Empty;
            sb.AppendLine($"            b.HasIndex(e => new {{ {members} }}).HasDatabaseName(\"{ix.Name}\"){uniqueSuffix};");
        }

        // Check constraints
        foreach (var ck in table.CheckConstraints)
        {
            var escaped = "@\"" + ck.Expression.Replace("\"", "\"\"") + "\"";
            sb.AppendLine($"            b.ToTable(tb => tb.HasCheckConstraint(\"{ck.Name}\", {escaped}));");
        }

        sb.AppendLine("        });");
        sb.AppendLine();
    }

    static string PropFor(Table table, string colName)
    {
        var col = table.Columns.FirstOrDefault(c =>
            string.Equals(c.Name, colName, System.StringComparison.OrdinalIgnoreCase));
        return col is not null
            ? EntityNaming.PropertyName(col)
            : CasingHelper.ToPascalCase(colName);
    }

    static string SequenceClrType(string sqlType) => sqlType.ToLowerInvariant() switch
    {
        "bigint"   => "long",
        "int"      => "int",
        "smallint" => "short",
        "tinyint"  => "byte",
        "decimal" or "numeric" => "decimal",
        _ => "long",
    };
}
