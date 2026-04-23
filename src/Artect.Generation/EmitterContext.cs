using System.Collections.Generic;
using Artect.Config;
using Artect.Core.Schema;
using Artect.Naming;
using Artect.Templating;

namespace Artect.Generation;

public sealed class EmitterContext
{
    public ArtectConfig Config { get; }
    public SchemaGraph Graph { get; }
    public NamedSchemaModel Model { get; }
    public TemplateLoader Templates { get; }
    public IReadOnlyDictionary<string, string> NamingCorrections => Config.NamingCorrections;

    public EmitterContext(ArtectConfig cfg, SchemaGraph graph, NamedSchemaModel model, TemplateLoader templates)
    {
        Config = cfg;
        Graph = graph;
        Model = model;
        Templates = templates;
    }
}
