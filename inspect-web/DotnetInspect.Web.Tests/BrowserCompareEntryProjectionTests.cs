using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using NuGetFetch;
using DotnetInspect.Web.Interop.Analysis;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserCompareEntryProjectionTests
{
    [Fact]
    public void Projection_PreservesTypedEntryAndClosedResultStates()
    {
        SubjectFixture fixture = CreateFixture();
        BrowserCompareEntryResult available =
            BrowserCompareEntryWireProjection.Project(
                Execute(
                    fixture.Member,
                    "member.compare",
                    ViewFacetAvailability.Available.Instance));

        Assert.Equal(1, available.SchemaVersion);
        Assert.Equal(
            BrowserCompareEntryResultKind.Available,
            available.Kind);
        Assert.Equal("member.compare", available.Entry.Facet);
        Assert.Equal("Compare", available.Entry.Title);
        Assert.Equal(
            BrowserCompareSubjectKind.Member,
            available.Entry.Subject.Kind);
        Assert.Equal(
            "sample.package",
            available.Entry.Subject.Package.PackageId);
        Assert.Equal(
            "Sample",
            available.Entry.Subject.Library.Assembly!.Name);
        Assert.Equal(
            ["Widget"],
            available.Entry.Subject.Type!.Segments);
        Assert.Equal(
            "Run()",
            available.Entry.Subject.Member!.StableSelector);
        Assert.Null(available.Unavailable);
        Assert.Null(available.Failure);

        ViewFacetUnavailableReason reason =
            ViewFacetUnavailableReason.CapabilityAbsent(
                "Compare is unavailable.");
        BrowserCompareEntryResult unavailable =
            BrowserCompareEntryWireProjection.Project(
                Execute(
                    fixture.Type,
                    "type.compare",
                    new ViewFacetAvailability.Unavailable(reason)));
        Assert.Equal(
            BrowserCompareEntryResultKind.Unavailable,
            unavailable.Kind);
        Assert.Equal(
            BrowserCompareEntryUnavailabilityKind.CapabilityAbsent,
            unavailable.Unavailable!.Kind);
        Assert.Equal(
            "Compare is unavailable.",
            unavailable.Unavailable.Message);
        Assert.Null(unavailable.Failure);

        BrowserCompareEntryResult failed =
            BrowserCompareEntryWireProjection.Project(
                Execute(
                    fixture.Library,
                    "library.compare",
                    new ViewFacetAvailability.Failed(
                        "Compare entry projection failed.",
                        new TestDiagnosticEvidence("failed"))));
        Assert.Equal(
            BrowserCompareEntryResultKind.Failed,
            failed.Kind);
        Assert.Equal(
            "Compare entry projection failed.",
            failed.Failure!.Message);
        Assert.Null(failed.Unavailable);

        string json = JsonSerializer.Serialize(
            available,
            BrowserAnalysisJsonContext.Default.BrowserCompareEntryResult);
        Assert.Contains("\"kind\":\"Available\"", json);
        Assert.Contains("\"facet\":\"member.compare\"", json);
    }

    static CompareFacetEntryResult Execute(
        StructuralSubjectIdentity subject,
        string facet,
        ViewFacetAvailability availability)
    {
        var id = new ViewFacetId(facet);
        NavigationLensActivationResult activation =
            NavigationLensActivation.Activate(
                subject,
                new NavigationLensIdentity(subject, id),
                InspectionViewFacetCatalog.Registry,
                new ViewFacetAvailabilitySnapshot(
                [
                    new ViewFacetAvailabilityFact(id, availability),
                ]));
        return CompareFacetEntryExecution.Execute(activation);
    }

    static SubjectFixture CreateFixture()
    {
        RealizedMemberCoordinate.Package coordinate = new(
            "sample.package",
            "1.0.0",
            "nuget-org",
            "net11.0",
            runtimeIdentifier: null);
        var workspaceIdentity = new InspectionWorkspaceIdentity();
        PackageRootBinding binding = Binding(coordinate);
        var occurrence = new WorkspacePackageOccurrence(
            workspaceIdentity,
            new WorkspacePackageDescriptor(binding),
            new PackageArtifactRootCorrespondence(
                workspaceIdentity,
                PackageArtifactRootRequest.From(binding)));
        StructuralSubjectIdentity.WorkspaceSubject workspace =
            StructuralSubjectIdentity.ForWorkspace(workspaceIdentity);
        StructuralSubjectIdentity.PackageSubject package =
            StructuralSubjectIdentity.ForPackage(workspace, occurrence);
        var libraryMember = new WorkspaceContextMember(
            WorkspaceMemberCoordinate.Package(
                coordinate.PackageId,
                coordinate.Version,
                coordinate.Framework,
                coordinate.RuntimeIdentifier),
            coordinate,
            Participant());
        StructuralSubjectIdentity.LibrarySubject library =
            StructuralSubjectIdentity.ForLibrary(package, libraryMember);
        MetadataTypeDefinitionName typeName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Sample",
                    ["Widget"])).Name;
        StructuralSubjectIdentity.TypeSubject type =
            StructuralSubjectIdentity.ForType(library, typeName);
        StructuralSubjectIdentity.MemberSubject member =
            StructuralSubjectIdentity.ForMember(
                type,
                new MemberAnchor(
                    "Run()",
                    "Sample.Widget.Run()",
                    MemberAnchor.ComputeFingerprint(
                        "Sample.Widget.Run()"),
                    "Sample.Widget",
                    "Run"));
        return new(library, type, member);
    }

    static PackageRootBinding Binding(
        RealizedMemberCoordinate.Package coordinate)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(
            bytes,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            using Stream entry = archive.CreateEntry(
                $"lib/{coordinate.Framework}/Sample.dll").Open();
            entry.WriteByte(0);
        }

        return PackageRootBinding.CreateFromSource(
            new AcquiredPackageSourcePayload(
                PackageSourceCoordinate.Create(
                    coordinate.PackageId,
                    coordinate.Version),
                new InMemoryPackageContent(
                    bytes.ToArray(),
                    fromCache: false,
                    producerKey: coordinate.Producer),
                coordinate.Producer,
                PackagePayloadOrigin.Download),
            coordinate.Framework,
            coordinate.RuntimeIdentifier);
    }

    static AssemblyContextParticipant Participant()
    {
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.Create(
                new AssemblyReferenceIdentity(
                    "Sample",
                    new Version(1, 0, 0, 0),
                    Culture: null,
                    PublicKeyToken: null),
                path: null,
                () => new MemoryStream([0], writable: false),
                AssemblyResolutionProvenance.Package(
                    "sample.package",
                    "1.0.0",
                    "net11.0",
                    rid: null));
        return new AssemblyContextParticipant(
            assembly,
            NoResolverAssemblyBindingPolicy.Instance);
    }

    sealed record SubjectFixture(
        StructuralSubjectIdentity.LibrarySubject Library,
        StructuralSubjectIdentity.TypeSubject Type,
        StructuralSubjectIdentity.MemberSubject Member);

    sealed record TestDiagnosticEvidence(string Detail)
        : IViewFacetDiagnosticEvidence;
}
