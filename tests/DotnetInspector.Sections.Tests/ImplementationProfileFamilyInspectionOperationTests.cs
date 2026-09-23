using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

using DotnetInspector.Fixtures;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections.Tests;

public sealed class ImplementationProfileFamilyInspectionOperationTests
{
    [Fact]
    public async Task Execute_ReturnsCompletedEnvelopeWithNonProjectableShare()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        InspectionEnvelope<
            AssemblyContextEntry<
                AssemblyImplementationProfileFamilyInspection>> envelope =
                    ImplementationProfileFamilyInspectionOperation.Execute(
                        group,
                        participant,
                        Selection(group, participant));

        var available = Assert.IsType<
            AssemblyContextEntry<
                AssemblyImplementationProfileFamilyInspection>.Available>(
                    envelope.Content);
        Assert.Equal(4, available.Value.Members.Length);
        Assert.NotEmpty(available.Value.Profiles);
        var share =
            Assert.IsType<InspectionShare.NonProjectable>(
                envelope.Share);
        Assert.Equal("implementation-profile-family/share", share.Path);
        Assert.Null(share.FullUrl);
        Assert.Null(share.Packet);
    }

    [Fact]
    public async Task Execute_PreservesFailedSelectionAsDiagnostic()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        ImplementationProfileFamilySelection selection =
            Selection(group, participant);

        InspectionEnvelope<
            AssemblyContextEntry<
                AssemblyImplementationProfileFamilyInspection>> envelope =
                    ImplementationProfileFamilyInspectionOperation.Execute(
                        group,
                        participant,
                        new(
                            selection.TypeDefinitionId,
                            [
                                selection.StableSelectors[0],
                                "Analyze~missing",
                            ]));

        _ = Assert.IsType<
            AssemblyContextEntry<
                AssemblyImplementationProfileFamilyInspection>.Failed>(
                    envelope.Content);
        InspectionDiagnostic diagnostic = Assert.Single(
            envelope.Diagnostics);
        Assert.Equal(
            "implementation-profiles.participant-failed",
            diagnostic.Code);
        Assert.Equal(
            InspectionDiagnosticSeverity.Error,
            diagnostic.Severity);
    }

    [Fact]
    public async Task Execute_PreservesRejectedParticipantAsDiagnostic()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup validGroup = Group(workspace);
        AssemblyContextParticipant validParticipant =
            Assert.Single(validGroup.Participants);
        ImplementationProfileFamilySelection selection =
            Selection(validGroup, validParticipant);

        using AssemblyContextGroup rejectedGroup =
            Group(workspace, mutateIdentity: true);
        InspectionEnvelope<
            AssemblyContextEntry<
                AssemblyImplementationProfileFamilyInspection>> envelope =
                    ImplementationProfileFamilyInspectionOperation.Execute(
                        rejectedGroup,
                        Assert.Single(rejectedGroup.Participants),
                        selection);

        _ = Assert.IsType<
            AssemblyContextEntry<
                AssemblyImplementationProfileFamilyInspection>.Rejected>(
                    envelope.Content);
        InspectionDiagnostic diagnostic = Assert.Single(
            envelope.Diagnostics);
        Assert.Equal(
            "implementation-profiles.participant-rejected",
            diagnostic.Code);
        Assert.Equal(
            InspectionDiagnosticSeverity.Warning,
            diagnostic.Severity);
    }

    static ImplementationProfileFamilySelection Selection(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant)
    {
        AssemblyApiSurface surface =
            Assert.IsType<
                AssemblyContextEntry<AssemblyApiSurface>.Available>(
                    AssemblyContextApiSurfaceQuery.ExecuteParticipant(
                        group,
                        participant))
                .Value;
        ApiType type = Assert.Single(
            surface.Surface.Types,
            type => type.Name == "ImplementationProfileSample");
        return new(
            AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(type),
            type.Members
                .Where(member => member.Name == "Analyze")
                .Select(member =>
                    ApiMemberIdentity
                        .GetMemberAnchor(type, member)
                        .StableSelector));
    }

    static AssemblyContextGroup Group(
        InspectionWorkspace workspace,
        bool mutateIdentity = false)
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                File.ReadAllBytes(
                    FixtureCatalog.AnalysisCallerLoop.AssemblyPath()));
        using var reader = new PEReader(image);
        AssemblyReferenceIdentity identity =
            AssemblyReferenceIdentity.FromAssemblyDefinition(
                reader.GetMetadataReader());
        if (mutateIdentity)
            identity = identity with { Name = "WrongIdentity" };
        var participant = new AssemblyContextParticipant(
            ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () => new MemoryStream(
                    ImmutableCollectionsMarshal.AsArray(image)!,
                    writable: false),
                AssemblyResolutionProvenance.Local(
                    "implementation-profile-family-operation-fixture")),
            new MissingBindingPolicy());
        return workspace.CreateAssemblyContextGroup([participant]);
    }

    sealed class MissingBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } =
            new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request) =>
            new(
                Version,
                AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.CandidateUnavailable)));
    }
}
