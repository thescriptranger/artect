using System.Collections.Generic;

namespace Artect.Generation;

public interface IEmitter
{
    IReadOnlyList<EmittedFile> Emit(EmitterContext ctx);
}
