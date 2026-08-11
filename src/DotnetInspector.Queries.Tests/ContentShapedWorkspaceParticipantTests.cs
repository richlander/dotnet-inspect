using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

/// <summary>
/// Gates the acquisition contract a host without a filesystem depends on: a participant minted
/// from in-memory content is acquired by the group, and one minted with a placeholder identity is
/// rejected rather than acquired.
/// </summary>
public sealed class ContentShapedWorkspaceParticipantTests
{
    [Fact]
    public void ParticipantMintedFromContentIdentity_IsAcquired()
    {
        ImmutableArray<byte> image = Image();
        using var workspace = new InspectionWorkspace();
        AssemblyContextParticipant participant = Participant(image, ContentIdentity(image));
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);

        Assert.Equal(
            image.Length,
            Available(group.UseAssemblyImage(participant.Assembly, view => view.Content.Length)));
    }

    [Fact]
    public void PlaceholderIdentity_IsRejectedRatherThanAcquired()
    {
        ImmutableArray<byte> image = Image();
        using var workspace = new InspectionWorkspace();

        // Acquisition must state the entry's real metadata identity: the workspace validates every
        // image against its descriptor, so a name-only placeholder is refused. This is why a
        // content-shaped acquisition owner has to decode identity before it mints a participant.
        AssemblyContextParticipant participant = Participant(
            image,
            new AssemblyReferenceIdentity(
                typeof(ContentShapedWorkspaceParticipantTests).Assembly.GetName().Name!,
                Version: null,
                Culture: null,
                PublicKeyToken: null));
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);

        var rejected = Assert.IsType<
            AssemblyImageAccessResult<ResolvedAssemblyReference>.Rejected>(
            group.RetainAssemblyReference(participant.Assembly));
        Assert.Equal(CandidateOpenFailureKind.InvalidImage, rejected.Failure.Kind);
    }

    static ImmutableArray<byte> Image() => ImmutableCollectionsMarshal.AsImmutableArray(
        File.ReadAllBytes(
            typeof(ContentShapedWorkspaceParticipantTests).Assembly.Location));

    static AssemblyReferenceIdentity ContentIdentity(ImmutableArray<byte> image)
    {
        using var peReader = new PEReader(image);
        MetadataReader reader = peReader.GetMetadataReader();
        return AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
    }

    static AssemblyContextParticipant Participant(
        ImmutableArray<byte> image,
        AssemblyReferenceIdentity identity) =>
        new(
            ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () => new MemoryStream(ImmutableCollectionsMarshal.AsArray(image)!, writable: false),
                AssemblyResolutionProvenance.Package("probe", "1.0.0", "net11.0", rid: null)),
            ContentBindingPolicy.Instance);

    static TValue Available<TValue>(AssemblyImageAccessResult<TValue> access) =>
        Assert.IsType<AssemblyImageAccessResult<TValue>.Available>(access).Value;

    sealed class ContentBindingPolicy : IAssemblyBindingPolicy
    {
        internal static ContentBindingPolicy Instance { get; } = new();

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelection Select(AssemblyBindingRequest request) =>
            AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.CandidateUnavailable));
    }
}
