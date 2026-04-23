using System;

namespace Artect.Templating;

public sealed class TemplateException : Exception
{
    public TemplateException(string message) : base(message) { }
    public TemplateException(string message, Exception inner) : base(message, inner) { }
}
