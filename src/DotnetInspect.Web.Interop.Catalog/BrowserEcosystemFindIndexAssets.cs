using System.Collections.Immutable;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using DotnetInspector.Ecosystems;

namespace DotnetInspect.Web.Interop.Catalog;

internal abstract record BrowserEcosystemFindIndexAssetLoad
{
    private protected BrowserEcosystemFindIndexAssetLoad() { }

    internal sealed record Available(
        ImmutableArray<EcosystemFindIndexArtifact> Artifacts) :
        BrowserEcosystemFindIndexAssetLoad;

    internal sealed record Invalid(string Reason) :
        BrowserEcosystemFindIndexAssetLoad;
}

[SupportedOSPlatform("browser")]
internal static partial class BrowserEcosystemFindIndexAssets
{
    const string ModuleName = "ecosystem-find-index-assets";
    const string ModulePath = "/ecosystem-find-index-assets.js";

    internal static ImmutableArray<EcosystemPackId> SupportedEcosystems =>
        [EcosystemPackIds.Aspire, EcosystemPackIds.MicrosoftExtensions];

    internal static Task<BrowserEcosystemFindIndexAssetLoad> LoadAsync() =>
        LoadAsync(SupportedEcosystems);

    internal static async Task<BrowserEcosystemFindIndexAssetLoad> LoadAsync(
        IReadOnlyCollection<EcosystemPackId> ecosystems)
    {
        ArgumentNullException.ThrowIfNull(ecosystems);
        if (ecosystems.Count == 0)
        {
            return new BrowserEcosystemFindIndexAssetLoad.Available([]);
        }
        if (ecosystems.Any(ecosystem =>
                !SupportedEcosystems.Contains(ecosystem)))
        {
            throw new ArgumentException(
                "Indexed Find asset selection contains an unsupported Ecosystem.",
                nameof(ecosystems));
        }

        try
        {
            await JSHost.ImportAsync(ModuleName, ModulePath)
                .ConfigureAwait(false);
            var artifacts =
                ImmutableArray.CreateBuilder<EcosystemFindIndexArtifact>();
            if (ecosystems.Contains(EcosystemPackIds.Aspire))
            {
                artifacts.Add(Load(
                    await LoadAspireAsync().ConfigureAwait(false),
                    EcosystemPackIds.Aspire));
            }
            if (ecosystems.Contains(EcosystemPackIds.MicrosoftExtensions))
            {
                artifacts.Add(Load(
                    await LoadMicrosoftExtensionsAsync().ConfigureAwait(false),
                    EcosystemPackIds.MicrosoftExtensions));
            }
            return new BrowserEcosystemFindIndexAssetLoad.Available(
                artifacts.DrainToImmutable());
        }
        catch (Exception exception) when (
            exception is FormatException
                or InvalidOperationException
                or JSException)
        {
            return new BrowserEcosystemFindIndexAssetLoad.Invalid(
                exception.Message);
        }
    }

    static EcosystemFindIndexArtifact Load(
        string encoded,
        EcosystemPackId expected)
    {
        EcosystemFindIndexLoadResult load = EcosystemFindIndexJson.Load(
            Convert.FromBase64String(encoded));
        if (load is not EcosystemFindIndexLoadResult.Available available)
        {
            throw new InvalidOperationException(
                $"The '{expected}' indexed Find asset is invalid: "
                    + ((EcosystemFindIndexLoadResult.Invalid)load).Detail);
        }
        if (available.Artifact.Ecosystem != expected)
        {
            throw new InvalidOperationException(
                $"The '{expected}' indexed Find asset names "
                    + $"'{available.Artifact.Ecosystem}'.");
        }
        return available.Artifact;
    }

    [JSImport("loadAspire", ModuleName)]
    private static partial Task<string> LoadAspireAsync();

    [JSImport("loadMicrosoftExtensions", ModuleName)]
    private static partial Task<string> LoadMicrosoftExtensionsAsync();
}
