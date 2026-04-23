namespace Artect.Console;

public sealed class ConsoleIO : IConsoleIO
{
    public void Write(string text) => System.Console.Write(text);
    public void WriteLine(string text) => System.Console.WriteLine(text);
    public string ReadLine() => System.Console.ReadLine() ?? string.Empty;
}
