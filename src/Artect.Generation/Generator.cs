using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;
using Artect.Templating;

namespace Artect.Generation;

public sealed class Generator
{
    readonly IReadOnlyList<IEmitter> _emitters;
    public Generator(IReadOnlyList<IEmitter> emitters) =>
        _emitters = emitters.OrderBy(e => e.GetType().Name, StringComparer.Ordinal).ToList();

    public void Generate(ArtectConfig cfg, SchemaGraph graph, string outputRoot)
    {
        var model = NamedSchemaModel.Build(graph);
        var templateAssembly = typeof(Artect.Templates.TemplatesMarker).Assembly;
        var loader = new TemplateLoader(templateAssembly, "Artect.Templates.Files");
        var ctx = new EmitterContext(cfg, graph, model, loader);

        var all = new List<EmittedFile>();
        foreach (var emitter in _emitters) all.AddRange(emitter.Emit(ctx));

        var wrapped = all.Select(f => GeneratedByRegionWrapper.Wrap(f, cfg.GeneratedByLabel)).ToList();

        foreach (var f in wrapped.OrderBy(f => f.RelativePath, StringComparer.Ordinal))
        {
            var full = Path.Combine(outputRoot, f.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, f.Contents);
        }
    }
}
