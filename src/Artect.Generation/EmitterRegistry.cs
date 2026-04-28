using System;
using System.Collections.Generic;
using System.Linq;

namespace Artect.Generation;

/// <summary>
/// V#16: emitter discovery via reflection over the Artect.Generation assembly.
/// Adding a new emitter requires no edit to this file — declare the class,
/// implement <see cref="IEmitter"/>, give it a public parameterless constructor,
/// and it is auto-registered. Mark with <see cref="ExcludeFromAutoRegistrationAttribute"/>
/// to opt out (experimental work, composite emitters, test fixtures).
///
/// The discovery order is alphabetical by full type name so generator output
/// remains byte-deterministic across machines and runs.
/// </summary>
public static class EmitterRegistry
{
    public static IReadOnlyList<IEmitter> All() => Discover().ToArray();

    static IEnumerable<IEmitter> Discover()
    {
        var emitterInterface = typeof(IEmitter);
        return emitterInterface.Assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .Where(t => emitterInterface.IsAssignableFrom(t))
            .Where(t => t.GetCustomAttributes(typeof(ExcludeFromAutoRegistrationAttribute), inherit: false).Length == 0)
            .Where(t => t.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .Select(t => (IEmitter)Activator.CreateInstance(t)!);
    }
}
