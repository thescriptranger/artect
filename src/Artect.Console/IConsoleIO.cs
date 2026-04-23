namespace Artect.Console;

public interface IConsoleIO
{
    void Write(string text);
    void WriteLine(string text);
    string ReadLine();
}
