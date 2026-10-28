using System.Reflection;
using DotnetInspector.Ecosystems;

namespace DotnetInspect.Cli.Inspectors;

internal abstract record EcosystemFindIndexResourceLoad
{
    private EcosystemFindIndexResourceLoad()
    {
    }

    internal sealed record Available(EcosystemFindIndexArtifact Artifact)
        : EcosystemFindIndexResourceLoad;

    internal sealed record Invalid(string Message)
        : EcosystemFindIndexResourceLoad;
}

internal static class EcosystemFindIndexResources
{
    private const string AspireResource =
        "dotnet-inspect.find-index.aspire.json";
    private const string MicrosoftExtensionsResource =
        "dotnet-inspect.find-index.microsoft-extensions.json";

    internal static bool IsAvailable(EcosystemPackId ecosystem) =>
        ecosystem == EcosystemPackIds.Aspire
        || ecosystem == EcosystemPackIds.MicrosoftExtensions;

    internal static EcosystemFindIndexResourceLoad Load(
        EcosystemPackId ecosystem)
    {
        string resource = ecosystem == EcosystemPackIds.Aspire
            ? AspireResource
            : ecosystem == EcosystemPackIds.MicrosoftExtensions
                ? MicrosoftExtensionsResource
                : throw new ArgumentException(
                    $"Ecosystem '{ecosystem}' has no embedded Find index.",
                    nameof(ecosystem));
        return EcosystemFindIndexJson.Load(ReadBytes(resource)) switch
        {
            EcosystemFindIndexLoadResult.Available available
                when available.Artifact.Ecosystem == ecosystem =>
                new EcosystemFindIndexResourceLoad.Available(
                    available.Artifact),
            EcosystemFindIndexLoadResult.Available available =>
                new EcosystemFindIndexResourceLoad.Invalid(
                    $"Embedded artifact '{resource}' names "
                    + $"'{available.Artifact.Ecosystem}', not '{ecosystem}'."),
            EcosystemFindIndexLoadResult.Invalid invalid =>
                new EcosystemFindIndexResourceLoad.Invalid(
                    invalid.Detail.ToString()),
            _ => throw new InvalidOperationException(
                "Unknown Ecosystem Find index load result."),
        };
    }

    internal static byte[] ReadBytes(EcosystemPackId ecosystem)
    {
        string resource = ecosystem == EcosystemPackIds.Aspire
            ? AspireResource
            : ecosystem == EcosystemPackIds.MicrosoftExtensions
                ? MicrosoftExtensionsResource
                : throw new ArgumentException(
                    $"Ecosystem '{ecosystem}' has no embedded Find index.",
                    nameof(ecosystem));
        return ReadBytes(resource);
    }

    private static byte[] ReadBytes(string resource)
    {
        Assembly assembly = typeof(EcosystemFindIndexResources).Assembly;
        using Stream stream =
            assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(
                $"Embedded Ecosystem Find index '{resource}' is missing.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
}
