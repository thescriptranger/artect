using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Artect.Config;

public static class YamlWriter
{
    public static void WriteFile(string path, ArtectConfig cfg) => File.WriteAllText(path, Write(cfg));

    public static string Write(ArtectConfig cfg)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"projectName: {cfg.ProjectName}");
        sb.AppendLine($"outputDirectory: {cfg.OutputDirectory}");
        sb.AppendLine($"targetFramework: {cfg.TargetFramework.ToMoniker()}");
        sb.AppendLine($"dataAccess: {cfg.DataAccess}");
        sb.AppendLine($"emitRepositoriesAndAbstractions: {Bool(cfg.EmitRepositoriesAndAbstractions)}");
        sb.AppendLine($"generatedByLabel: \"{cfg.GeneratedByLabel}\"");
        sb.AppendLine($"generateInitialMigration: {Bool(cfg.GenerateInitialMigration)}");
        sb.AppendLine($"crud: {CrudString(cfg.Crud)}");
        sb.AppendLine($"apiVersioning: {cfg.ApiVersioning}");
        sb.AppendLine($"auth: {cfg.Auth}");
        sb.AppendLine($"includeTestsProject: {Bool(cfg.IncludeTestsProject)}");
        sb.AppendLine($"includeDockerAssets: {Bool(cfg.IncludeDockerAssets)}");
        sb.AppendLine($"partitionStoredProceduresBySchema: {Bool(cfg.PartitionStoredProceduresBySchema)}");
        sb.AppendLine($"includeChildCollectionsInResponses: {Bool(cfg.IncludeChildCollectionsInResponses)}");
        sb.AppendLine($"validateForeignKeyReferences: {Bool(cfg.ValidateForeignKeyReferences)}");
        sb.AppendLine("schemas:");
        foreach (var s in cfg.Schemas) sb.AppendLine($"  - {s}");
        if (cfg.NamingCorrections.Count > 0)
        {
            sb.AppendLine("namingCorrections:");
            foreach (var kv in cfg.NamingCorrections.OrderBy(k => k.Key, StringComparer.Ordinal))
                sb.AppendLine($"  {kv.Key}: {kv.Value}");
        }
        if (cfg.TableClassifications.Count > 0)
        {
            sb.AppendLine("tableClassifications:");
            foreach (var kv in cfg.TableClassifications.OrderBy(k => k.Key, StringComparer.Ordinal))
                sb.AppendLine($"  {kv.Key}: {kv.Value}");
        }
        if (cfg.ColumnMetadata.Count > 0)
        {
            sb.AppendLine("columnMetadata:");
            foreach (var t in cfg.ColumnMetadata.OrderBy(k => k.Key, StringComparer.Ordinal))
                foreach (var c in t.Value.OrderBy(k => k.Key, StringComparer.Ordinal))
                    sb.AppendLine($"  {t.Key}.{c.Key}: {FlagsString(c.Value)}");
        }
        return sb.ToString();
    }

    static string FlagsString(ColumnMetadata flags)
    {
        if (flags == ColumnMetadata.None) return nameof(ColumnMetadata.None);
        var parts = new List<string>();
        foreach (ColumnMetadata v in Enum.GetValues(typeof(ColumnMetadata)))
        {
            if (v == ColumnMetadata.None) continue;
            if (flags.HasFlag(v)) parts.Add(v.ToString());
        }
        return string.Join(", ", parts);
    }

    static string Bool(bool b) => b ? "true" : "false";

    static string CrudString(CrudOperation ops)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (CrudOperation v in Enum.GetValues(typeof(CrudOperation)))
        {
            if (v is CrudOperation.None or CrudOperation.All) continue;
            if (ops.HasFlag(v)) parts.Add(v.ToString());
        }
        return "[" + string.Join(", ", parts) + "]";
    }
}
