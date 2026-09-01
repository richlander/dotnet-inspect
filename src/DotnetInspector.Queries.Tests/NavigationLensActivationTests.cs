using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries.Tests;

public sealed class NavigationLensActivationTests
{
    [Fact]
    public void StandaloneLensActivation_RejectsDifferentExactSubjectBeforeRegistryResolution()
    {
        AssemblyContextParticipant participant = Participant();
        StructuralSubjectIdentity.TypeSubject active =
            TypeSubject("Active", participant);
        StructuralSubjectIdentity.TypeSubject equal =
            TypeSubject("Active", participant);
        StructuralSubjectIdentity.TypeSubject requested =
            TypeSubject("Requested", participant);
        Assert.NotSame(active, equal);
        Assert.Equal(active, equal);

        ViewFacetDescriptor availableDescriptor = Descriptor(
            "type.api",
            StructuralSubjectKind.Type);
        ViewFacetRegistry availableRegistry = Registry(availableDescriptor);
        var availableFacts = new ViewFacetAvailabilitySnapshot(
        [
            new ViewFacetAvailabilityFact(
                availableDescriptor.Id,
                ViewFacetAvailability.Available.Instance),
        ]);
        NavigationLensActivationResult.Applied equalResult =
            Assert.IsType<NavigationLensActivationResult.Applied>(
                NavigationLensActivation.Activate(
                    active,
                    new NavigationLensIdentity(
                        equal,
                        availableDescriptor.Id),
                    availableRegistry,
                    availableFacts));
        Assert.Same(equal, equalResult.Request.Subject);

        var request = new NavigationLensIdentity(
            requested,
            new ViewFacetId("type.api"));
        ViewFacetRegistry registry = Registry(
            availableDescriptor,
            _ => throw new InvalidOperationException(
                "Registry applicability must not run."));

        NavigationLensActivationResult.Rejected result =
            Assert.IsType<NavigationLensActivationResult.Rejected>(
                NavigationLensActivation.Activate(
                    active,
                    request,
                    registry,
                    ThrowingFacts.Instance));
        NavigationLensRejection.SubjectMismatch rejection =
            Assert.IsType<NavigationLensRejection.SubjectMismatch>(
                result.Rejection);

        Assert.Same(request, result.Request);
        Assert.Same(active, rejection.ActiveSubject);
    }

    [Fact]
    public void ExplicitLensResolution_MapsEveryRegistryOutcomeWithoutFallback()
    {
        ActivationFixture fixture = CreateFixture();

        NavigationLensActivationResult.Applied available =
            Assert.IsType<NavigationLensActivationResult.Applied>(
                fixture.Activate("type.available"));
        Assert.Equal(
            "type.available",
            available.Outcome.EffectiveLens!.Facet.Value);

        NavigationLensActivationResult.Unavailable unavailable =
            Assert.IsType<NavigationLensActivationResult.Unavailable>(
                fixture.Activate("type.unavailable"));
        Assert.Null(unavailable.Outcome.EffectiveLens);
        Assert.Equal(
            "type.unavailable",
            Assert.IsType<NavigationLensEvaluationBasis.ExactRequest>(
                unavailable.Outcome.Basis).Request.Facet.Value);

        NavigationLensActivationResult.Failed failed =
            Assert.IsType<NavigationLensActivationResult.Failed>(
                fixture.Activate("type.failed"));
        Assert.Null(failed.Outcome.EffectiveLens);
        Assert.Equal(
            "type.failed",
            Assert.IsType<NavigationLensEvaluationBasis.ExactRequest>(
                failed.Outcome.Basis).Request.Facet.Value);
        Assert.IsType<NavigationLensFailure.RegistryEvaluation>(
            failed.Outcome.Failure);

        NavigationLensActivationResult.Rejected inapplicable =
            Assert.IsType<NavigationLensActivationResult.Rejected>(
                fixture.Activate("root.inapplicable"));
        Assert.IsType<ViewFacetResolution.Inapplicable>(
            Assert.IsType<NavigationLensRejection.Registry>(
                inapplicable.Rejection).Result);

        NavigationLensActivationResult.Rejected unknown =
            Assert.IsType<NavigationLensActivationResult.Rejected>(
                fixture.Activate("type.unknown"));
        Assert.IsType<ViewFacetResolution.Unknown>(
            Assert.IsType<NavigationLensRejection.Registry>(
                unknown.Rejection).Result);

        StructuralSubjectIdentity.RootSubject root =
            StructuralSubjectIdentity.ForRoot(PackageCoordinate());
        ViewFacetDescriptor rootDescriptor = Descriptor(
            "root.overview",
            StructuralSubjectKind.Root);
        ViewFacetRegistry rootRegistry = Registry(
            rootDescriptor,
            target =>
                target.RootKind == ViewFacetRootKind.PackageCapable);
        var rootFacts = new ViewFacetAvailabilitySnapshot(
        [
            new ViewFacetAvailabilityFact(
                rootDescriptor.Id,
                ViewFacetAvailability.Available.Instance),
        ]);
        NavigationLensActivationResult.Applied rootResult =
            Assert.IsType<NavigationLensActivationResult.Applied>(
                NavigationLensActivation.Activate(
                    root,
                    new NavigationLensIdentity(root, rootDescriptor.Id),
                    rootRegistry,
                    rootFacts));
        Assert.Same(root, rootResult.Outcome.EffectiveLens!.Subject);
    }

