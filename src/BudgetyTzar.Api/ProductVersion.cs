using System.Reflection;

namespace BudgetyTzar.Api;

public static class ProductVersion
{
    public const string ProductName = "BudgetyTzar";

    public static string SemanticVersion => InformationalVersion.Split('+', 2)[0];

    public static string InformationalVersion =>
        typeof(ProductVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";

    public static string? BuildMetadata
    {
        get
        {
            var parts = InformationalVersion.Split('+', 2);
            return parts.Length == 2 ? parts[1] : null;
        }
    }
}
