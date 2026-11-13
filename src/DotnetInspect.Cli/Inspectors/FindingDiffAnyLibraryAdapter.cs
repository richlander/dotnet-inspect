using System.Collections.Immutable;
using DotnetInspect.Cli.Commands;
using DotnetInspector.Diff;
using DotnetInspector.PackageQueries;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Inspectors;

/// <summary>
/// Opens the CLI's one Library per endpoint and runs Finding Diff Any's
/// Library route over it through the CLI binding
/// (finding-diff-any.md#adoption, step 9). The CLI keeps endpoint opening,
/// the one-Library pairing, and the workspace and scope lifetime, as it does
/// for the single-Library API Diff terminal; the comparison runs unsliced.
/// </summary>
internal static class FindingDiffAnyLibraryAdapter
{
    /// <summary>Compares two local or platform assemblies.</summary>
    internal static async Task<InspectionEnvelope<FindingDiffAnyLibraryDocument>> ExecuteAsync(
        string name,
        string beforeVersion,
        string afterVersion,
        FindingDiffAnyAxes axes,
        AssemblySetEntry before,
        AssemblySetEntry after,
        CancellationToken cancellationToken)
    {
        AssemblyContextParticipant beforeParticipant =
            LibraryApiChangesDiffAdapter.CreateParticipant(before);
        AssemblyContextParticipant afterParticipant =
            LibraryApiChangesDiffAdapter.CreateParticipant(after);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup beforeGroup =
            workspace.CreateAssemblyContextGroup([beforeParticipant]);
        using AssemblyContextGroup afterGroup =
            workspace.CreateAssemblyContextGroup([afterParticipant]);
        return await FindingDiffAnyCommandCapability.Binding.ExecuteAsync(
                new FindingDiffAnyLibraryRequest(
                    name,
                    beforeVersion,
                    afterVersion,
                    new DiffLibraryPair(
                        beforeGroup,
                        beforeParticipant,
                        afterGroup,
                        afterParticipant),
                    axes),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Compares the one implementation Library of each package endpoint,
    /// read through the implementation group that holds it, so a package that
    /// ships <c>ref/</c> compares its implementation, never its reference
    /// stubs.
    /// </summary>
    internal static Task<InspectionEnvelope<FindingDiffAnyLibraryDocument>> ExecuteAsync(
        string name,
        string beforeVersion,
        string afterVersion,
        FindingDiffAnyAxes axes,
        PackageEndpointScope before,
        PackageEndpointScope after,
        CancellationToken cancellationToken)
        => PackageEndpointBodyQuery.ExecuteAsync(
            before,
            after,
            (from, to, token) =>
            {
                PackageEndpointBodyParticipant beforeLibrary = Single(from, "before");
                PackageEndpointBodyParticipant afterLibrary = Single(to, "after");
                return FindingDiffAnyCommandCapability.Binding.ExecuteAsync(
                    new FindingDiffAnyLibraryRequest(
                        name,
                        beforeVersion,
                        afterVersion,
                        new DiffLibraryPair(
                            beforeLibrary.Group,
                            beforeLibrary.GroupParticipant,
                            afterLibrary.Group,
                            afterLibrary.GroupParticipant),
                        axes),
                    token);
            },
            cancellationToken);

    static PackageEndpointBodyParticipant Single(
        ImmutableArray<PackageEndpointBodyParticipant> participants,
        string role)
        => participants.Length == 1
            ? participants[0]
            : throw new InvalidOperationException(
                $"Changed Types requires exactly one implementation Library at each package endpoint; "
                + $"the {role} endpoint has {participants.Length}.");
}
