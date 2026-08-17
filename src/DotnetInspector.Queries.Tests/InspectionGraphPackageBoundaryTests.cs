using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class InspectionGraphPackageBoundaryTests
{
    [Fact]
    public void MixedLens_UsesOneTypedPackageAsNodeAndGroup()
    {
        RealizedMemberCoordinate.Package package =
            Package("sample.package", "feed-a");
        WorkspaceContextMember first =
            PackageMember(package, "Sample.First");
        WorkspaceContextMember second =
            PackageMember(package, "Sample.Second");

        InspectionGraphPackageBoundary boundary =
            InspectionGraphPackageBoundary.Create([first, second]);
        InspectionGraphDocument document = boundary.Project(
            InspectionGraphPackageBoundaryLens.Mixed);

        Assert.Equal(InspectionGraphDocumentScope.SessionBound, document.Scope);
        Assert.Equal(3, document.Nodes.Length);
        InspectionGraphGroup group = Assert.Single(document.Groups);
        Assert.Same(document.Nodes[0].Subject, group.Subject);
        Assert.IsType<InspectionGraphSubject.PackageSubject>(
            document.Nodes[0].Subject);
        Assert.All(
            document.Nodes.Skip(1),
            node =>
            {
                Assert.Equal([group.Id], node.GroupIds);
                var subject =
                    Assert.IsType<InspectionGraphSubject.AssemblySubject>(
                        node.Subject);
                Assert.IsType<InspectionGraphAssemblyIdentity.Acquired>(
                    subject.Identity);
            });

        Assert.True(
            boundary.TryGetPackageSubject(
                first.Participant.Assembly.Registration,
                out InspectionGraphSubject.PackageSubject? firstOwner));
        Assert.True(
            boundary.TryGetPackageSubject(
                second.Participant.Assembly.Registration,
                out InspectionGraphSubject.PackageSubject? secondOwner));
        Assert.Same(group.Subject, firstOwner);
        Assert.Same(firstOwner, secondOwner);
    }

    [Fact]
    public void PackageNodesLens_IsPortableAndPreservesProducerIdentity()
    {
        RealizedMemberCoordinate.Package firstPackage =
            Package("sample.package", "feed-a");
        RealizedMemberCoordinate.Package secondPackage =
            Package("sample.package", "feed-b");

        InspectionGraphDocument document =
            InspectionGraphPackageBoundary.Create(
                [
                    PackageMember(
                        firstPackage,
                        "Sample",
                        assemblyVersion: new Version(1, 0)),
                    PackageMember(
                        secondPackage,
                        "Sample",
                        assemblyVersion: new Version(1, 0)),
                ])
            .Project(InspectionGraphPackageBoundaryLens.PackageNodes);

        Assert.Equal(InspectionGraphDocumentScope.Portable, document.Scope);
        Assert.Equal(2, document.Nodes.Length);
        Assert.Empty(document.Groups);
        Assert.NotEqual(
            document.Nodes[0].Subject,
            document.Nodes[1].Subject);
        Assert.All(
            document.Nodes,
            node => Assert.True(node.Subject.IsPortable));
    }

    [Fact]
    public void PackageSeed_BindsToNodeOrGroupSelectedByLens()
    {
        RealizedMemberCoordinate.Package package =
            Package("sample.package", "feed-a");
        WorkspaceContextMember member =
            PackageMember(package, "Sample");
        InspectionGraphPackageBoundary boundary =
            InspectionGraphPackageBoundary.Create([member]);
        Assert.True(
            boundary.TryGetPackageSubject(
                member.Participant.Assembly.Registration,
                out InspectionGraphSubject.PackageSubject? subject));
        InspectionGraphModeRequest request =
            InspectionGraphModeRequest.SingleSeed(subject);

        InspectionGraphDocument packageNodes = boundary.Project(
            InspectionGraphPackageBoundaryLens.PackageNodes,
            request);
        InspectionGraphSeed nodeSeed =
            Assert.Single(packageNodes.Seeds);
        Assert.Equal(
            InspectionGraphTargetKind.Node,
            nodeSeed.Target.Kind);
        Assert.Same(
            packageNodes.Nodes[nodeSeed.Target.Id].Subject,
            nodeSeed.Subject);

        InspectionGraphDocument packageGroups = boundary.Project(
            InspectionGraphPackageBoundaryLens.PackageGroups,
            request);
        InspectionGraphSeed groupSeed =
            Assert.Single(packageGroups.Seeds);
        Assert.Equal(
            InspectionGraphTargetKind.Group,
            groupSeed.Target.Kind);
        Assert.Same(
            packageGroups.Groups[groupSeed.Target.Id].Subject,
            groupSeed.Subject);

        InspectionGraphDocument mixed = boundary.Project(
            InspectionGraphPackageBoundaryLens.Mixed,
            request);
        Assert.Equal(
            InspectionGraphTargetKind.Node,
            Assert.Single(mixed.Seeds).Target.Kind);
    }

    [Fact]
    public void PackageGroupsLens_DoesNotCollapseMatchingAssemblyMetadata()
    {
        RealizedMemberCoordinate.Package firstPackage =
            Package("sample.package", "feed-a");
        RealizedMemberCoordinate.Package secondPackage =
            Package("sample.package", "feed-b");

        InspectionGraphDocument document =
            InspectionGraphPackageBoundary.Create(
                [
                    PackageMember(firstPackage, "Sample"),
                    PackageMember(secondPackage, "Sample"),
                ])
            .Project(InspectionGraphPackageBoundaryLens.PackageGroups);

        Assert.Equal(2, document.Nodes.Length);
        Assert.Equal(2, document.Groups.Length);
        Assert.NotEqual(
            document.Nodes[0].Subject,
            document.Nodes[1].Subject);
        Assert.Equal([0], document.Nodes[0].GroupIds);
        Assert.Equal([1], document.Nodes[1].GroupIds);
    }

    [Fact]
    public void PackageGroupsLens_KeepsNonPackageArtifactsUngrouped()
    {
        RealizedMemberCoordinate.Package package =
            Package("sample.package", "feed-a");
        WorkspaceContextMember embedded = EmbeddedMember("Embedded.Sample");

        InspectionGraphPackageBoundary boundary =
            InspectionGraphPackageBoundary.Create(
                [PackageMember(package, "Package.Sample"), embedded]);
        InspectionGraphDocument document = boundary.Project(
            InspectionGraphPackageBoundaryLens.PackageGroups);

        Assert.Equal(2, document.Nodes.Length);
        Assert.Single(document.Groups);
        Assert.Equal([0], document.Nodes[0].GroupIds);
        Assert.Empty(document.Nodes[1].GroupIds);
        Assert.False(
            boundary.TryGetPackageSubject(
                embedded.Participant.Assembly.Registration,
                out _));
    }

    [Theory]
    [InlineData("other.package", "1.0.0")]
    [InlineData("sample.package", "2.0.0")]
    public void Create_RejectsPackageCoordinateThatConflictsWithProvenance(
        string packageId,
        string version)
    {
        RealizedMemberCoordinate.Package package =
            Package("sample.package", "feed-a");
        WorkspaceContextMember member = PackageMember(
            package,
            "Sample",
            provenancePackageId: packageId,
            provenanceVersion: version);

        Assert.Throws<ArgumentException>(
            () => InspectionGraphPackageBoundary.Create([member]));
    }

    [Fact]
    public void Create_KeepsEffectiveAndPhysicalPackageTargetsDistinct()
    {
        var package = new RealizedMemberCoordinate.Package(
            "sample.package",
            "1.0.0",
            "feed-a",
            "net11.0",
            "linux-x64");
        WorkspaceContextMember member = PackageMember(
            package,
            "Sample",
            provenanceFramework: "net10.0",
            provenanceRuntimeIdentifier: "linux-musl-x64");

        InspectionGraphPackageBoundary boundary =
            InspectionGraphPackageBoundary.Create([member]);
        InspectionGraphDocument document = boundary.Project(
            InspectionGraphPackageBoundaryLens.PackageNodes);

        var subject =
            Assert.IsType<InspectionGraphSubject.PackageSubject>(
                Assert.Single(document.Nodes).Subject);
        var identity =
            Assert.IsType<InspectionGraphPackageIdentity.Realized>(
                subject.Identity);
        Assert.Same(package, identity.Package);
        var provenance =
            Assert.IsType<AssemblyResolutionProvenance.PackageAsset>(
                member.Participant.Assembly.Provenance);
        Assert.Equal("net10.0", provenance.Tfm);
        Assert.Equal("linux-musl-x64", provenance.Rid);
    }

    [Fact]
    public void Create_RejectsConflictingOwnershipForOneAcquisition()
    {
        RealizedMemberCoordinate.Package firstPackage =
            Package("sample.package", "feed-a");
        WorkspaceContextMember first =
            PackageMember(firstPackage, "Sample");
        RealizedMemberCoordinate.Package secondPackage =
            Package("other.package", "feed-a");
        var second = new WorkspaceContextMember(
            WorkspaceMemberCoordinate.Package(
                secondPackage.PackageId,
                secondPackage.Version,
                secondPackage.Framework),
            secondPackage,
            first.Participant);

        Assert.Throws<ArgumentException>(
            () => InspectionGraphPackageBoundary.Create([first, second]));
    }

    [Fact]
    public void Create_RejectsLoadedContextThatOmitsAParticipant()
    {
        RealizedMemberCoordinate.Package package =
            Package("sample.package", "feed-a");
        WorkspaceContextMember first =
            PackageMember(package, "Sample.First");
        WorkspaceContextMember second =
            PackageMember(package, "Sample.Second");
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [first.Participant, second.Participant]);
        var loaded = new WorkspaceContextLoadOutcome.Loaded(
            group,
            [first],
            package.Framework,
            package.RuntimeIdentifier);

        Assert.Throws<ArgumentException>(
            () => InspectionGraphPackageBoundary.Create(loaded));
    }

    static RealizedMemberCoordinate.Package Package(
        string packageId,
        string producer) =>
        new(
            packageId,
            "1.0.0",
            producer,
            "net11.0",
            null);

    static WorkspaceContextMember PackageMember(
        RealizedMemberCoordinate.Package package,
        string assemblyName,
        Version? assemblyVersion = null,
        string? provenancePackageId = null,
        string? provenanceVersion = null,
        string? provenanceFramework = null,
        string? provenanceRuntimeIdentifier = null)
    {
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.Create(
                new AssemblyReferenceIdentity(
                    assemblyName,
                    assemblyVersion ?? new Version(1, 0),
                    null,
                    null),
                path: null,
                static () => new MemoryStream([]),
                AssemblyResolutionProvenance.Package(
                    provenancePackageId ?? package.PackageId,
                    provenanceVersion ?? package.Version,
                    provenanceFramework ?? package.Framework,
                    provenanceRuntimeIdentifier
                        ?? package.RuntimeIdentifier));
        return new WorkspaceContextMember(
            WorkspaceMemberCoordinate.Package(
                package.PackageId,
                package.Version,
                package.Framework,
                package.RuntimeIdentifier),
            package,
            new AssemblyContextParticipant(
                assembly,
                NoResolverAssemblyBindingPolicy.Instance));
    }

    static WorkspaceContextMember EmbeddedMember(string assemblyName)
    {
        const string ContentRef = "fixtures/embedded.dll";
        const string Digest =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.Create(
                new AssemblyReferenceIdentity(
                    assemblyName,
                    new Version(1, 0),
                    null,
                    null),
                path: null,
                static () => new MemoryStream([]),
                AssemblyResolutionProvenance.Embedded(
                    ContentRef,
                    Digest,
                    assemblyName));
        return new WorkspaceContextMember(
            WorkspaceMemberCoordinate.Embedded(
                ContentRef,
                Digest,
                assemblyName),
            new RealizedMemberCoordinate.Embedded(
                ContentRef,
                Digest,
                assemblyName),
            new AssemblyContextParticipant(
                assembly,
                NoResolverAssemblyBindingPolicy.Instance));
    }
}
