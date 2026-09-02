using System.Runtime.Versioning;
using DotnetInspector.Queries;

namespace InspectWeb.Engine;

/// <summary>
/// The browser's explicit API-surface projection policy: what a single-threaded, memory-bounded
/// tab is willing to walk and to serialize for one package load.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AssemblyContextApiSurfaceQuery.Definition"/> is declared
/// <see cref="InspectionCost.Unbounded"/> — "potentially large, slow, or fan-out work that must be
/// explicitly requested" — and a package load is not an explicit request for unbounded work. The
/// browser therefore invokes only
/// <see cref="AssemblyContextApiSurfaceQuery.ExecuteBounded(AssemblyContextGroup, ApiSurfaceScope, ApiSurfaceProjectionLimits, IReadOnlyList{AssemblyContextParticipant})"/>,
/// and <c>BannedSymbols.txt</c> makes the compiler enforce that: the unbounded entry points do not
/// bind in this project at all.
/// </para>
/// <para>
/// Three things bound the work. The workspace already caps retained image bytes per role and
/// assemblies per role before any query runs; the participant selection below projects only the
/// requested coordinate's assemblies rather than every assembly of every package the site happens
/// to have open; and these limits stop the projection at the first bound it would exceed. An early
/// stop is reported as <see cref="ApiSurfaceProjectionTruncation"/> and carried into the response
/// notice, so a truncated surface never reads as a complete one.
/// </para>
/// <para>
/// <c>BrowserEngineBoundaryTests.ApiSurfaceProjection_IsBoundedAndReportsTruncation</c> gates
/// extraction truncation, <c>SurfaceProjection_LongDeclaringTypeStopsIncrementally</c> gates
/// derived transport identities, and <c>ApiSurfacePolicy_AcceptsCoreLibraryAtEveryBrowserScope</c>
/// is the real-artifact policy canary.
/// </para>
/// </remarks>
[SupportedOSPlatform("browser")]
internal static class BrowserApiSurfacePolicy
{
    // The participant bound is the workspace's own assembly-per-role limit, so declaring it here
    // refuses nothing the workspace already accepted; the remaining ceilings bound every retained
    // row kind. The text ceiling admits CoreLib at both scopes the Browser uses while remaining
    // far below what a hostile artifact can amplify from the scope's 64 MB retained-image budget.
    internal const int MaxParticipants = BrowserInspectionScope.MaxAssembliesPerRole;
    internal const int MaxTypes = 100_000;
    internal const int MaxMembers = 1_000_000;
    internal const int MaxInspectionFailures = 1_024;
    internal const int MaxTypeForwarders = 100_000;
    internal const int MaxMetadataRows = 250_000;
    internal const int MaxRetainedTextCharacters = 32_000_000;

    /// <summary>The bounds every browser API-surface projection runs under.</summary>
    internal static ApiSurfaceProjectionLimits Limits { get; } =
        new(
            MaxParticipants,
            MaxTypes,
            MaxMembers,
            MaxInspectionFailures,
            MaxTypeForwarders,
            MaxMetadataRows,
            MaxRetainedTextCharacters);

    /// <summary>
    /// The visible notice for a truncated projection, or null when the projection was complete.
    /// </summary>
    internal static string? TruncationNotice(ApiSurfaceProjectionTruncation? truncation) =>
        truncation is null
            ? null
            : $"API surface truncated at the browser {truncation.Limit} bound "
                + $"({truncation.Bound}): projected {truncation.ProjectedTypes} type(s) and "
                + $"{truncation.ProjectedMembers} member(s), retained "
                + $"{truncation.ProjectedInspectionFailures} inspection failure(s) and "
                + $"{truncation.ProjectedTypeForwarders} type forwarder(s), inspected "
                + $"{truncation.InspectedMetadataRows} metadata row(s), and counted "
                + $"{truncation.ProjectedRetainedTextCharacters} retained text character(s) across "
                + $"{truncation.ProjectedParticipants} assembly(ies); "
                + $"{truncation.OmittedParticipants} assembly(ies) were not projected.";

    internal static string TransportTruncationNotice(
        int projectedParticipants,
        int omittedParticipants,
        int retainedTextCharacters) =>
        $"API surface transport truncated at the browser retained text bound "
        + $"({MaxRetainedTextCharacters}): retained {retainedTextCharacters} text character(s) "
        + $"from {projectedParticipants} assembly(ies); {omittedParticipants} assembly(ies) "
        + "were not projected.";
}
