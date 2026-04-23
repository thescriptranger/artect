using System.Collections.Generic;
using Artect.Templating.Ast;
using Artect.Templating.Tokens;

namespace Artect.Templating;

public static class TemplateParser
{
    public static TemplateDocument Parse(string source)
    {
        var tokens = Tokenizer.Tokenize(source);
        int idx = 0;
        var nodes = ParseNodes(tokens, ref idx, TokenKind.Eof);
        return new TemplateDocument(nodes);
    }

    static IReadOnlyList<TemplateNode> ParseNodes(IReadOnlyList<Token> tokens, ref int idx, params TokenKind[] terminators)
    {
        var nodes = new List<TemplateNode>();
        while (idx < tokens.Count)
        {
            var t = tokens[idx];
            foreach (var term in terminators) if (t.Kind == term) return nodes;
            switch (t.Kind)
            {
                case TokenKind.Text: nodes.Add(new TextNode(t.Text)); idx++; break;
                case TokenKind.Variable: nodes.Add(ParseVariable(t.Text)); idx++; break;
                case TokenKind.IfStart: nodes.Add(ParseIf(tokens, ref idx)); break;
                case TokenKind.ForStart: nodes.Add(ParseFor(tokens, ref idx)); break;
                default: throw new TemplateException($"Unexpected token '{t.Kind}' at line {t.Line}");
            }
        }
        return nodes;
    }

    static VariableNode ParseVariable(string body)
    {
        var parts = body.Split('|');
        var path = parts[0].Trim();
        var filters = new List<string>();
        for (int i = 1; i < parts.Length; i++) filters.Add(parts[i].Trim());
        return new VariableNode(path, filters);
    }

    static IfNode ParseIf(IReadOnlyList<Token> tokens, ref int idx)
    {
        var branches = new List<(string, IReadOnlyList<TemplateNode>)>();
        IReadOnlyList<TemplateNode>? elseBody = null;
        string condition = tokens[idx].Text; idx++;
        var body = ParseNodes(tokens, ref idx, TokenKind.ElseIf, TokenKind.Else, TokenKind.IfEnd);
        branches.Add((condition, body));
        while (idx < tokens.Count && tokens[idx].Kind == TokenKind.ElseIf)
        {
            condition = tokens[idx].Text; idx++;
            body = ParseNodes(tokens, ref idx, TokenKind.ElseIf, TokenKind.Else, TokenKind.IfEnd);
            branches.Add((condition, body));
        }
        if (idx < tokens.Count && tokens[idx].Kind == TokenKind.Else)
        {
            idx++;
            elseBody = ParseNodes(tokens, ref idx, TokenKind.IfEnd);
        }
        if (idx >= tokens.Count || tokens[idx].Kind != TokenKind.IfEnd)
            throw new TemplateException("Unterminated if-block");
        idx++;
        return new IfNode(branches, elseBody);
    }

    static ForNode ParseFor(IReadOnlyList<Token> tokens, ref int idx)
    {
        var body = tokens[idx].Text; idx++;
        var parts = body.Split(new[] { " in " }, 2, System.StringSplitOptions.None);
        if (parts.Length != 2) throw new TemplateException($"Invalid for-loop syntax: '{body}'");
        var nodes = ParseNodes(tokens, ref idx, TokenKind.ForEnd);
        if (idx >= tokens.Count || tokens[idx].Kind != TokenKind.ForEnd)
            throw new TemplateException("Unterminated for-block");
        idx++;
        return new ForNode(parts[0].Trim(), parts[1].Trim(), nodes);
    }
}
