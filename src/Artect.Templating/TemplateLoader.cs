using System.IO;
using System.Reflection;

namespace Artect.Templating;

public sealed class TemplateLoader
{
    readonly Assembly _assembly;
    readonly string _prefix;

    public TemplateLoader(Assembly assembly, string prefix)
    {
        _assembly = assembly;
        _prefix = prefix;
    }

    public string Load(string logicalName)
    {
        var full = $"{_prefix}.{logicalName}";
        using var stream = _assembly.GetManifestResourceStream(full)
            ?? throw new TemplateException($"Embedded template '{full}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
