using DotnetInspector.Diff;
using DotnetInspector.PackageQueries;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Inspectors;

/// <summary>
/// Opens the CLI's one Library per endpoint and asks the single-Library API
/// Diff terminal for only its Library API presentation
/// (diff-system.md#single-library-api-diff-terminal). The CLI keeps endpoint
/// opening, the one-Library pairing, and the workspace and scope lifetime.
/// </summary>
internal static class LibraryApiChangesDiffAdapter
{
    internal static ApiSurfaceProjectionLimits Limits { get; } =
        new(1, 1_000_000, 1_000_000, 1_000, 1_000_000, 10_000_000);

    /// <summary>Compares two local assemblies.</summary>
    internal static async Task<LibraryApiDiffOutcome> ExecuteAsync(
        LibraryPairDiffRequest request,
        AssemblySetEntry before,
        AssemblySetEntry after)
    {
        AssemblyContextParticipant beforeParticipant = CreateParticipant(before);
        AssemblyContextParticipant afterParticipant = CreateParticipant(after);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup beforeGroup =
            workspace.CreateAssemblyContextGroup([beforeParticipant]);
        using AssemblyContextGroup afterGroup =
            workspace.CreateAssemblyContextGroup([afterParticipant]);

        return Execute(
            request,
            new DiffLibraryPair(
                beforeGroup,
                beforeParticipant,
                afterGroup,
                afterParticipant));
    }

    /// <summary>
    /// Compares the one surface Library of each package endpoint, reading
    /// each participant through the scope that holds its group.
    /// </summary>
    internal static async Task<LibraryApiDiffOutcome> ExecuteAsync(
        LibraryPairDiffRequest request,
        PackageEndpointScope before,
        PackageEndpointScope after,
        CancellationToken cancellationToken = default)
    {
        PackageEndpointParticipant beforeLibrary =
            before.SurfaceParticipants.Single();
        PackageEndpointParticipant afterLibrary =
            after.SurfaceParticipants.Single();
        ArtifactRootResult<ArtifactRootResult<LibraryApiDiffOutcome>> result =
            await before.UseSurfaceParticipantAsync(
                    beforeLibrary,
                    (beforeGroup, beforeParticipant, token) =>
                        after.UseSurfaceParticipantAsync(
                            afterLibrary,
                            (afterGroup, afterParticipant, _) =>
                                ValueTask.FromResult(
                                    Execute(
                                        request,
                                        new DiffLibraryPair(
                                            beforeGroup,
                                            beforeParticipant,
                                            afterGroup,
                                            afterParticipant))),
                            token),
                    cancellationToken)
                .ConfigureAwait(false);
        return result switch
        {
            ArtifactRootResult<ArtifactRootResult<LibraryApiDiffOutcome>>.Available
            {
                Value: ArtifactRootResult<LibraryApiDiffOutcome>.Available inner,
            } => inner.Value,
            ArtifactRootResult<ArtifactRootResult<LibraryApiDiffOutcome>>.Available
            {
                Value: ArtifactRootResult<LibraryApiDiffOutcome>.Rejected rejected,
            } => throw new InvalidOperationException(
                "API comparison not compared: the after endpoint rejected its query: "
                    + rejected.Failure),
            ArtifactRootResult<ArtifactRootResult<LibraryApiDiffOutcome>>.Rejected rejected =>
                throw new InvalidOperationException(
                    "API comparison not compared: the before endpoint rejected its query: "
                        + rejected.Failure),
            _ => throw new InvalidOperationException(
                "Unknown package Root query outcome."),
        };
    }

    /// <summary>
    /// The CLI's Library API-only request: the whole Library at the API axis,
    /// or every member for <c>--all</c>, at the CLI's bounds, narrowed by the
    /// <c>-t</c> Type selection.
    /// </summary>
    internal static LibraryPairDiffRequest CreateRequest(
        string name,
        string beforeVersion,
        string afterVersion,
        bool includeAll,
        ApiTypeSelection? typeSelection)
        => new(
            name,
            beforeVersion,
            afterVersion,
            includeAll ? ApiSurfaceScope.IncludeAll : ApiSurfaceScope.ApiAxis,
            Limits,
            typeSelection);

    static LibraryApiDiffOutcome Execute(
        LibraryPairDiffRequest request,
        DiffLibraryPair pair)
    {
        InspectionEnvelope<DiffAnalysisDocument> inspection =
            DiffOperation.ExecuteLibraryPair(
                request,
                pair,
                new DiffPairContent(hostUnavailability: []));
        return inspection.Content.LibraryApi
            ?? throw new InvalidOperationException(
                "The single-Library API Diff terminal returned no Library API outcome.");
    }

    internal static AssemblyContextParticipant CreateParticipant(AssemblySetEntry entry)
    {
        ResolvedAssemblyReference assembly =
            AssemblySetInspectionWorkspace.TryCreateManagedAssembly(
                entry,
                out CandidateOpenFailure? failure)
            ?? throw new InvalidOperationException(
                $"API comparison not compared: '{entry.Path}' ({failure!.Kind}): {failure.Detail}");
        return new(
            assembly,
            new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(entry.Path)
                {
                    PreferImplementationAssemblies = true,
                    AllowPlatformAssemblyVersionRollForward = true,
                }));
    }
}
