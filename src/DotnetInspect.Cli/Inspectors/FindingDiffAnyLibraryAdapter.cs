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
/// Library or Type route over it through the CLI binding
/// (finding-diff-any.md#adoption, step 9). The CLI keeps endpoint opening,
/// the one-Library pairing, and the workspace and scope lifetime, as it does
/// for the single-Library API Diff terminal; the comparison runs unsliced.
/// </summary>
internal static class FindingDiffAnyLibraryAdapter
{
    /// <summary>Compares two local or platform assemblies.</summary>
    internal static Task<InspectionEnvelope<FindingDiffAnyLibraryDocument>> ExecuteAsync(
        string name,
        string beforeVersion,
        string afterVersion,
        FindingDiffAnyAxes axes,
        AssemblySetEntry before,
        AssemblySetEntry after,
        CancellationToken cancellationToken)
        => RunAsync(
            before,
            after,
            (pair, token) => FindingDiffAnyCommandCapability.Binding.ExecuteAsync(
                new FindingDiffAnyLibraryRequest(name, beforeVersion, afterVersion, pair, axes),
                token),
            cancellationToken);

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
        => RunAsync(
            before,
            after,
            (pair, token) => FindingDiffAnyCommandCapability.Binding.ExecuteAsync(
                new FindingDiffAnyLibraryRequest(name, beforeVersion, afterVersion, pair, axes),
                token),
            cancellationToken);

    /// <summary>Compares one Type of two local or platform assemblies.</summary>
    internal static Task<InspectionEnvelope<FindingDiffAnyTypeDocument>> ExecuteTypeAsync(
        string name,
        string beforeVersion,
        string afterVersion,
        string type,
        AssemblySetEntry before,
        AssemblySetEntry after,
        CancellationToken cancellationToken)
        => RunAsync(
            before,
            after,
            (pair, token) => FindingDiffAnyCommandCapability.TypeBinding.ExecuteAsync(
                new FindingDiffAnyTypeRequest(name, beforeVersion, afterVersion, pair, type),
                token),
            cancellationToken);

    /// <summary>
    /// Compares one Type of the one implementation Library of each package
    /// endpoint, as the Library-level overload reads it.
    /// </summary>
    internal static Task<InspectionEnvelope<FindingDiffAnyTypeDocument>> ExecuteTypeAsync(
        string name,
        string beforeVersion,
        string afterVersion,
        string type,
        PackageEndpointScope before,
        PackageEndpointScope after,
        CancellationToken cancellationToken)
        => RunAsync(
            before,
            after,
            (pair, token) => FindingDiffAnyCommandCapability.TypeBinding.ExecuteAsync(
                new FindingDiffAnyTypeRequest(name, beforeVersion, afterVersion, pair, type),
                token),
            cancellationToken);

    static async Task<T> RunAsync<T>(
        AssemblySetEntry before,
        AssemblySetEntry after,
        Func<DiffLibraryPair, CancellationToken, ValueTask<T>> run,
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
        return await run(
                new DiffLibraryPair(beforeGroup, beforeParticipant, afterGroup, afterParticipant),
                cancellationToken)
            .ConfigureAwait(false);
    }

    static Task<T> RunAsync<T>(
        PackageEndpointScope before,
        PackageEndpointScope after,
        Func<DiffLibraryPair, CancellationToken, ValueTask<T>> run,
        CancellationToken cancellationToken)
        => PackageEndpointBodyQuery.ExecuteAsync(
            before,
            after,
            (from, to, token) =>
            {
                PackageEndpointBodyParticipant beforeLibrary = Single(from, "before");
                PackageEndpointBodyParticipant afterLibrary = Single(to, "after");
                return run(
                    new DiffLibraryPair(
                        beforeLibrary.Group,
                        beforeLibrary.GroupParticipant,
                        afterLibrary.Group,
                        afterLibrary.GroupParticipant),
                    token);
            },
            cancellationToken);

    static PackageEndpointBodyParticipant Single(
        ImmutableArray<PackageEndpointBodyParticipant> participants,
        string role)
        => participants.Length == 1
            ? participants[0]
            : throw new InvalidOperationException(
                $"Finding Diff Any requires exactly one implementation Library at each package endpoint; "
                + $"the {role} endpoint has {participants.Length}.");
}
