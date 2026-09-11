using System.Collections.Immutable;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries.Tests;

public sealed class NavigationLensRecommendationTests
{
    [Fact]
    public void LensIdentity_BindsExactStructuralSubjectAndFacet()
    {
        StructuralSubjectIdentity.TypeSubject first = TypeSubject("First");
        StructuralSubjectIdentity.TypeSubject other = TypeSubject("Other");
        ViewFacetId api = new("type.api");

        var identity = new NavigationLensIdentity(first, api);

        Assert.Equal(
            identity,
            new NavigationLensIdentity(first, new ViewFacetId("type.api")));
        Assert.NotEqual(
            identity,
            new NavigationLensIdentity(other, api));
        Assert.NotEqual(
            identity,
            new NavigationLensIdentity(first, new ViewFacetId("type.source")));
        Assert.Equal(
            "root.overview",
            new NavigationLensIdentity(
                first,
                new ViewFacetId("root.overview")).Facet.Value);
    }

    [Fact]
    public void LensOutcome_RetainsRecommendationOrExactRequestBasis()
    {
        StructuralSubjectIdentity.TypeSubject subject = TypeSubject("Widget");
        ImmutableArray<ViewFacetOption> options =
        [
            Option(
                "type.api",
                StructuralSubjectKind.Type,
                100,
                ViewFacetRole.TypeApi,
                ViewFacetAvailability.Available.Instance),
            Option(
                "type.metadata",
                StructuralSubjectKind.Type,
                200,
                role: null,
                ViewFacetAvailability.Available.Instance),
        ];
        NavigationLensOutcome recommended =
            NavigationLensRecommendation.Recommend(subject, options);
        var recommendation = Assert.IsType<
            NavigationLensEvaluationBasis.Recommendation>(
                recommended.Basis);
        Assert.Same(subject, recommendation.Subject);
        Assert.Equal(options, recommendation.Options);
        var equalRecommendation =
            new NavigationLensEvaluationBasis.Recommendation(
                subject,
                ViewFacetRole.TypeApi,
                [.. options]);
        Assert.Equal(recommendation, equalRecommendation);
        Assert.Equal(
            recommendation.GetHashCode(),
            equalRecommendation.GetHashCode());
        Assert.NotEqual(
            recommendation,
            new NavigationLensEvaluationBasis.Recommendation(
                subject,
                ViewFacetRole.TypeApi,
                [options[0]]));
        Assert.NotEqual(
            recommendation,
            new NavigationLensEvaluationBasis.Recommendation(
                subject,
                ViewFacetRole.TypeApi,
                [options[1], options[0]]));
        var hashProbe = new HashProbeEvidence();
        var hashRecommendation =
            new NavigationLensEvaluationBasis.Recommendation(
                subject,
                ViewFacetRole.TypeApi,
                [
                    Option(
                        "type.api",
                        StructuralSubjectKind.Type,
                        100,
                        ViewFacetRole.TypeApi,
                        new ViewFacetAvailability.Failed(
                            "The API view failed.",
                            hashProbe)),
                ]);
        _ = hashRecommendation.GetHashCode();
        Assert.True(hashProbe.HashCalls > 0);
        Assert.Throws<ArgumentException>(
            () => new NavigationLensOutcome.Effective(
                recommendation,
                new NavigationLensIdentity(
                    subject,
                    new ViewFacetId("type.source"))));
        foreach (ViewFacetAvailability availability
            in new ViewFacetAvailability[]
            {
                new ViewFacetAvailability.Unavailable(
                    ViewFacetUnavailableReason.CapabilityAbsent(
                        "The API view is unavailable.")),
                new ViewFacetAvailability.Failed(
                    "The API view failed.",
                    new TestDiagnosticEvidence("api")),
            })
        {
            var nonAvailableRecommendation =
                new NavigationLensEvaluationBasis.Recommendation(
                    subject,
                    ViewFacetRole.TypeApi,
                    [
                        Option(
                            "type.api",
                            StructuralSubjectKind.Type,
                            100,
                            ViewFacetRole.TypeApi,
                            availability),
                    ]);
            Assert.Throws<ArgumentException>(
                () => new NavigationLensOutcome.Effective(
                    nonAvailableRecommendation,
                    new NavigationLensIdentity(
                        subject,
                        new ViewFacetId("type.api"))));
        }

        var request = new NavigationLensIdentity(
            subject,
            new ViewFacetId("type.source"));
        ViewFacetDescriptor requestDescriptor = Option(
            "type.source",
            StructuralSubjectKind.Type,
            200,
            role: null,
            ViewFacetAvailability.Available.Instance).Descriptor;
        var resolution = new ViewFacetResolution.Unavailable(
            requestDescriptor,
            ViewFacetUnavailableReason.CapabilityAbsent(
                "The source view is unavailable."));
        var exact = new NavigationLensEvaluationBasis.ExactRequest(
            request,
            resolution);
        var unavailable = new NavigationLensOutcome.Unavailable(exact);

        Assert.Same(exact, unavailable.Basis);
        Assert.Same(request, exact.Request);
        Assert.Same(resolution, exact.Result);
        Assert.Null(unavailable.EffectiveLens);
        var crossKindRequest = new NavigationLensIdentity(
            subject,
            new ViewFacetId("root.overview"));
        ViewFacetDescriptor crossKindDescriptor = Option(
            "root.overview",
            StructuralSubjectKind.Root,
            300,
            ViewFacetRole.RootOverview,
            ViewFacetAvailability.Available.Instance).Descriptor;
        var crossKindResolution =
            new ViewFacetResolution.Inapplicable(crossKindDescriptor);
        var crossKindExact =
            new NavigationLensEvaluationBasis.ExactRequest(
                crossKindRequest,
                crossKindResolution);
        Assert.Same(crossKindRequest, crossKindExact.Request);
        Assert.Same(crossKindResolution, crossKindExact.Result);
        foreach (ViewFacetResolution crossKindApplicableResult
            in new ViewFacetResolution[]
            {
                new ViewFacetResolution.Available(crossKindDescriptor),
                new ViewFacetResolution.Unavailable(
                    crossKindDescriptor,
                    ViewFacetUnavailableReason.CapabilityAbsent(
                        "The root view is unavailable.")),
                new ViewFacetResolution.Failed(
                    crossKindDescriptor,
                    "The root view failed.",
                    new TestDiagnosticEvidence("root")),
            })
        {
            Assert.Throws<ArgumentException>(
                () => new NavigationLensEvaluationBasis.ExactRequest(
                    crossKindRequest,
                    crossKindApplicableResult));
        }
        ViewFacetDescriptor otherDescriptor = Option(
            "type.metadata",
            StructuralSubjectKind.Type,
            300,
            role: null,
            ViewFacetAvailability.Available.Instance).Descriptor;
        ViewFacetUnavailableReason otherUnavailable =
            ViewFacetUnavailableReason.CapabilityAbsent(
                "The metadata view is unavailable.");
        var otherDiagnostic = new TestDiagnosticEvidence("metadata");
        foreach (ViewFacetResolution otherResult in new ViewFacetResolution[]
        {
            new ViewFacetResolution.Available(otherDescriptor),
            new ViewFacetResolution.Unavailable(
                otherDescriptor,
                otherUnavailable),
            new ViewFacetResolution.Failed(
                otherDescriptor,
                "The metadata view failed.",
                otherDiagnostic),
            new ViewFacetResolution.Inapplicable(otherDescriptor),
        })
        {
            Assert.Throws<ArgumentException>(
                () => new NavigationLensEvaluationBasis.ExactRequest(
                    request,
                    otherResult));
        }

        foreach (ViewFacetResolution nonAvailableResult
            in new ViewFacetResolution[]
            {
                resolution,
                new ViewFacetResolution.Failed(
                    requestDescriptor,
                    "The source view failed.",
                    new TestDiagnosticEvidence("source")),
                new ViewFacetResolution.Inapplicable(requestDescriptor),
                new ViewFacetResolution.Unknown(),
            })
        {
            var nonAvailableExact =
                new NavigationLensEvaluationBasis.ExactRequest(
                    request,
                    nonAvailableResult);
            Assert.Throws<ArgumentException>(
                () => new NavigationLensOutcome.Effective(
                    nonAvailableExact,
                    request));
        }

        var availableExact =
            new NavigationLensEvaluationBasis.ExactRequest(
                request,
                new ViewFacetResolution.Available(requestDescriptor));
        var exactOutcome = new NavigationLensOutcome.Effective(
            availableExact,
            request);
        Assert.Same(request, exactOutcome.EffectiveLens);
    }

