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

    /// <summary>
    /// The connection string Artect used for introspection, if any. Threaded through so the
    /// <see cref="Emitters.AppSettingsEmitter"/> can embed it into the generated
    /// <c>appsettings.Development.json</c> (which is gitignored). Never embedded in
    /// <c>appsettings.json</c> or <c>artect.yaml</c> — those files are typically committed.
    /// </summary>
    public string? ConnectionString { get; }

    public EmitterContext(ArtectConfig cfg, SchemaGraph graph, NamedSchemaModel model, TemplateLoader templates, string? connectionString = null)
    {
        Config = cfg;
        Graph = graph;
        Model = model;
        Templates = templates;
        ConnectionString = connectionString;
    }
}
