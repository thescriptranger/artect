using System;

namespace Artect.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            System.Console.WriteLine("artect — scaffolding CLI for Clean Architecture + Minimal API solutions.");
            System.Console.WriteLine("Usage:");
            System.Console.WriteLine("  artect new [--config <path>] [--connection <string>] [flags...]");
            System.Console.WriteLine("  artect generate-yaml --connection <string> [--output <path>] [--force]");
            return 0;
        }
        var command = args[0];
        var cli = CliArguments.Parse(args);
        return command switch
        {
            "new" => new NewCommand().Run(cli),
            "generate-yaml" => new GenerateYamlCommand().Run(cli),
            _ => Unknown(command),
        };
    }

    static int Unknown(string command)
    {
        System.Console.Error.WriteLine($"Unknown command '{command}'. Expected 'new' or 'generate-yaml'.");
        return 2;
    }
}
