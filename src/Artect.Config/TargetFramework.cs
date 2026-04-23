namespace Artect.Config;
public enum TargetFramework { Net8_0, Net9_0, Net10_0 }

public static class TargetFrameworkExtensions
{
    public static string ToMoniker(this TargetFramework tfm) => tfm switch
    {
        TargetFramework.Net8_0 => "net8.0",
        TargetFramework.Net9_0 => "net9.0",
        TargetFramework.Net10_0 => "net10.0",
        _ => throw new System.ArgumentOutOfRangeException(nameof(tfm))
    };

    public static TargetFramework FromMoniker(string moniker) => moniker switch
    {
        "net8.0" => TargetFramework.Net8_0,
        "net9.0" => TargetFramework.Net9_0,
        "net10.0" => TargetFramework.Net10_0,
        _ => throw new System.ArgumentException($"Unsupported target framework '{moniker}'.", nameof(moniker))
    };

    public static string MajorVersion(this TargetFramework tfm) => tfm switch
    {
        TargetFramework.Net8_0 => "8",
        TargetFramework.Net9_0 => "9",
        TargetFramework.Net10_0 => "10",
        _ => throw new System.ArgumentOutOfRangeException(nameof(tfm))
    };

    public static string DockerTag(this TargetFramework tfm) => tfm switch
    {
        TargetFramework.Net8_0 => "8.0",
        TargetFramework.Net9_0 => "9.0",
        TargetFramework.Net10_0 => "10.0",
        _ => throw new System.ArgumentOutOfRangeException(nameof(tfm))
    };
}
