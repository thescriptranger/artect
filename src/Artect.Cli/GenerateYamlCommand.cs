using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Artect.Config;
using Artect.Introspection;
using Artect.Naming;

namespace Artect.Cli;

public sealed class GenerateYamlCommand
{
    public int Run(CliArguments args)
    {
        var output = args.Get("output") ?? "artect.yaml";
        var force = args.Has("force");
        if (File.Exists(output) && !force)
        {
            System.Console.Error.WriteLine(
                $"'{output}' already exists. Pass --force to overwrite.");
            return 4;
        }

        var connection = ConnectionResolver.Resolve(args, fromConfigYaml: null);
        var probe = new SchemaProbe(new SqlConnectionFactory(connection));
        var schemas = probe.ListSchemas();
        var reader = new SqlServerSchemaReader(new SqlConnectionFactory(connection));
        var graph = reader.Read(schemas);

        var classifications = new SortedDictionary<string, EntityClassification>(StringComparer.Ordinal);
        foreach (var t in graph.Tables)
        {
            // Cross-schema collisions: last write wins. Same convention as NamingCorrections.
            classifications[t.Name] = EntityClassifier.Heuristic(t);
        }

        var defaults = ArtectConfig.Defaults() with
        {
            ProjectName = args.Get("name") ?? "MyApi",
            OutputDirectory = args.Get("output-dir") ?? "./MyApi",
            Schemas = schemas,
            TableClassifications = classifications,
        };

        var yaml = YamlWriter.Write(defaults) + ColumnMetadataHint();
        File.WriteAllText(output, yaml);
        System.Console.WriteLine($"Wrote {output} with {classifications.Count} table classification(s).");
        System.Console.WriteLine("Edit the file to override classifications or add columnMetadata, then run:");
        System.Console.WriteLine($"  artect new --config {output} --connection \"...\"");
        return 0;
    }

    static string ColumnMetadataHint() =>
        "# columnMetadata: optional per-column flags. Format: 'Table.Column: Flag1, Flag2'." + Environment.NewLine +
        "# Allowed flags: Ignored, ProtectedFromUpdate, ConcurrencyToken, Audit, Sensitive." + Environment.NewLine +
        "# Example:" + Environment.NewLine +
        "# columnMetadata:" + Environment.NewLine +
        "#   Customer.UpdatedAtUtc: ProtectedFromUpdate" + Environment.NewLine +
        "#   Customer.RowVersion: ConcurrencyToken" + Environment.NewLine +
        "#   Customer.Email: Sensitive" + Environment.NewLine;
}