    [Fact]
    public void LensRecommendation_UsesPreferredRoleBeforeRegistryOrder()
    {
        foreach ((StructuralSubjectIdentity Subject, ViewFacetRole Role)
            in SubjectsAndRoles())
        {
            StructuralSubjectKind kind = Subject.Kind;
            ImmutableArray<ViewFacetOption> options =
            [
                Option(
                    Id(kind, "earlier"),
                    kind,
                    100,
                    role: null,
                    ViewFacetAvailability.Available.Instance),
                Option(
                    Id(kind, "preferred"),
                    kind,
                    200,
                    Role,
                    ViewFacetAvailability.Available.Instance),
            ];

            NavigationLensOutcome.Effective outcome =
                Assert.IsType<NavigationLensOutcome.Effective>(
                    NavigationLensRecommendation.Recommend(Subject, options));

            Assert.Equal(
                Id(kind, "preferred"),
                outcome.EffectiveLens!.Facet.Value);
            Assert.Same(Subject, outcome.EffectiveLens.Subject);
        }
    }

    [Fact]
    public void LensRecommendation_FallsBackToFirstAvailableInRegistryOrder()
    {
        StructuralSubjectIdentity.TypeSubject subject = TypeSubject("Widget");
        foreach (ViewFacetAvailability preferredAvailability
            in new ViewFacetAvailability[]
        {
            new ViewFacetAvailability.Unavailable(
                ViewFacetUnavailableReason.CapabilityAbsent(
                    "The API view is unavailable.")),
            new ViewFacetAvailability.Failed(
                "The API view could not be prepared.",
                new TestDiagnosticEvidence("api")),
        })
        {
            ImmutableArray<ViewFacetOption> options =
            [
                Option(
                    "type.metadata",
                    StructuralSubjectKind.Type,
                    100,
                    role: null,
                    ViewFacetAvailability.Available.Instance),
                Option(
                    "type.api",
                    StructuralSubjectKind.Type,
                    200,
                    ViewFacetRole.TypeApi,
                    preferredAvailability),
                Option(
                    "type.source",
                    StructuralSubjectKind.Type,
                    300,
                    role: null,
                    ViewFacetAvailability.Available.Instance),
            ];

            NavigationLensOutcome.Effective outcome =
                Assert.IsType<NavigationLensOutcome.Effective>(
                    NavigationLensRecommendation.Recommend(subject, options));

            Assert.Equal(
                "type.metadata",
                outcome.EffectiveLens!.Facet.Value);
        }
    }

