using System.Collections.Generic;
using System.Text;

namespace Artect.Templating.Tokens;

public static class Tokenizer
{
    public static IReadOnlyList<Token> Tokenize(string source)
    {
        var tokens = new List<Token>();
        var sb = new StringBuilder();
        int i = 0, line = 1;
        while (i < source.Length)
        {
            if (i + 1 < source.Length && source[i] == '{' && source[i + 1] == '{')
            {
                if (sb.Length > 0) { tokens.Add(new Token(TokenKind.Text, sb.ToString(), line)); sb.Clear(); }
                int end = source.IndexOf("}}", i + 2, System.StringComparison.Ordinal);
                if (end < 0) throw new TemplateException($"Unclosed tag at line {line}");
                var body = source.Substring(i + 2, end - (i + 2)).Trim();
                tokens.Add(Classify(body, line));
                i = end + 2;
                continue;
            }
            if (source[i] == '\n') line++;
            sb.Append(source[i]);
            i++;
        }
        if (sb.Length > 0) tokens.Add(new Token(TokenKind.Text, sb.ToString(), line));
        tokens.Add(Token.Eof);
        return tokens;
    }

    static Token Classify(string body, int line)
    {
        if (body.StartsWith("# if ")) return new Token(TokenKind.IfStart, body[5..].Trim(), line);
        if (body.StartsWith("# elseif ")) return new Token(TokenKind.ElseIf, body[9..].Trim(), line);
        if (body == "else" || body == "# else") return new Token(TokenKind.Else, string.Empty, line);
        if (body == "/if") return new Token(TokenKind.IfEnd, string.Empty, line);
        if (body.StartsWith("# for ")) return new Token(TokenKind.ForStart, body[6..].Trim(), line);
        if (body == "/for") return new Token(TokenKind.ForEnd, string.Empty, line);
        return new Token(TokenKind.Variable, body, line);
    }
}
