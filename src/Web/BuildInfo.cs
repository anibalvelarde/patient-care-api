using System.Reflection;

namespace Neurocorp.Api.Web;

/// <summary>
/// Exposes the compile-time build timestamp stamped into the Web assembly by MSBuild
/// (AssemblyMetadata key "BuildTimestampUtc" in Web.csproj).
/// </summary>
public static class BuildInfo
{
    public const string MetadataKey = "BuildTimestampUtc";

    public static string BuildTimestampUtc { get; } =
        typeof(BuildInfo).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == MetadataKey)?.Value ?? "unknown";
}
