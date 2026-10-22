using System.Collections.Immutable;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextReferencesQueryTests
{
    [Fact]
    public async Task Execute_ReadsEveryParticipantInOrder()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(
            workspace,
            typeof(AssemblyContextReferencesQueryTests).Assembly.Location);

        AssemblyContextResult<ImmutableArray<AssemblyReferenceIdentity>> result =
            AssemblyContextReferencesQuery.Execute(group);

        var available = Assert.IsType<
            AssemblyContextEntry<ImmutableArray<AssemblyReferenceIdentity>>.Available>(
                Assert.Single(result.Assemblies));
        Assert.NotEmpty(available.Value);
        Assert.Equal(
            group.Participants[0].Assembly.Registration,
            available.Subject.Registration);

        AssemblyContextEntry<ImmutableArray<AssemblyReferenceRow>> rows =
            AssemblyContextReferencesQuery.ExecuteParticipantRows(
                group,
                group.Participants[0]);
        var portable = Assert.IsType<
            AssemblyContextEntry<ImmutableArray<AssemblyReferenceRow>>.Available>(
                rows);
        AssemblyReferenceRow systemRuntime = Assert.Single(
            portable.Value,
            reference => reference.Name == "System.Runtime");
        Assert.False(string.IsNullOrWhiteSpace(systemRuntime.Version));
        Assert.Equal("neutral", systemRuntime.Culture);
        Assert.False(string.IsNullOrWhiteSpace(systemRuntime.PublicKeyToken));
    }

    [Fact]
    public async Task
        ExecuteParticipantBindingEvidence_RetainsExactRequestAndContextDecision()
    {
        var policy = new NoNameOwnerBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(
                        ResolvedAssemblyReference.CreateFromPath(
                            typeof(AssemblyContextReferencesQueryTests)
                                .Assembly.Location,
                            AssemblyResolutionProvenance.Local(
                                "reference binding evidence tests")),
                        policy),
                ]);
        AssemblyContextParticipant participant = group.Participants[0];

        var available = Assert.IsType<
            AssemblyContextEntry<
                ImmutableArray<AssemblyContextReferenceBindingEvidence>>
                .Available>(
                    AssemblyContextReferencesQuery
                        .ExecuteParticipantBindingEvidence(
                            group,
                            participant));

        AssemblyContextReferenceBindingEvidence evidence =
            Assert.Single(
                available.Value,
                candidate =>
                    candidate.Reference.Name == "System.Runtime");
        var target = Assert.IsType<
            AssemblyBindingTarget.AssemblyReference>(
                evidence.Request.Target);
        Assert.Equal(
            evidence.Reference.Name,
            target.Identity.Name);
        Assert.Equal(
            evidence.Reference.Version,
            target.Identity.Version?.ToString());
        var origin = Assert.IsType<
            AssemblyBindingOrigin.RequestingAssembly>(
                evidence.Request.Origin);
        Assert.Same(
            participant.Assembly.Registration,
            origin.Registration);
        Assert.Equal(
            AssemblyResolutionScope.Any,
            evidence.Request.Scope);
        Assert.Same(
            policy.Version,
            evidence.ReferencingContext.Version);
        var missing = Assert.IsType<AssemblyBindingSelection.Missing>(
            evidence.ReferencingContext.Selection);
        Assert.Equal(
            AssemblyBindingMissDisposition.NoNameOwner,
            missing.Disposition);
    }

    [Fact]
    public async Task Execute_CarriesAcquisitionFailureBesideHealthyReferences()
    {
        string path = typeof(AssemblyContextReferencesQueryTests).Assembly.Location;
        byte[] bytes = File.ReadAllBytes(path);
        ResolvedAssemblyReference actual =
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local("available"));
        ResolvedAssemblyReference rejected = ResolvedAssemblyReference.Create(
            actual.Identity with { Name = "WrongIdentity" },
            path: null,
            () => new MemoryStream(bytes, writable: false),
            AssemblyResolutionProvenance.Local("rejected"));
        var policy = new TestBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup(
            [
                new AssemblyContextParticipant(rejected, policy),
                new AssemblyContextParticipant(actual, policy),
            ]);

        AssemblyContextResult<ImmutableArray<AssemblyReferenceIdentity>> result =
            AssemblyContextReferencesQuery.Execute(group);

        var failed = Assert.IsType<
            AssemblyContextEntry<ImmutableArray<AssemblyReferenceIdentity>>.Rejected>(
                result.Assemblies[0]);
        Assert.Equal(CandidateOpenFailureKind.InvalidImage, failed.Failure.Kind);
        Assert.IsType<
            AssemblyContextEntry<ImmutableArray<AssemblyReferenceIdentity>>.Available>(
                result.Assemblies[1]);

        var portableRejected = Assert.IsType<
            AssemblyContextEntry<ImmutableArray<AssemblyReferenceRow>>.Rejected>(
                AssemblyContextReferencesQuery.ExecuteParticipantRows(
                    group,
                    group.Participants[0]));
        AssemblyReferenceRowsRejection rejection =
            AssemblyContextReferencesQuery.GetRowsRejection(portableRejected);
        Assert.Equal(
            AssemblyReferenceRowsRejectionKind.InvalidImage,
            rejection.Kind);
        Assert.Equal(portableRejected.Failure.Detail, rejection.Detail);
        Assert.Equal(
            typeof(AssemblyContextReferencesQuery).Assembly,
            typeof(AssemblyReferenceRowsRejection).Assembly);
    }

    [Fact]
    public async Task ExecuteParticipant_RejectsParticipantFromAnotherGroup()
    {
        string path = typeof(AssemblyContextReferencesQueryTests).Assembly.Location;
        await using var firstWorkspace = new InspectionWorkspace();
        await using var secondWorkspace = new InspectionWorkspace();
        using AssemblyContextGroup first = Group(firstWorkspace, path);
        using AssemblyContextGroup second = Group(secondWorkspace, path);

        Assert.Throws<ArgumentException>(
            () => AssemblyContextReferencesQuery.ExecuteParticipant(
                first,
                second.Participants[0]));
        Assert.Throws<ArgumentException>(
            () => AssemblyContextReferencesQuery
                .ExecuteParticipantBindingEvidence(
                    first,
                    second.Participants[0]));
    }

    static AssemblyContextGroup Group(
        InspectionWorkspace workspace,
        string path)
        => workspace.CreateAssemblyContextGroup(
            [
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.CreateFromPath(
                        path,
                        AssemblyResolutionProvenance.Local("reference query tests")),
                    new TestBindingPolicy()),
            ]);

    sealed class TestBindingPolicy : IAssemblyBindingPolicy
    {
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

    sealed class NoNameOwnerBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request) =>
            new(
                Version,
                AssemblyBindingSelection.NameNotOwned());
    }
}
