using System.Runtime.Versioning;

using DotnetInspector.Libraries;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspect.Web;

[SupportedOSPlatform("browser")]
internal static class BrowserExactMemberPolicy
{
    private const long MaxAssemblyImageBytes =
        64L * 1024 * 1024;

    internal static ApiSurfaceExtractionBounds Bounds { get; } =
        new(
            maxTypes: BrowserApiSurfacePolicy.MaxTypes,
            maxMembers: BrowserApiSurfacePolicy.MaxMembers,
            maxInspectionFailures:
                BrowserApiSurfacePolicy.MaxInspectionFailures,
            maxTypeForwarders:
                BrowserApiSurfacePolicy.MaxTypeForwarders,
            maxMetadataRows:
                BrowserApiSurfacePolicy.MaxMetadataRows,
            maxRetainedTextCharacters:
                BrowserApiSurfacePolicy.MaxRetainedTextCharacters);

    internal static AssemblyContextLibraryMaterializationLimits
        MaterializationLimits { get; } =
            new(
                MaxAssemblyImageBytes,
                MaxAssemblyImageBytes);

    internal static MetadataTypeDefinitionName ParseTypeIdentity(
        string typeIdentity) =>
        MetadataTypeDefinitionName.ParseSerialized(typeIdentity)
            switch
            {
                MetadataTypeDefinitionNameResult.Valid valid =>
                    valid.Name,
                MetadataTypeDefinitionNameResult.Rejected rejected =>
                    throw new ArgumentException(
                        $"The exact Type identity is invalid "
                            + $"({rejected.Rejection.Kind}).",
                        nameof(typeIdentity)),
                _ => throw new InvalidOperationException(
                    "Unknown exact Type identity parse result."),
            };
}
