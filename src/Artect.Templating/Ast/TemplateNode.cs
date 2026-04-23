using System.Collections.Generic;

namespace Artect.Templating.Ast;

public abstract record TemplateNode;
public sealed record TextNode(string Text) : TemplateNode;
public sealed record VariableNode(string Path, IReadOnlyList<string> Filters) : TemplateNode;
public sealed record IfNode(IReadOnlyList<(string Condition, IReadOnlyList<TemplateNode> Body)> Branches, IReadOnlyList<TemplateNode>? ElseBody) : TemplateNode;
public sealed record ForNode(string ItemName, string CollectionPath, IReadOnlyList<TemplateNode> Body) : TemplateNode;
public sealed record TemplateDocument(IReadOnlyList<TemplateNode> Nodes);
