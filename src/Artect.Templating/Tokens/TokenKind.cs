namespace Artect.Templating.Tokens;

public enum TokenKind
{
    Text, Variable,
    IfStart, ElseIf, Else, IfEnd,
    ForStart, ForEnd,
    Eof
}
