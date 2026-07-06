using System.Reflection;

namespace BudgetyTzar.Api;

public sealed record RuntimeVersion(string ProductVersion, string InformationalVersion)
{
    public static RuntimeVersion Current { get; } = Create();

    private static RuntimeVersion Create()
    {
        var assembly = typeof(RuntimeVersion).Assembly;
        var informationalVersion =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        var productVersion = informationalVersion.Split('+', 2)[0];

        return new RuntimeVersion(productVersion, informationalVersion);
    }
}
