using System;

namespace Artect.Generation;

/// <summary>
/// V#16: opt-out marker for the reflection-based <see cref="EmitterRegistry"/>.
/// An <see cref="IEmitter"/> implementation tagged with this attribute is NOT
/// auto-discovered and will be skipped at scan time. Use this for experimental
/// emitters, composite emitters that another emitter delegates to, or test
/// fixtures that happen to live in the same assembly.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ExcludeFromAutoRegistrationAttribute : Attribute
{
}
