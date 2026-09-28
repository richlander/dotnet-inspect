using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using static DotnetInspect.Web.BrowserOrdinaryWorkerJsonBudget;

namespace DotnetInspect.Web.Interop.Catalog;

internal static class BrowserRetainedWorkspaceDetailWireProjection
{
    internal static BrowserRetainedWorkspacePackageAdmissionResult Admit(
        BrowserRetainedWorkspacePackageAdmissionResult result) =>
        Admit(
            result,
            BrowserCatalogJsonContext.Default.BrowserRetainedWorkspacePackageAdmissionResult,
            static message => new("unavailable", null, message),
            static value => value.Package is { Surface.Types.Length: > 1 } package
                ? value with
                {
                    Package = package with
                    {
                        Surface = HalfPage(package.Surface),
                        TypePage = package.TypePage with
                        {
                            NextOffset = package.TypePage.Offset + package.Surface.Types.Length / 2,
                        },
                    },
                }
                : null);

    internal static BrowserRetainedWorkspacePlatformAdmissionResult Admit(
        BrowserRetainedWorkspacePlatformAdmissionResult result) =>
        Admit(
            result,
            BrowserCatalogJsonContext.Default.BrowserRetainedWorkspacePlatformAdmissionResult,
            static message => new("unavailable", null, message),
            static value => value.Platform is { Surface.Types.Length: > 1 } platform
                ? value with
                {
                    Platform = platform with
                    {
                        Surface = HalfPage(platform.Surface),
                        TypePage = platform.TypePage with
                        {
                            NextOffset = platform.TypePage.Offset + platform.Surface.Types.Length / 2,
                        },
                    },
                }
                : null);

    static BrowserPackageSurface HalfPage(BrowserPackageSurface surface) =>
        surface with { Types = surface.Types[..(surface.Types.Length / 2)] };

    static T Admit<T>(
        T result,
        JsonTypeInfo<T> typeInfo,
        Func<string, T> unavailable,
        Func<T, T?>? shrink = null)
        where T : class
    {
        while (true)
        {
            using JsonDocument document = JsonSerializer.SerializeToDocument(result, typeInfo);
            long entries = CollectionEntries(document.RootElement)
                + OrdinaryWorkerResultTupleOverhead;
            long characters = JsonStringifyCharacters(document.RootElement)
                + OrdinaryWorkerResultTupleOverhead;
            if (entries <= MaxOrdinaryWorkerCollectionEntries
                && characters <= MaxOrdinaryWorkerJsonCharacters)
            {
                return result;
            }
            if (shrink?.Invoke(result) is { } smaller)
            {
                result = smaller;
                continue;
            }
            return unavailable(
                "The retained row detail exceeds the ordinary Worker transport limit even at its smallest Type page "
                + $"({entries} collection entries, {characters} JSON characters; "
                + $"limits {MaxOrdinaryWorkerCollectionEntries} and {MaxOrdinaryWorkerJsonCharacters}).");
        }
    }
}
