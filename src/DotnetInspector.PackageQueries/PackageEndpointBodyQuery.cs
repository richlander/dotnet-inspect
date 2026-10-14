using System.Collections.Immutable;

using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// One implementation participant of a package endpoint, ready for a
/// method-body view: its image, a descriptor that reads that image, and the
/// resolver that binds its references within the endpoint's implementation
/// group.
/// </summary>
/// <remarks>
/// The resolver, group, and group participant are valid only inside the
/// <see cref="PackageEndpointBodyQuery"/> callback that issued them. The
/// assembly descriptor and image remain valid afterward.
/// </remarks>
public sealed class PackageEndpointBodyParticipant
{
    internal PackageEndpointBodyParticipant(
        PackageEndpointParticipant descriptor,
        ResolvedAssemblyReference assembly,
        ImmutableArray<byte> image,
        IAssemblyReferenceResolver resolver,
        AssemblyContextGroup group,
        AssemblyContextParticipant groupParticipant)
    {
        Descriptor = descriptor;
        Assembly = assembly;
        Image = image;
        Resolver = resolver;
        Group = group;
        GroupParticipant = groupParticipant;
    }

    /// <summary>The implementation participant, with its package provenance.</summary>
    public PackageEndpointParticipant Descriptor { get; }

    /// <summary>A descriptor whose content is <see cref="Image"/>.</summary>
    public ResolvedAssemblyReference Assembly { get; }

    /// <summary>The participant's immutable image.</summary>
    public ImmutableArray<byte> Image { get; }

    /// <summary>
    /// Binds the participant's references through the implementation group's
    /// binding policy, never through installed runtimes or local caches.
    /// </summary>
    public IAssemblyReferenceResolver Resolver { get; }

    /// <summary>
    /// The endpoint's implementation group, for a group query such as
    /// SourceHouse's over <see cref="GroupParticipant"/>.
    /// </summary>
    public AssemblyContextGroup Group { get; }

    /// <summary>The participant as the implementation group holds it, with its package provenance.</summary>
    public AssemblyContextParticipant GroupParticipant { get; }
}

/// <summary>
/// Hands the implementation participants of two package endpoints to one
/// method-body comparison (docs/design/package-endpoint-scope.md, adoption
/// step 4 part 3). Both endpoints must have been opened with
/// <c>SurfaceAndImplementation</c>.
/// </summary>
/// <remarks>
/// Implementation participants are the package's runtime assets for the
/// selected framework, so a package that ships <c>ref/</c> compares its
/// implementation, never its reference stubs. The callback runs while both
/// implementation groups are held; it must return materialized results.
/// </remarks>
public static class PackageEndpointBodyQuery
{
    public static async Task<TResult> ExecuteAsync<TResult>(
        PackageEndpointScope before,
        PackageEndpointScope after,
        Func<
            ImmutableArray<PackageEndpointBodyParticipant>,
            ImmutableArray<PackageEndpointBodyParticipant>,
            CancellationToken,
            ValueTask<TResult>> query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(query);

        ArtifactRootResult<TResult> outer = await before.UseImplementationAsync(
                async (beforeGroup, beforeParticipants, beforeToken) =>
                {
                    Bound beforeBound = Bind(before, beforeGroup, beforeParticipants, beforeToken);
                    ArtifactRootResult<TResult> inner = await after.UseImplementationAsync(
                            async (afterGroup, afterParticipants, afterToken) =>
                            {
                                Bound afterBound = Bind(after, afterGroup, afterParticipants, afterToken);
                                TResult value = await query(
                                        beforeBound.Participants,
                                        afterBound.Participants,
                                        afterToken)
                                    .ConfigureAwait(false);
                                beforeBound.ValidateForPublication();
                                afterBound.ValidateForPublication();
                                return value;
                            },
                            beforeToken)
                        .ConfigureAwait(false);
                    return Unwrap(after, inner);
                },
                cancellationToken)
            .ConfigureAwait(false);
        return Unwrap(before, outer);
    }

    static Bound Bind(
        PackageEndpointScope scope,
        AssemblyContextGroup group,
        ImmutableArray<PackageEndpointBoundParticipant> participants,
        CancellationToken cancellationToken)
    {
        var bound = ImmutableArray.CreateBuilder<PackageEndpointBodyParticipant>(participants.Length);
        var resolvers = new List<AssemblyContextAnalysisSource.BindingPolicyResolver>(participants.Length);
        foreach (PackageEndpointBoundParticipant participant in participants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResolvedAssemblyReference assembly = participant.Participant.Assembly;
            AssemblyImageAccessResult<(ImmutableArray<byte> Image, ResolvedAssemblyReference Retained)> access =
                group.UseSnapshot(
                    assembly,
                    cancellationToken,
                    snapshot => (snapshot.Content, snapshot.RetainAssemblyReference(assembly)));
            var (image, retained) = access switch
            {
                AssemblyImageAccessResult<(ImmutableArray<byte>, ResolvedAssemblyReference)>.Available available =>
                    available.Value,
                AssemblyImageAccessResult<(ImmutableArray<byte>, ResolvedAssemblyReference)>.Rejected rejected =>
                    throw new InvalidOperationException(
                        $"The implementation image {participant.Descriptor.Asset.Path} of "
                            + $"{scope.Root.Root.PackageId}@{scope.PackageVersion} was rejected "
                            + $"({rejected.Failure.Kind}): {rejected.Failure.Detail}"),
                _ => throw new InvalidOperationException("Unknown assembly image access outcome."),
            };
            AssemblyContextAnalysisSource.BindingPolicyResolver resolver =
                AssemblyContextAnalysisSource.Resolver(group, new AssemblyContextSubject(assembly));
            resolvers.Add(resolver);
            bound.Add(new PackageEndpointBodyParticipant(
                participant.Descriptor,
                retained,
                image,
                resolver,
                group,
                participant.Participant));
        }
        return new(bound.MoveToImmutable(), resolvers);
    }

    static TResult Unwrap<TResult>(PackageEndpointScope scope, ArtifactRootResult<TResult> result) =>
        result switch
        {
            ArtifactRootResult<TResult>.Available available => available.Value,
            ArtifactRootResult<TResult>.Rejected rejected =>
                throw new InvalidOperationException(
                    $"The package endpoint {scope.Root.Root.PackageId}@{scope.PackageVersion} "
                        + $"rejected its implementation query: {rejected.Failure}"),
            _ => throw new InvalidOperationException("Unknown package Root query outcome."),
        };

    sealed record Bound(
        ImmutableArray<PackageEndpointBodyParticipant> Participants,
        List<AssemblyContextAnalysisSource.BindingPolicyResolver> Resolvers)
    {
        public void ValidateForPublication()
        {
            foreach (AssemblyContextAnalysisSource.BindingPolicyResolver resolver in Resolvers)
                resolver.ValidateForPublication();
        }
    }
}
