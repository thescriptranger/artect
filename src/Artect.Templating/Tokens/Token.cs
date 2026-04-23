namespace Artect.Templating.Tokens;

public sealed record Token(TokenKind Kind, string Text, int Line)
{
    public static readonly Token Eof = new(TokenKind.Eof, string.Empty, 0);
}
