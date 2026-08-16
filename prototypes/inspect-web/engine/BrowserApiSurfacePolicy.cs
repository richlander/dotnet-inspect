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
/// <c>BrowserEngineBoundaryTests.ApiSurfaceProjection_IsBoundedAndReportsTruncation</c> gates both
/// halves: the bound trips on an over-budget projection, and an ordinary one is untouched.
/// </para>
/// </remarks>
[SupportedOSPlatform("browser")]
internal static class BrowserApiSurfacePolicy
{
    // The aggregate text ceiling leaves room for large multi-member assemblies. The much smaller
    // per-model ceiling stops one hostile type or signature before it can consume that allowance.
    // ApiSurfaceProjection_IsBoundedAndReportsTruncation,
    // ApiSurfacePolicy_AcceptsCompactMultiMemberSurfaceAboveOldTextLimit, and
    // ParameterFanOut_IsStoppedBeforeLargeSignatureMaterialization gate those three properties.
    internal const int MaxParticipants = BrowserInspectionScope.MaxAssembliesPerRole;
    internal const int MaxTypes = 100_000;
    internal const int MaxMembers = 1_000_000;
    internal const int MaxInspectionFailures = 1_024;
    internal const int MaxTypeForwarders = 100_000;
    internal const int MaxMetadataRows = 250_000;
    internal const int MaxRetainedTextCharacters = 32_000_000;
    internal const int MaxRetainedTextCharactersPerModel = 1_000_000;

    /// <summary>The bounds every browser API-surface projection runs under.</summary>
    internal static ApiSurfaceProjectionLimits Limits { get; } =
        new(
            MaxParticipants,
            MaxTypes,
            MaxMembers,
            MaxInspectionFailures,
            MaxTypeForwarders,
            MaxMetadataRows,
            MaxRetainedTextCharacters,
            MaxRetainedTextCharactersPerModel);

    /// <summary>
    /// The visible notice for a truncated projection, or null when the projection was complete.
    /// </summary>
    internal static string? TruncationNotice(ApiSurfaceProjectionTruncation? truncation)
    {
        if (truncation is null)
            return null;

        string limit = truncation.Limit switch
        {
            ApiSurfaceProjectionLimit.RetainedTextCharacters =>
                "retained-text-character",
            ApiSurfaceProjectionLimit.RetainedTextCharactersPerModel =>
                "per-model retained-text-character",
            _ => truncation.Limit.ToString(),
        };
        return $"API surface truncated at the browser {limit} bound "
                + $"({truncation.Bound}): projected {truncation.ProjectedTypes} type(s) and "
                + $"{truncation.ProjectedMembers} member(s), retained "
                + $"{truncation.ProjectedInspectionFailures} inspection failure(s) and "
                + $"{truncation.ProjectedTypeForwarders} type forwarder(s) after inspecting "
                + $"{truncation.InspectedMetadataRows} metadata row(s) and retaining "
                + $"{truncation.ProjectedRetainedTextCharacters} text character(s) from "
                + $"{truncation.ProjectedParticipants} assembly(ies); "
                + $"{truncation.OmittedParticipants} assembly(ies) were not projected.";
    }
}
