namespace Artect.Config;
public enum TargetFramework { Net8_0, Net9_0 }

public static class TargetFrameworkExtensions
{
    public static string ToMoniker(this TargetFramework tfm) => tfm switch
    {
        TargetFramework.Net8_0 => "net8.0",
        TargetFramework.Net9_0 => "net9.0",
        _ => throw new System.ArgumentOutOfRangeException(nameof(tfm))
    };

    public static TargetFramework FromMoniker(string moniker) => moniker switch
    {
        "net8.0" => TargetFramework.Net8_0,
        "net9.0" => TargetFramework.Net9_0,
        _ => throw new System.ArgumentException($"Unsupported target framework '{moniker}'.", nameof(moniker))
    };
}