    [Fact]
    public void LensRecommendation_ConsumesRegistryOrderWithoutResorting()
    {
        StructuralSubjectIdentity.TypeSubject subject = TypeSubject("Widget");
        ImmutableArray<ViewFacetOption> options =
        [
            Option(
                "type.source",
                StructuralSubjectKind.Type,
                900,
                role: null,
                ViewFacetAvailability.Available.Instance),
            Option(
                "type.api",
                StructuralSubjectKind.Type,
                500,
                ViewFacetRole.TypeApi,
                new ViewFacetAvailability.Unavailable(
                    ViewFacetUnavailableReason.CapabilityAbsent(
                        "The API view is unavailable."))),
            Option(
                "type.metadata",
                StructuralSubjectKind.Type,
                100,
                role: null,
                ViewFacetAvailability.Available.Instance),
        ];

        NavigationLensOutcome.Effective outcome =
            Assert.IsType<NavigationLensOutcome.Effective>(
                NavigationLensRecommendation.Recommend(subject, options));

        Assert.Equal("type.source", outcome.EffectiveLens!.Facet.Value);
    }

    [Fact]
    public void LensRecommendation_RetainsAllRegistryOptionsAndEvidence()
    {
        StructuralSubjectIdentity.TypeSubject subject = TypeSubject("Widget");
        ViewFacetUnavailableReason unavailable =
            ViewFacetUnavailableReason.CapabilityAbsent(
                "The API view is unavailable.");
        var diagnostic = new TestDiagnosticEvidence("metadata");
        ImmutableArray<ViewFacetOption> options =
        [
            Option(
                "type.api",
                StructuralSubjectKind.Type,
                100,
                ViewFacetRole.TypeApi,
                new ViewFacetAvailability.Unavailable(unavailable)),
            Option(
                "type.metadata",
                StructuralSubjectKind.Type,
                200,
                role: null,
                new ViewFacetAvailability.Failed(
                    "Metadata preparation failed.",
                    diagnostic)),
            Option(
                "type.source",
                StructuralSubjectKind.Type,
                300,
                role: null,
                ViewFacetAvailability.Available.Instance),
        ];

        NavigationLensOutcome outcome =
            NavigationLensRecommendation.Recommend(subject, options);
        var basis = Assert.IsType<
            NavigationLensEvaluationBasis.Recommendation>(outcome.Basis);

        Assert.Equal(options, basis.Options);
        Assert.Same(
            unavailable,
            Assert.IsType<ViewFacetAvailability.Unavailable>(
                basis.Options[0].Availability).Reason);
        Assert.Same(
            diagnostic,
            Assert.IsType<ViewFacetAvailability.Failed>(
                basis.Options[1].Availability).Evidence);
    }

