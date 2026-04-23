using System;

namespace Artect.Config;

public sealed class YamlException : Exception
{
    public YamlException(string message) : base(message) { }
    public YamlException(string message, Exception inner) : base(message, inner) { }
}
