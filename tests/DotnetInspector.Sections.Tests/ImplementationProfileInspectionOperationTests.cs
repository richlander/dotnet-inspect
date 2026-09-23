using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections.Tests;

public sealed class ImplementationProfileInspectionOperationTests
{
    [Fact]
    public async Task Execute_ReturnsCompletedEnvelopeWithNonProjectableShare()
    {
        byte[] content = File.ReadAllBytes(
            typeof(ImplementationProfileInspectionOperationTests)
                .Assembly.Location);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, content);

        InspectionEnvelope<
            AssemblyContextEntry<AssemblyImplementationProfileInspection>>
            envelope = ImplementationProfileInspectionOperation.Execute(
                group,
                Assert.Single(group.Participants));

        var available = Assert.IsType<
            AssemblyContextEntry<
                AssemblyImplementationProfileInspection>.Available>(
                    envelope.Content);
        Assert.NotEmpty(available.Value.Profiles);
        var share =
            Assert.IsType<InspectionShare.NonProjectable>(
                envelope.Share);
        Assert.Equal("implementation-profiles/share", share.Path);
        Assert.Null(share.FullUrl);
        Assert.Null(share.Packet);
    }

    [Fact]
    public async Task Execute_PreservesRejectedParticipantAsDiagnostic()
    {
        byte[] content = File.ReadAllBytes(
            typeof(ImplementationProfileInspectionOperationTests)
                .Assembly.Location);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(
            workspace,
            content,
            mutateIdentity: true);

        InspectionEnvelope<
            AssemblyContextEntry<AssemblyImplementationProfileInspection>>
            envelope = ImplementationProfileInspectionOperation.Execute(
                group,
                Assert.Single(group.Participants));

        _ = Assert.IsType<
            AssemblyContextEntry<
                AssemblyImplementationProfileInspection>.Rejected>(
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

    static AssemblyContextGroup Group(
        InspectionWorkspace workspace,
        byte[] content,
        bool mutateIdentity = false)
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(content);
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
                    "implementation-profile-operation-fixture")),
            new MissingBindingPolicy());
        return workspace.CreateAssemblyContextGroup([participant]);
    }

    sealed class MissingBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } =
            new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.CandidateUnavailable)));
        }
    }
}