    [Fact]
    public void LensRecommendation_MissingPreferredRoleFails()
    {
        StructuralSubjectIdentity.TypeSubject subject = TypeSubject("Widget");
        ImmutableArray<ViewFacetOption> options =
        [
            Option(
                "type.source",
                StructuralSubjectKind.Type,
                100,
                role: null,
                ViewFacetAvailability.Available.Instance),
        ];

        NavigationLensOutcome.Failed outcome =
            Assert.IsType<NavigationLensOutcome.Failed>(
                NavigationLensRecommendation.Recommend(subject, options));
        NavigationLensFailure.Policy failure =
            Assert.IsType<NavigationLensFailure.Policy>(outcome.Failure);

        Assert.Equal(
            NavigationLensPolicyFailureKind.MissingPreferredRole,
            failure.Kind);
        Assert.Null(outcome.EffectiveLens);
    }

    [Fact]
    public void LensRecommendation_EmptyOptionsFails()
    {
        StructuralSubjectIdentity.TypeSubject subject = TypeSubject("Widget");

        NavigationLensOutcome.Failed outcome =
            Assert.IsType<NavigationLensOutcome.Failed>(
                NavigationLensRecommendation.Recommend(
                    subject,
                    []));
        NavigationLensFailure.Policy failure =
            Assert.IsType<NavigationLensFailure.Policy>(outcome.Failure);

        Assert.Equal(
            NavigationLensPolicyFailureKind.EmptyOptions,
            failure.Kind);
        Assert.Empty(
            Assert.IsType<NavigationLensEvaluationBasis.Recommendation>(
                outcome.Basis).Options);
    }

