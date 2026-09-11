using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries.Tests;

public sealed class CompareFacetEntryExecutionTests
{
    [Fact]
    public void Execution_DispatchesEveryCompareTargetAndRetainsExactAncestry()
    {
        SubjectFixture fixture = CreateFixture();
        (StructuralSubjectIdentity Subject, string Facet, Type ResultType)[] cases =
        [
            (
                fixture.Library,
                "library.compare",
                typeof(CompareFacetEntry.Library)),
            (
                fixture.Type,
                "type.compare",
                typeof(CompareFacetEntry.Type)),
            (
                fixture.Member,
                "member.compare",
                typeof(CompareFacetEntry.Member)),
        ];

        foreach ((StructuralSubjectIdentity subject, string facet, Type resultType)
            in cases)
        {
            NavigationLensActivationResult activation =
                Activate(
                    subject,
                    facet,
                    ViewFacetAvailability.Available.Instance);
            CompareFacetEntryResult.Available result =
                Assert.IsType<CompareFacetEntryResult.Available>(
                    CompareFacetEntryExecution.Execute(activation));

            Assert.IsType(resultType, result.Entry);
            Assert.Same(activation.Request, result.Request.Lens);
            Assert.Same(subject, result.Request.Subject);
            Assert.Same(fixture.Context.Workspace, result.Request.Workspace);
            Assert.Same(fixture.Context.Subject, result.Request.Package);
            Assert.Same(
                fixture.Context.Occurrence,
                result.Request.Package.Occurrence);
            Assert.Equal(facet, result.Request.Descriptor.Id.Value);
            Assert.Equal("Compare", result.Request.Descriptor.Title);
        }
    }

    [Fact]
    public void Execution_PreservesUnavailableAndFailedRegistryEvidence()
    {
        SubjectFixture fixture = CreateFixture();
        ViewFacetUnavailableReason reason =
            ViewFacetUnavailableReason.CapabilityAbsent(
                "Compare entry preparation is unavailable.");
        NavigationLensActivationResult unavailableActivation =
            Activate(
                fixture.Type,
                "type.compare",
                new ViewFacetAvailability.Unavailable(reason));

        CompareFacetEntryResult.Unavailable unavailable =
            Assert.IsType<CompareFacetEntryResult.Unavailable>(
                CompareFacetEntryExecution.Execute(
                    unavailableActivation));

        Assert.Same(reason, unavailable.Reason);
        Assert.Same(fixture.Type, unavailable.Request.Subject);
        Assert.Same(fixture.Context.Subject, unavailable.Request.Package);

        var evidence = new TestDiagnosticEvidence("projection failed");
        NavigationLensActivationResult failedActivation =
            Activate(
                fixture.Member,
                "member.compare",
                new ViewFacetAvailability.Failed(
                    "Compare entry preparation failed.",
                    evidence));

        CompareFacetEntryResult.Failed failed =
            Assert.IsType<CompareFacetEntryResult.Failed>(
                CompareFacetEntryExecution.Execute(failedActivation));

        Assert.Equal(
            "Compare entry preparation failed.",
            failed.Message);
        Assert.Same(evidence, failed.Evidence);
        Assert.Same(fixture.Member, failed.Request.Subject);
        Assert.Same(fixture.Context.Subject, failed.Request.Package);
    }

    [Fact]
    public void Execution_RejectsNonCompareAndNonApplicableActivations()
    {
        SubjectFixture fixture = CreateFixture();
        NavigationLensActivationResult api =
            Activate(
                fixture.Type,
                "type.api",
                ViewFacetAvailability.Available.Instance);
        Assert.Throws<ArgumentException>(
            () => CompareFacetEntryExecution.Execute(api));

        NavigationLensActivationResult wrongSubject =
            NavigationLensActivation.Activate(
                fixture.Type,
                new NavigationLensIdentity(
                    fixture.Type,
                    new ViewFacetId("library.compare")),
                InspectionViewFacetCatalog.Registry,
                ThrowingFacts.Instance);
        Assert.IsType<NavigationLensActivationResult.Rejected>(
            wrongSubject);
        Assert.Throws<ArgumentException>(
            () => CompareFacetEntryExecution.Execute(wrongSubject));
    }

    static NavigationLensActivationResult Activate(
        StructuralSubjectIdentity subject,
        string facet,
        ViewFacetAvailability availability)
    {
        var id = new ViewFacetId(facet);
        return NavigationLensActivation.Activate(
            subject,
            new NavigationLensIdentity(subject, id),
            InspectionViewFacetCatalog.Registry,
            new ViewFacetAvailabilitySnapshot(
            [
                new ViewFacetAvailabilityFact(id, availability),
            ]));
    }

    static SubjectFixture CreateFixture()
    {
        RealizedMemberCoordinate.Package coordinate = new(
            "sample.package",
            "1.0.0",
            "nuget-org",
            "net11.0",
            runtimeIdentifier: null);
        StructuralSubjectTestData.PackageContext context =
            StructuralSubjectTestData.Package(coordinate);
        var libraryMember = new WorkspaceContextMember(
            WorkspaceMemberCoordinate.Package(
                coordinate.PackageId,
                coordinate.Version,
                coordinate.Framework,
                coordinate.RuntimeIdentifier),
            coordinate,
            Participant());
        StructuralSubjectIdentity.LibrarySubject library =
            StructuralSubjectIdentity.ForLibrary(
                context.Subject,
                libraryMember);
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
        return new(context, library, type, member);
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
        StructuralSubjectTestData.PackageContext Context,
        StructuralSubjectIdentity.LibrarySubject Library,
        StructuralSubjectIdentity.TypeSubject Type,
        StructuralSubjectIdentity.MemberSubject Member);

    sealed record TestDiagnosticEvidence(string Detail)
        : IViewFacetDiagnosticEvidence;

    sealed class ThrowingFacts : IViewFacetAvailabilityFacts
    {
        public static ThrowingFacts Instance { get; } = new();

        public ViewFacetAvailability Get(ViewFacetId id) =>
            throw new InvalidOperationException(
                $"Availability must not be consulted for '{id.Value}'.");
    }
}
