using System.Collections.Generic;
using Artect.Config;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits <c>artect.yaml</c> at the scaffold root by serialising the current
/// <see cref="ArtectConfig"/> via <see cref="YamlWriter"/>.
/// The connection string is intentionally omitted — <see cref="YamlWriter"/> never writes it.
/// </summary>
public sealed class ArtectConfigEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var yaml = YamlWriter.Write(ctx.Config);
        return new[] { new EmittedFile("artect.yaml", yaml) };
    }
}
