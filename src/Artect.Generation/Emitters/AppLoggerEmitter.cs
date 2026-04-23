using System.Collections.Generic;
using Artect.Templating;

namespace Artect.Generation.Emitters;

/// <summary>
/// Emits the <c>IAppLogger&lt;T&gt;</c> port (Application layer) and the
/// <c>MicrosoftAppLogger&lt;T&gt;</c> adapter (Infrastructure layer).
/// These are baseline ports — always emitted, not gated by any config flag.
/// </summary>
public sealed class AppLoggerEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        var project = ctx.Config.ProjectName;
        var portsNs  = CleanLayout.PortsNamespace(project);
        var loggingNs = CleanLayout.LoggingNamespace(project);

        // ── Interface ──────────────────────────────────────────────────────────
        var ifaceTemplate = TemplateParser.Parse(ctx.Templates.Load("AppLogger.cs.artect"));
        var ifaceRendered = Renderer.Render(ifaceTemplate, new
        {
            Namespace = portsNs,
        });

        // ── Adapter ────────────────────────────────────────────────────────────
        var adapterTemplate = TemplateParser.Parse(ctx.Templates.Load("MicrosoftAppLogger.cs.artect"));
        var adapterRendered = Renderer.Render(adapterTemplate, new
        {
            ApplicationPortsNamespace = portsNs,
            Namespace = loggingNs,
        });

        return new[]
        {
            new EmittedFile(CleanLayout.PortsPath(project, "IAppLogger"), ifaceRendered),
            new EmittedFile(CleanLayout.LoggingPath(project, "MicrosoftAppLogger"), adapterRendered),
        };
    }
}
