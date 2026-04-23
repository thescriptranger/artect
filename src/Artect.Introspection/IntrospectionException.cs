using System;

namespace Artect.Introspection;

public sealed class IntrospectionException : Exception
{
    public IntrospectionException(string message) : base(message) { }
    public IntrospectionException(string message, Exception inner) : base(message, inner) { }
}