    [Fact]
    public void ExplicitLensResolution_RetainsExactRegistryEvidence()
    {
        ActivationFixture fixture = CreateFixture();

        NavigationLensActivationResult.Applied available =
            Assert.IsType<NavigationLensActivationResult.Applied>(
                fixture.Activate("type.available"));
        AssertExactBasis(
            fixture,
            available.Request,
            available.Outcome.Basis,
            fixture.Available);

        NavigationLensActivationResult.Unavailable unavailable =
            Assert.IsType<NavigationLensActivationResult.Unavailable>(
                fixture.Activate("type.unavailable"));
        NavigationLensEvaluationBasis.ExactRequest unavailableBasis =
            AssertExactBasis(
                fixture,
                unavailable.Request,
                unavailable.Outcome.Basis,
                fixture.Unavailable);
        Assert.Same(
            fixture.UnavailableReason,
            Assert.IsType<ViewFacetResolution.Unavailable>(
                unavailableBasis.Result).Reason);

        NavigationLensActivationResult.Failed failed =
            Assert.IsType<NavigationLensActivationResult.Failed>(
                fixture.Activate("type.failed"));
        NavigationLensEvaluationBasis.ExactRequest failedBasis =
            AssertExactBasis(
                fixture,
                failed.Request,
                failed.Outcome.Basis,
                fixture.Failed);
        Assert.Same(
            fixture.Diagnostic,
            Assert.IsType<ViewFacetResolution.Failed>(
                failedBasis.Result).Evidence);

        foreach ((string Id, ViewFacetDescriptor? Descriptor) rejected in
            new[]
            {
                ("root.inapplicable", fixture.Inapplicable),
                ("type.unknown", null),
            })
        {
            NavigationLensActivationResult.Rejected result =
                Assert.IsType<NavigationLensActivationResult.Rejected>(
                    fixture.Activate(rejected.Id));
            ViewFacetResolution resolution =
                Assert.IsType<NavigationLensRejection.Registry>(
                    result.Rejection).Basis.Result;

            Assert.Same(fixture.Subject, result.Request.Subject);
            Assert.Equal(rejected.Id, result.Request.Facet.Value);
            if (rejected.Descriptor is null)
            {
                Assert.IsType<ViewFacetResolution.Unknown>(resolution);
            }
            else
            {
                Assert.Same(
                    rejected.Descriptor,
                    Assert.IsType<ViewFacetResolution.Inapplicable>(
                        resolution).Descriptor);
            }
        }
    }

    static NavigationLensEvaluationBasis.ExactRequest AssertExactBasis(
        ActivationFixture fixture,
        NavigationLensIdentity request,
        NavigationLensEvaluationBasis basis,
        ViewFacetDescriptor descriptor)
    {
        var exact =
            Assert.IsType<NavigationLensEvaluationBasis.ExactRequest>(basis);
        Assert.Same(request, exact.Request);
        ViewFacetDescriptor actual = exact.Result switch
        {
            ViewFacetResolution.Available available =>
                available.Descriptor,
            ViewFacetResolution.Unavailable unavailable =>
                unavailable.Descriptor,
            ViewFacetResolution.Failed failed =>
                failed.Descriptor,
            _ => throw new Xunit.Sdk.XunitException(
                "Expected an applicable Registry result."),
        };
        Assert.Same(descriptor, actual);
        Assert.Same(fixture.Subject, exact.Subject);
        return exact;
    }

