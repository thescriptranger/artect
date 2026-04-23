using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Artect.Core.Schema;
using Artect.Naming;
using Artect.Templating;

namespace Artect.Generation.Emitters;

public sealed class ValidatorEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var template = TemplateParser.Parse(ctx.Templates.Load("Validator.cs.artect"));
        var list = new List<EmittedFile>();

        var commandsNs = CleanLayout.ApplicationCommandsNamespace(ctx.Config.ProjectName);
        var validatorNs = $"{CleanLayout.ApplicationNamespace(ctx.Config.ProjectName)}.Validators";

        foreach (var entity in ctx.Model.Entities)
        {
            if (entity.IsJoinTable) continue;
            if (!entity.HasPrimaryKey) continue;

            var pkCols = entity.Table.PrimaryKey!.ColumnNames
                .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

            var createBodyLines = BuildBodyLines(entity, pkCols, isCreate: true, ctx.Config.ValidateForeignKeyReferences);
            var updateBodyLines = BuildBodyLines(entity, pkCols, isCreate: false, ctx.Config.ValidateForeignKeyReferences);

            var data = new
            {
                CommandsNamespace = commandsNs,
                Namespace         = validatorNs,
                EntityName        = entity.EntityTypeName,
                CreateBodyLines   = createBodyLines,
                UpdateBodyLines   = updateBodyLines,
            };

            var rendered = Renderer.Render(template, data);
            // Two classes in one file — use the entity name as the class name prefix
            var path = CleanLayout.ValidatorPath(ctx.Config.ProjectName, $"{entity.EntityTypeName}CommandValidators");
            list.Add(new EmittedFile(path, rendered));
        }

        return list;
    }

    // ------------------------------------------------------------------ body builders

    static IReadOnlyList<object> BuildBodyLines(
        NamedEntity entity,
        HashSet<string> pkCols,
        bool isCreate,
        bool validateForeignKeyRefs)
    {
        var lines = new List<object>();

        void Add(string text) => lines.Add(new { Text = text });

        foreach (var c in entity.Table.Columns)
        {
            // For Create requests, skip server-generated / identity PK columns entirely.
            if (isCreate && pkCols.Contains(c.Name) && c.IsServerGenerated) continue;

            var prop = EntityNaming.PropertyName(c);

            if (c.ClrType == ClrType.String && !c.IsNullable)
            {
                Add($"if (string.IsNullOrWhiteSpace(request.{prop}))");
                Add($"    errors.Add(new ValidationError(\"{prop}\", \"required\", \"{prop} is required.\"));");
            }

            if (c.ClrType == ClrType.String && c.MaxLength is > 0)
            {
                var max = c.MaxLength!.Value;
                Add($"if (request.{prop} is {{ Length: > {max} }})");
                Add($"    errors.Add(new ValidationError(\"{prop}\", \"maxLength\", \"{prop} must be at most {max} characters.\"));");
            }
        }

        // CHECK constraints — per-table (not per-column).
        if (entity.Table.CheckConstraints.Count > 0)
        {
            var propByCol = entity.Table.Columns
                .ToDictionary(c => c.Name, c => EntityNaming.PropertyName(c), System.StringComparer.OrdinalIgnoreCase);

            foreach (var cc in entity.Table.CheckConstraints)
            {
                EmitCheckConstraint(lines, propByCol, cc.Name, cc.Expression);
            }
        }

        // FK reference guards (optional feature flag).
        if (validateForeignKeyRefs)
        {
            var pkColSet = new HashSet<string>(pkCols, System.StringComparer.OrdinalIgnoreCase);
            foreach (var fk in entity.Table.ForeignKeys)
            {
                foreach (var pair in fk.ColumnPairs)
                {
                    // FK col must be non-nullable (required) — check via column definition.
                    var col = entity.Table.Columns.FirstOrDefault(c =>
                        string.Equals(c.Name, pair.FromColumn, System.StringComparison.OrdinalIgnoreCase));
                    if (col is null || col.IsNullable) continue;

                    // Skip PK columns that are also FKs (composite PKs / join tables already filtered).
                    if (isCreate && pkColSet.Contains(pair.FromColumn)) continue;

                    var prop = EntityNaming.PropertyName(col);
                    Add($"// TODO: wire existence query for {prop} referencing {fk.ToTable}.{pair.ToColumn}");
                    Add($"if (request.{prop} == default)");
                    Add($"    errors.Add(new ValidationError(\"{prop}\", \"foreignKey\", \"{prop} must reference an existing {fk.ToTable}.\"));");
                }
            }
        }

        return lines;
    }

    // ------------------------------------------------------------------ check constraint translation

    // Bare or bracket-quoted identifier.
    private const string Identifier = @"\[?(?<col>[A-Za-z_][A-Za-z0-9_]*)\]?";
    // Signed integer, optionally wrapped in extra parens.
    private const string IntLiteral = @"\(*\s*(?<val>-?\d+)\s*\)*";

    private static readonly Regex ComparisonPattern = new(
        @"^\s*\(?\s*" + Identifier + @"\s*(?<op>>=|<=|>|<)\s*" + IntLiteral + @"\s*\)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex BetweenPattern = new(
        @"^\s*\(?\s*" + Identifier + @"\s+between\s+\(*\s*(?<lo>-?\d+)\s*\)*\s+and\s+\(*\s*(?<hi>-?\d+)\s*\)*\s*\)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static void EmitCheckConstraint(
        List<object> lines,
        Dictionary<string, string> propByCol,
        string ckName,
        string expression)
    {
        void Add(string text) => lines.Add(new { Text = text });

        // Normalise newlines for the comment so it never wraps.
        var exprForComment = expression.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');

        var m = ComparisonPattern.Match(expression);
        if (m.Success && long.TryParse(m.Groups["val"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cmpVal))
        {
            var col  = m.Groups["col"].Value;
            var op   = m.Groups["op"].Value;
            var prop = PropFor(propByCol, col);
            // Negate the constraint — violation fires the error.
            var errorOp = op switch
            {
                ">=" => "<",
                ">"  => "<=",
                "<=" => ">",
                "<"  => ">=",
                _    => null,
            };
            if (errorOp is null)
            {
                Add($"// TODO: translate check constraint '{ckName}': {exprForComment}");
                return;
            }
            var lit = cmpVal.ToString(CultureInfo.InvariantCulture);
            Add($"// From SQL check constraint '{ckName}': {exprForComment}");
            Add($"if (request.{prop} {errorOp} {lit})");
            Add($"    errors.Add(new ValidationError(\"{prop}\", \"range\", \"{prop} must be {op} {lit}.\"));");
            return;
        }

        var mb = BetweenPattern.Match(expression);
        if (mb.Success &&
            long.TryParse(mb.Groups["lo"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lo) &&
            long.TryParse(mb.Groups["hi"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hi))
        {
            var col  = mb.Groups["col"].Value;
            var prop = PropFor(propByCol, col);
            var loS  = lo.ToString(CultureInfo.InvariantCulture);
            var hiS  = hi.ToString(CultureInfo.InvariantCulture);
            Add($"// From SQL check constraint '{ckName}': {exprForComment}");
            Add($"if (request.{prop} < {loS} || request.{prop} > {hiS})");
            Add($"    errors.Add(new ValidationError(\"{prop}\", \"range\", \"{prop} must be between {loS} and {hiS}.\"));");
            return;
        }

        // Untranslatable — emit a TODO comment; never throw.
        Add($"// TODO: translate check constraint '{ckName}': {exprForComment}");
    }

    static string PropFor(Dictionary<string, string> propByCol, string col)
        => propByCol.TryGetValue(col, out var name) ? name : CasingHelper.ToPascalCase(col);
}
