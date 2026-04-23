using System;

namespace Artect.Cli;

public static class ConnectionResolver
{
    public static string Resolve(CliArguments args, string? fromConfigYaml)
    {
        var flag = args.Get("connection");
        if (!string.IsNullOrEmpty(flag)) return flag;
        var env = Environment.GetEnvironmentVariable("ARTECT_CONNECTION");
        if (!string.IsNullOrEmpty(env)) return env;
        if (!string.IsNullOrEmpty(fromConfigYaml)) return fromConfigYaml!;
        throw new System.InvalidOperationException(
            "No connection string provided. Pass --connection, set ARTECT_CONNECTION, or add connectionString: to artect.yaml.");
    }
}
