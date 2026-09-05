using System.Collections.Immutable;
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
        AssemblyContextParticipant participant = new(
            ResolvedAssemblyReference.CreateFromStreamIfManaged(
                () => Open(image),
                AssemblyResolutionProvenance.Package(
                    "probe",
                    "1.0.0",
                    "net11.0",
                    rid: null))!,
            ContentBindingPolicy.Instance);
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);

        Assert.Null(participant.Assembly.Path);
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

    [Fact]
    public void EquivalentDescriptorIdentity_RetainsAcquiredSnapshot()
    {
        ImmutableArray<byte> image = Image();
        AssemblyReferenceIdentity identity =
            ResolvedAssemblyReference.CreateFromStreamIfManaged(
                () => Open(image),
                AssemblyResolutionProvenance.Package(
                    "probe",
                    "1.0.0",
                    "net11.0",
                    rid: null))!.Identity;
        using var workspace = new InspectionWorkspace();
        AssemblyContextParticipant participant = Participant(
            image,
            identity with
            {
                Name = identity.Name.ToUpperInvariant(),
                Culture = identity.Culture is null ? "neutral" : identity.Culture,
            });
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);

        ResolvedAssemblyReference retained = Available(
            group.RetainAssemblyReference(participant.Assembly));

        Assert.Same(participant.Assembly.Registration, retained.Registration);
    }

    static ImmutableArray<byte> Image() => ImmutableCollectionsMarshal.AsImmutableArray(
        File.ReadAllBytes(
            typeof(ContentShapedWorkspaceParticipantTests).Assembly.Location));

    static AssemblyContextParticipant Participant(
        ImmutableArray<byte> image,
        AssemblyReferenceIdentity identity) =>
        new(
            ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () => Open(image),
                AssemblyResolutionProvenance.Package("probe", "1.0.0", "net11.0", rid: null)),
            ContentBindingPolicy.Instance);

    static MemoryStream Open(ImmutableArray<byte> image) =>
        new(
            ImmutableCollectionsMarshal.AsArray(image)!,
            writable: false);

    static TValue Available<TValue>(AssemblyImageAccessResult<TValue> access) =>
        Assert.IsType<AssemblyImageAccessResult<TValue>.Available>(access).Value;

    sealed class ContentBindingPolicy : IAssemblyBindingPolicy
    {
        internal static ContentBindingPolicy Instance { get; } = new();

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore() =>
                AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                AssemblyBindingFailureKind.CandidateUnavailable));
        }
    }
}