    [Fact]
    public void LensRecommendation_FailedDominatesUnavailableWhenNoOptionIsAvailable()
    {
        StructuralSubjectIdentity.TypeSubject subject = TypeSubject("Widget");
        var diagnostic = new TestDiagnosticEvidence("source");
        ImmutableArray<ViewFacetOption> options =
        [
            Option(
                "type.api",
                StructuralSubjectKind.Type,
                100,
                ViewFacetRole.TypeApi,
                new ViewFacetAvailability.Unavailable(
                    ViewFacetUnavailableReason.CapabilityAbsent(
                        "The API view is unavailable."))),
            Option(
                "type.source",
                StructuralSubjectKind.Type,
                200,
                role: null,
                new ViewFacetAvailability.Failed(
                    "Source preparation failed.",
                    diagnostic)),
        ];

        NavigationLensOutcome.Failed outcome =
            Assert.IsType<NavigationLensOutcome.Failed>(
                NavigationLensRecommendation.Recommend(subject, options));

        Assert.IsType<NavigationLensFailure.RegistryEvaluation>(
            outcome.Failure);
        Assert.Same(
            diagnostic,
            Assert.IsType<ViewFacetAvailability.Failed>(
                Assert.IsType<
                    NavigationLensEvaluationBasis.Recommendation>(
                        outcome.Basis).Options[1].Availability).Evidence);

        var preferredDiagnostic = new TestDiagnosticEvidence("api");
        ImmutableArray<ViewFacetOption> preferredFailedOptions =
        [
            Option(
                "type.api",
                StructuralSubjectKind.Type,
                100,
                ViewFacetRole.TypeApi,
                new ViewFacetAvailability.Failed(
                    "API preparation failed.",
                    preferredDiagnostic)),
            Option(
                "type.source",
                StructuralSubjectKind.Type,
                200,
                role: null,
                new ViewFacetAvailability.Unavailable(
                    ViewFacetUnavailableReason.CapabilityAbsent(
                        "The source view is unavailable."))),
        ];
        NavigationLensOutcome.Failed preferredFailed =
            Assert.IsType<NavigationLensOutcome.Failed>(
                NavigationLensRecommendation.Recommend(
                    subject,
                    preferredFailedOptions));
        Assert.IsType<NavigationLensFailure.RegistryEvaluation>(
            preferredFailed.Failure);
        Assert.Same(
            preferredDiagnostic,
            Assert.IsType<ViewFacetAvailability.Failed>(
                Assert.IsType<
                    NavigationLensEvaluationBasis.Recommendation>(
                        preferredFailed.Basis).Options[0].Availability).Evidence);
    }

    [Fact]
    public void LensRecommendation_AllUnavailableReturnsUnavailable()
    {
        StructuralSubjectIdentity.TypeSubject subject = TypeSubject("Widget");
        ImmutableArray<ViewFacetOption> options =
        [
            Option(
                "type.api",
                StructuralSubjectKind.Type,
                100,
                ViewFacetRole.TypeApi,
                new ViewFacetAvailability.Unavailable(
                    ViewFacetUnavailableReason.CapabilityAbsent(
                        "The API view is unavailable."))),
            Option(
                "type.source",
                StructuralSubjectKind.Type,
                200,
                role: null,
                new ViewFacetAvailability.Unavailable(
                    ViewFacetUnavailableReason.CapabilityAbsent(
                        "The source view is unavailable."))),
        ];

        NavigationLensOutcome.Unavailable outcome =
            Assert.IsType<NavigationLensOutcome.Unavailable>(
                NavigationLensRecommendation.Recommend(subject, options));

        Assert.Null(outcome.EffectiveLens);
        Assert.Equal(
            options,
            Assert.IsType<NavigationLensEvaluationBasis.Recommendation>(
                outcome.Basis).Options);
    }

    [Fact]
    public void MemberRecommendation_UsesMemberOverviewRole()
    {
        StructuralSubjectIdentity.MemberSubject subject = MemberSubject();
        ImmutableArray<ViewFacetOption> options =
        [
            Option(
                "member.source",
                StructuralSubjectKind.Member,
                100,
                role: null,
                ViewFacetAvailability.Available.Instance),
            Option(
                "member.overview",
                StructuralSubjectKind.Member,
                200,
                ViewFacetRole.MemberOverview,
                ViewFacetAvailability.Available.Instance),
        ];

        NavigationLensOutcome.Effective outcome =
            Assert.IsType<NavigationLensOutcome.Effective>(
                NavigationLensRecommendation.Recommend(subject, options));

        Assert.Equal("member.overview", outcome.EffectiveLens!.Facet.Value);
    }