    static ActivationFixture CreateFixture()
    {
        StructuralSubjectIdentity.TypeSubject subject = TypeSubject("Widget");
        ViewFacetDescriptor available = Descriptor(
            "type.available",
            StructuralSubjectKind.Type,
            order: 100);
        ViewFacetDescriptor unavailable = Descriptor(
            "type.unavailable",
            StructuralSubjectKind.Type,
            order: 200);
        ViewFacetDescriptor failed = Descriptor(
            "type.failed",
            StructuralSubjectKind.Type,
            order: 300);
        ViewFacetDescriptor fallback = Descriptor(
            "type.fallback",
            StructuralSubjectKind.Type,
            order: 400);
        ViewFacetDescriptor inapplicable = Descriptor(
            "root.inapplicable",
            StructuralSubjectKind.Root,
            order: 100);
        ViewFacetUnavailableReason unavailableReason =
            ViewFacetUnavailableReason.CapabilityAbsent(
                "The requested view is unavailable.");
        var diagnostic = new TestDiagnosticEvidence("failed");
        ViewFacetRegistry registry = Registry(
        [
            Registration(available),
            Registration(unavailable),
            Registration(failed),
            Registration(fallback),
            Registration(inapplicable),
        ]);
        var facts = new ViewFacetAvailabilitySnapshot(
        [
            new ViewFacetAvailabilityFact(
                available.Id,
                ViewFacetAvailability.Available.Instance),
            new ViewFacetAvailabilityFact(
                unavailable.Id,
                new ViewFacetAvailability.Unavailable(unavailableReason)),
            new ViewFacetAvailabilityFact(
                failed.Id,
                new ViewFacetAvailability.Failed(
                    "The requested view failed.",
                    diagnostic)),
            new ViewFacetAvailabilityFact(
                fallback.Id,
                ViewFacetAvailability.Available.Instance),
        ]);
        return new(
            subject,
            registry,
            facts,
            available,
            unavailable,
            failed,
            inapplicable,
            unavailableReason,
            diagnostic);
    }

    static ViewFacetRegistry Registry(
        ViewFacetDescriptor descriptor,
        Func<ViewFacetTarget, bool>? applies = null) =>
        Registry([Registration(descriptor, applies)]);

    static ViewFacetRegistry Registry(
        IEnumerable<ViewFacetRegistration> registrations)
    {
        ViewFacetRegistration[] values = [.. registrations];
        return new(
            values,
            values.Select(registration =>
                Assert.IsType<ViewFacetRegistration.Active>(
                    registration).Binding));
    }

    static ViewFacetRegistration.Active Registration(
        ViewFacetDescriptor descriptor,
        Func<ViewFacetTarget, bool>? applies = null) =>
        new(
            descriptor,
            descriptor.Summary,
            applies ?? (target => target.Subject.Kind == descriptor.Kind),
            new ViewFacetExecutionBinding(descriptor.Id, descriptor),
            (_, facts) => facts.Get(descriptor.Id));

    static ViewFacetDescriptor Descriptor(
        string id,
        StructuralSubjectKind kind,
        int order = 100) =>
        new(
            new ViewFacetId(id),
            kind,
            id,
            $"Summary for {id}.",
            order);

    static RealizedMemberCoordinate.Package PackageCoordinate() =>
        new(
            "sample.package",
            "1.0.0",
            "nuget-org",
            "net11.0",
            runtimeIdentifier: null);

    static AssemblyContextParticipant Participant()
    {
        ResolvedAssemblyReference assembly = ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity(
                "Library",
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

    static StructuralSubjectIdentity.TypeSubject TypeSubject(
        string name,
        AssemblyContextParticipant? participant = null)
    {
        RealizedMemberCoordinate.Package coordinate = PackageCoordinate();
        var library = new WorkspaceContextMember(
            WorkspaceMemberCoordinate.Package(
                coordinate.PackageId,
                coordinate.Version,
                coordinate.Framework,
                coordinate.RuntimeIdentifier),
            coordinate,
            participant ?? Participant());
        MetadataTypeDefinitionName type =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Sample",
                    [name])).Name;
        return StructuralSubjectIdentity.ForType(
            StructuralSubjectIdentity.ForLibrary(library),
            type);
    }

    sealed record ActivationFixture(
        StructuralSubjectIdentity.TypeSubject Subject,
        ViewFacetRegistry Registry,
        IViewFacetAvailabilityFacts Facts,
        ViewFacetDescriptor Available,
        ViewFacetDescriptor Unavailable,
        ViewFacetDescriptor Failed,
        ViewFacetDescriptor Inapplicable,
        ViewFacetUnavailableReason UnavailableReason,
        TestDiagnosticEvidence Diagnostic)
    {
        public NavigationLensActivationResult Activate(string facet) =>
            NavigationLensActivation.Activate(
                Subject,
                new NavigationLensIdentity(
                    Subject,
                    new ViewFacetId(facet)),
                Registry,
                Facts);
    }

    sealed record TestDiagnosticEvidence(string Value)
        : IViewFacetDiagnosticEvidence;

    sealed class ThrowingFacts : IViewFacetAvailabilityFacts
    {
        public static ThrowingFacts Instance { get; } = new();

        public ViewFacetAvailability Get(ViewFacetId id) =>
            throw new InvalidOperationException(
                $"Facts must not be read for '{id.Value}'.");
    }
}
