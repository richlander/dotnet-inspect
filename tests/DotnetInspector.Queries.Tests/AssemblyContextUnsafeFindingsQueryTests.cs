using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextUnsafeFindingsQueryTests
{
    [Fact]
    public async Task ExecuteParticipant_AttributesUngradedFindingsToPublicMembers()
    {
        var policy = new UnresolvedBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        ImmutableArray<byte> image = SelfImage();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
            [
                Participant(
                    image,
                    ContentIdentity(image),
                    policy),
            ]);

        AssemblyContextEntry<AssemblyUnsafeFindings> entry =
            AssemblyContextUnsafeFindingsQuery.ExecuteParticipant(
                group,
                Assert.Single(group.Participants));

        AssemblyUnsafeFindings result =
            Assert.IsType<
                AssemblyContextEntry<
                    AssemblyUnsafeFindings>.Available>(entry)
                .Value;
        AssemblyUnsafeFinding publicFinding =
            Assert.Single(
                result.Findings,
                finding =>
                    finding.PublicMember?.Member
                    == nameof(
                        ResearchProjectionProbe
                            .InvokeFunctionPointer)
                    && finding.Finding.SafetyKind
                    == "Unsafe signature");
        Assert.Equal(
            typeof(ResearchProjectionProbe).FullName,
            publicFinding.PublicMember!.TypeDefinitionId);
        Assert.StartsWith(
            $"{nameof(ResearchProjectionProbe.InvokeFunctionPointer)}~",
            publicFinding.PublicMember.StableSelector);
        Assert.Equal(
            nameof(ResearchProjectionProbe.InvokeFunctionPointer),
            publicFinding.PublicMember.BodyMember);
        Assert.False(
            string.IsNullOrWhiteSpace(
                publicFinding.PublicMember.BodySelector));
        Assert.Equal(
            publicFinding.Finding.Method.MetadataToken,
            publicFinding.PublicMember.BodyToken);
        Assert.Equal(
            SafetyFactLocation.Declaration,
            publicFinding.Finding.Location);
        Assert.Equal(
            typeof(ResearchProjectionProbe)
                .GetMethod(
                    nameof(
                        ResearchProjectionProbe
                            .InvokeFunctionPointer))!
                .MetadataToken,
            publicFinding.Finding.Method.MetadataToken);
        Assert.True(result.NonPublicFindings > 0);
        Assert.Equal(result.Findings.Length, result.TotalFindings);
        Assert.Equal(
            result.Findings
                .OrderBy(
                    finding =>
                        finding.PublicMember?.TypeDefinitionId
                            ?? finding.Finding.Method.DeclaringType
                                .ToQualifiedDisplayString(),
                    StringComparer.Ordinal)
                .ThenBy(
                    finding =>
                        finding.PublicMember?.StableSelector
                            ?? finding.Finding.Method.Name,
                    StringComparer.Ordinal)
                .ThenBy(
                    finding =>
                        finding.Finding.Method.MetadataToken)
                .ThenBy(
                    finding =>
                        finding.Finding.ILOffset ?? -1)
                .ThenBy(
                    finding =>
                        finding.Finding.SafetyKind,
                    StringComparer.Ordinal),
            result.Findings);
    }

    [Fact]
    public async Task ExecuteParticipant_AttributesLiftedUnsafeBodyToPublicSource()
    {
        var policy = new UnresolvedBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                File.ReadAllBytes(
                    FixtureCatalog.DecompilerClassicAsync
                        .AssemblyPath()));
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
            [
                Participant(
                    image,
                    ContentIdentity(image),
                    policy),
            ]);

        AssemblyUnsafeFindings result =
            Assert.IsType<
                AssemblyContextEntry<
                    AssemblyUnsafeFindings>.Available>(
                        AssemblyContextUnsafeFindingsQuery
                            .ExecuteParticipant(
                                group,
                                Assert.Single(group.Participants)))
                .Value;
        AssemblyUnsafeFinding stackAllocation =
            Assert.Single(
                result.Findings,
                finding =>
                    finding.PublicMember?.Member
                        == "AwaitInitializedStackalloc"
                    && finding.Finding.SafetyKind
                        == "stackalloc");

        Assert.Equal(
            "MoveNext",
            stackAllocation.PublicMember!.BodyMember);
        Assert.Equal(
            stackAllocation.Finding.Method.MetadataToken,
            stackAllocation.PublicMember.BodyToken);
        Assert.False(
            string.IsNullOrWhiteSpace(
                stackAllocation.PublicMember.BodySelector));
        Assert.NotEqual(
            stackAllocation.PublicMember.StableSelector,
            stackAllocation.PublicMember.BodySelector);

        AssemblyContextEntry<AssemblyMemberProjection> projectionEntry =
            AssemblyContextMemberProjectionQuery.ExecuteParticipant(
                group,
                Assert.Single(group.Participants),
                new AssemblyContextMemberProjectionRequest(
                    stackAllocation.PublicMember.TypeDefinitionId,
                    stackAllocation.PublicMember.BodyMember,
                    MethodToken:
                        stackAllocation.PublicMember.BodyToken,
                    SourceDocument: true,
                    FactRows: true,
                    FindingEvidence: true,
                    AnalysisFeatures:
                        LibraryBodyAnalysisFeatures.Default));
        if (projectionEntry is
            AssemblyContextEntry<AssemblyMemberProjection>.Failed failed)
        {
            throw failed.Error;
        }
        AssemblyMemberProjection projection =
            Assert.IsType<
                AssemblyContextEntry<
                    AssemblyMemberProjection>.Available>(
                        projectionEntry)
                .Value;
        Assert.Equal(
            stackAllocation.PublicMember.BodyToken,
            projection.Projection.SelectedMethodToken);
        Assert.NotNull(projection.Projection.SourceDocument);
    }

    [Fact]
    public async Task ExecuteParticipant_PreservesSelectedParticipantFailure()
    {
        var policy = new UnresolvedBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        ImmutableArray<byte> image = SelfImage();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
            [
                Participant(
                    image,
                    ContentIdentity(image) with
                    {
                        Name = "WrongIdentity",
                    },
                    policy),
            ]);

        AssemblyContextEntry<AssemblyUnsafeFindings> entry =
            AssemblyContextUnsafeFindingsQuery.ExecuteParticipant(
                group,
                Assert.Single(group.Participants));

        var rejected =
            Assert.IsType<
                AssemblyContextEntry<
                    AssemblyUnsafeFindings>.Rejected>(entry);
        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            rejected.Failure.Kind);
    }

    static AssemblyContextParticipant Participant(
        ImmutableArray<byte> image,
        AssemblyReferenceIdentity identity,
        IAssemblyBindingPolicy policy) =>
        new(
            ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () => new MemoryStream(
                    ImmutableCollectionsMarshal.AsArray(image)!,
                    writable: false),
                AssemblyResolutionProvenance.Package(
                    "unsafe-findings-probe",
                    "1.0.0",
                    "net11.0",
                    rid: null)),
            policy);

    static ImmutableArray<byte> SelfImage() =>
        ImmutableCollectionsMarshal.AsImmutableArray(
            File.ReadAllBytes(
                typeof(
                    AssemblyContextUnsafeFindingsQueryTests)
                    .Assembly.Location));

    static AssemblyReferenceIdentity ContentIdentity(
        ImmutableArray<byte> image)
    {
        using var reader = new PEReader(image);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            reader.GetMetadataReader());
    }

    sealed class UnresolvedBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } =
            new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request) =>
            new(
                Version,
                AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind
                            .CandidateUnavailable)));
    }
}
