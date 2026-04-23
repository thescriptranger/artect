namespace Artect.Console;

public static class Ansi
{
    public const string Reset  = "[0m";
    public const string Bold   = "[1m";
    public const string Dim    = "[2m";
    public const string Cyan   = "[36m";
    public const string Green  = "[32m";
    public const string Yellow = "[33m";
    public const string Red    = "[31m";

    public static string Colour(string text, string colour) => colour + text + Reset;
}
