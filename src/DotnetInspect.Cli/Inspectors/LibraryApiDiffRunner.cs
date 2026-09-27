using DotnetInspector.PackageQueries;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Inspectors;

internal static class LibraryApiDiffRunner
{
    internal static ApiSurfaceProjectionLimits Limits { get; } =
        new(1, 1_000_000, 1_000_000, 1_000, 1_000_000, 10_000_000);

    internal static async Task<
        InspectionEnvelope<LibraryApiDiffOutcome>> ExecuteAsync(
            AssemblySetEntry before,
            AssemblySetEntry after,
            bool includeAll)
    {
        AssemblyContextParticipant beforeParticipant = CreateParticipant(before);
        AssemblyContextParticipant afterParticipant = CreateParticipant(after);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup beforeGroup =
            workspace.CreateAssemblyContextGroup([beforeParticipant]);
        using AssemblyContextGroup afterGroup =
            workspace.CreateAssemblyContextGroup([afterParticipant]);

        return LibraryApiDiffInspection.Execute(
            beforeGroup,
            beforeParticipant,
            afterGroup,
            afterParticipant,
            includeAll ? ApiSurfaceScope.IncludeAll : ApiSurfaceScope.Public,
            Limits);
    }

    /// <summary>
    /// Compares the one surface Library of each package endpoint, reading
    /// each participant through the scope that holds its group.
    /// </summary>
    internal static async Task<
        InspectionEnvelope<LibraryApiDiffOutcome>> ExecuteAsync(
            PackageEndpointScope before,
            PackageEndpointScope after,
            bool includeAll,
            CancellationToken cancellationToken = default)
    {
        PackageEndpointParticipant beforeLibrary =
            before.SurfaceParticipants.Single();
        PackageEndpointParticipant afterLibrary =
            after.SurfaceParticipants.Single();
        ArtifactRootResult<ArtifactRootResult<
            InspectionEnvelope<LibraryApiDiffOutcome>>> result =
            await before.UseSurfaceParticipantAsync(
                    beforeLibrary,
                    (beforeGroup, beforeParticipant, token) =>
                        after.UseSurfaceParticipantAsync(
                            afterLibrary,
                            (afterGroup, afterParticipant, _) =>
                                ValueTask.FromResult(
                                    LibraryApiDiffInspection.Execute(
                                        beforeGroup,
                                        beforeParticipant,
                                        afterGroup,
                                        afterParticipant,
                                        includeAll
                                            ? ApiSurfaceScope.IncludeAll
                                            : ApiSurfaceScope.Public,
                                        Limits)),
                            token),
                    cancellationToken)
                .ConfigureAwait(false);
        return result switch
        {
            ArtifactRootResult<ArtifactRootResult<
                InspectionEnvelope<LibraryApiDiffOutcome>>>.Available
            {
                Value: ArtifactRootResult<
                    InspectionEnvelope<LibraryApiDiffOutcome>>.Available inner,
            } => inner.Value,
            ArtifactRootResult<ArtifactRootResult<
                InspectionEnvelope<LibraryApiDiffOutcome>>>.Available
            {
                Value: ArtifactRootResult<
                    InspectionEnvelope<LibraryApiDiffOutcome>>.Rejected rejected,
            } => throw new InvalidOperationException(
                "API comparison not compared: the after endpoint rejected its query: "
                    + rejected.Failure),
            ArtifactRootResult<ArtifactRootResult<
                InspectionEnvelope<LibraryApiDiffOutcome>>>.Rejected rejected =>
                throw new InvalidOperationException(
                    "API comparison not compared: the before endpoint rejected its query: "
                        + rejected.Failure),
            _ => throw new InvalidOperationException(
                "Unknown package Root query outcome."),
        };
    }

    static AssemblyContextParticipant CreateParticipant(AssemblySetEntry entry)
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
