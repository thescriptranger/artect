using System;
using System.IO;
using Artect.Config;
using Artect.Console;
using Artect.Generation;
using Artect.Introspection;

namespace Artect.Cli;

public sealed class NewCommand
{
    public int Run(CliArguments args)
    {
        var configPath = args.Get("config");
        ArtectConfig config;
        string? yamlConnection = null;

        if (configPath is not null)
        {
            config = YamlReader.ReadFile(configPath);
            yamlConnection = TryReadConnectionFromYaml(configPath);
        }
        else
        {
            var io = new ConsoleIO();
            var defaults = ArtectConfig.Defaults();
            var withFlags = ConfigOverrides.Apply(args, defaults);
            if (HasAllRequired(withFlags, args))
            {
                config = withFlags;
            }
            else
            {
                var connection = ConnectionResolver.Resolve(args, yamlConnection);
                var factory = new SqlConnectionFactory(connection);
                var probe = new SchemaProbe(factory);
                var available = probe.ListSchemas();
                var wizard = new WizardRunner(io);
                config = wizard.Run(available);
                config = ConfigOverrides.Apply(args, config);
            }
        }

        var connection2 = ConnectionResolver.Resolve(args, yamlConnection);
        var reader = new SqlServerSchemaReader(new SqlConnectionFactory(connection2));
        var graph = reader.Read(config.Schemas);
        var generator = new Generator(EmitterRegistry.All());
        var outputRoot = Path.GetFullPath(config.OutputDirectory);
        Directory.CreateDirectory(outputRoot);
        generator.Generate(config, graph, outputRoot, connection2);
        System.Console.WriteLine($"Generated scaffold at {outputRoot}");
        return 0;
    }

    static bool HasAllRequired(ArtectConfig cfg, CliArguments args) =>
        args.Has("name") && args.Has("output"); // minimal heuristic — expand as needed

    static string? TryReadConnectionFromYaml(string path)
    {
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.TrimStart();
            if (line.StartsWith("connectionString:", StringComparison.Ordinal))
            {
                var value = line.Substring("connectionString:".Length).Trim();
                if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                    value = value.Substring(1, value.Length - 2);
                return value;
            }
        }
        return null;
    }
}