    static IEnumerable<(
        StructuralSubjectIdentity Subject,
        ViewFacetRole Role)> SubjectsAndRoles()
    {
        yield return (
            StructuralSubjectIdentity.ForRoot(PackageCoordinate()),
            ViewFacetRole.PackageOverview);
        yield return (
            StructuralSubjectIdentity.ForRoot(PlatformCoordinate()),
            ViewFacetRole.RootOverview);
        yield return (
            StructuralSubjectIdentity.ForRoot(EmbeddedCoordinate()),
            ViewFacetRole.RootOverview);
        yield return (
            StructuralSubjectIdentity.ForAllLibraries(PackageCoordinate()),
            ViewFacetRole.LibraryReferences);
        yield return (
            StructuralSubjectIdentity.ForLibrary(
                Library(PackageCoordinate())),
            ViewFacetRole.LibraryReferences);
        yield return (TypeSubject("Widget"), ViewFacetRole.TypeApi);
        yield return (MemberSubject(), ViewFacetRole.MemberOverview);
    }

    static ViewFacetOption Option(
        string id,
        StructuralSubjectKind kind,
        int order,
        ViewFacetRole? role,
        ViewFacetAvailability availability) =>
        new(
            new ViewFacetDescriptor(
                new ViewFacetId(id),
                kind,
                id,
                $"Summary for {id}.",
                order,
                role),
            availability);

    static string Id(
        StructuralSubjectKind kind,
        string name) =>
        $"{kind.ToString().ToLowerInvariant()}.{name}";

    static StructuralSubjectIdentity.TypeSubject TypeSubject(string name)
    {
        RealizedMemberCoordinate.Package coordinate = PackageCoordinate();
        return StructuralSubjectIdentity.ForType(
            StructuralSubjectIdentity.ForLibrary(Library(coordinate)),
            TypeName("Sample", name));
    }

    static StructuralSubjectIdentity.MemberSubject MemberSubject()
    {
        StructuralSubjectIdentity.TypeSubject type = TypeSubject("Widget");
        return StructuralSubjectIdentity.ForMember(
            type,
            new MemberAnchor(
                "Run()",
                "Sample.Widget.Run()",
                MemberAnchor.ComputeFingerprint("Sample.Widget.Run()"),
                "Sample.Widget",
                "Run"));
    }

    static RealizedMemberCoordinate.Package PackageCoordinate() =>
        new(
            "sample.package",
            "1.0.0",
            "nuget-org",
            "net11.0",
            runtimeIdentifier: null);

    static RealizedMemberCoordinate.Platform PlatformCoordinate() =>
        new(
            "runtime",
            "11.0.0",
            "fixture",
            "net11.0",
            assembly: null);

    static RealizedMemberCoordinate.Embedded EmbeddedCoordinate() =>
        new(
            "lib/sample.dll",
            new string('a', 64),
            "Sample");

    static WorkspaceContextMember Library(
        RealizedMemberCoordinate.Package coordinate)
    {
        ResolvedAssemblyReference assembly = ResolvedAssemblyReference.Create(
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
        return new WorkspaceContextMember(
            WorkspaceMemberCoordinate.Package(
                coordinate.PackageId,
                coordinate.Version,
                coordinate.Framework,
                coordinate.RuntimeIdentifier),
            coordinate,
            new AssemblyContextParticipant(
                assembly,
                NoResolverAssemblyBindingPolicy.Instance));
    }

    static MetadataTypeDefinitionName TypeName(
        string @namespace,
        string name) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                @namespace,
                [name])).Name;

    sealed record TestDiagnosticEvidence(string Detail) :
        IViewFacetDiagnosticEvidence;

    sealed class HashProbeEvidence : IViewFacetDiagnosticEvidence
    {
        public int HashCalls { get; private set; }

        public override int GetHashCode()
        {
            HashCalls++;
            return 1;
        }
    }
}
