using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries.Tests;

public sealed class ViewFacetRegistryTests
{
    [Fact]
    public void Catalog_IsCompleteUniqueAndDeterministicallyOrdered()
    {
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        ViewFacetDescriptor[] descriptors = [.. registry.Descriptors];

        Assert.NotEmpty(descriptors);
        Assert.Equal(
            descriptors.Length,
            descriptors.Select(descriptor => descriptor.Id).Distinct().Count());
        Assert.Equal(
            descriptors,
            descriptors.OrderBy(descriptor => descriptor.Kind)
                .ThenBy(descriptor => descriptor.Order));
        Assert.Equal(
            [
                StructuralSubjectKind.Root,
                StructuralSubjectKind.Library,
                StructuralSubjectKind.Type,
                StructuralSubjectKind.Member,
            ],
            descriptors.Select(descriptor => descriptor.Kind).Distinct());
        Assert.All(
            descriptors,
            descriptor =>
            {
                Assert.InRange(
                    descriptor.Id.Value.Length,
                    1,
                    ViewFacetId.MaximumLength);
                Assert.True(
                    ViewFacetId.TryGetKind(
                        descriptor.Id.Value,
                        out StructuralSubjectKind prefix));
                Assert.Equal(descriptor.Kind, prefix);
                Assert.False(string.IsNullOrWhiteSpace(descriptor.Title));
                Assert.False(string.IsNullOrWhiteSpace(descriptor.Summary));
            });
        Assert.All(
            descriptors.GroupBy(descriptor => descriptor.Kind),
            group => Assert.Equal(
                group.Count(),
                group.Select(descriptor => descriptor.Order).Distinct().Count()));
        Assert.Equal(
            Enum.GetValues<ViewFacetRole>(),
            descriptors.Where(descriptor => descriptor.Role is not null)
                .Select(descriptor => descriptor.Role!.Value)
                .Order());
        Assert.All(
            descriptors.Where(descriptor => descriptor.Role is not null)
                .GroupBy(descriptor => descriptor.Kind),
            group => Assert.Equal(
                group.Count(),
                group.Select(descriptor => descriptor.Role).Distinct().Count()));

        string maximum =
            $"root.a{new string('a', ViewFacetId.MaximumLength - 6)}";
        Assert.Equal(
            ViewFacetId.MaximumLength,
            new ViewFacetId(maximum).Value.Length);
        Assert.Throws<ArgumentException>(
            () => new ViewFacetId($"{maximum}a"));
        Assert.Throws<ArgumentException>(
            () => new ViewFacetId("root.overview\n"));
        Assert.Throws<ArgumentException>(
            () => new ViewFacetId("Root.overview"));
        Assert.Throws<ArgumentException>(
            () => new ViewFacetId("root.overview "));
        Assert.Throws<ArgumentException>(
            () => new ViewFacetId("root.-overview"));
    }

    [Fact]
    public void RegistrationsAndBindingsAgree()
    {
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        ViewFacetRegistration[] registrations = [.. registry.Registrations];
        ViewFacetRegistration.Active[] active =
        [
            .. registrations.OfType<ViewFacetRegistration.Active>(),
        ];
        ViewFacetRegistration.Tombstone[] tombstones =
        [
            .. registrations.OfType<ViewFacetRegistration.Tombstone>(),
        ];

        Assert.Equal(
            registry.Descriptors.Select(descriptor => descriptor.Id).OrderBy(Id),
            active.Select(registration => registration.Descriptor.Id)
                .Concat(
                    tombstones.Select(registration =>
                        registration.Descriptor.Id))
                .OrderBy(Id));
        Assert.Equal(
            active.Select(registration => registration.Descriptor.Id)
                .OrderBy(Id),
            registry.ActiveBindings.Select(binding => binding.Id).OrderBy(Id));
        Assert.All(
            active,
            registration =>
            {
                ViewFacetExecutionBinding binding = Assert.Single(
                    registry.ActiveBindings,
                    candidate => candidate.Id == registration.Descriptor.Id);
                Assert.Same(binding, registration.Binding);
                Assert.NotNull(binding.Target);
            });
        Assert.All(tombstones, registration => Assert.Null(registration.Binding));

        ViewFacetRegistration.Tombstone synthetic = Tombstone(
            Descriptor(
                "root.retired",
                StructuralSubjectKind.Root,
                100),
            AppliesToPackageRoot);
        ViewFacetRegistry syntheticRegistry = Registry([synthetic]);
        ViewFacetOption option = Assert.Single(
            syntheticRegistry.Discover(
                RootTarget(ViewFacetRootKind.PackageCapable),
                ThrowingFacts.Instance));
        ViewFacetAvailability.Unavailable unavailable =
            Assert.IsType<ViewFacetAvailability.Unavailable>(
                option.Availability);
        Assert.Equal(
            ViewFacetUnavailabilityKind.Retired,
            unavailable.Reason.Kind);
        Assert.Empty(syntheticRegistry.ActiveBindings);

        Assert.Throws<ArgumentException>(
            () => new ViewFacetRegistry(
                [Active(
                    Descriptor(
                        "root.active",
                        StructuralSubjectKind.Root,
                        100),
                    AppliesToPackageRoot)],
                []));
        ViewFacetDescriptor invalidActive =
            Descriptor(
                "root.invalid-active",
                StructuralSubjectKind.Root,
                200);
        ViewFacetRegistry invalidActiveRegistry = Registry(
        [
            new ViewFacetRegistration.Active(
                invalidActive,
                invalidActive.Summary,
                AppliesToPackageRoot,
                new ViewFacetExecutionBinding(
                    invalidActive.Id,
                    new TestExecutionTarget(invalidActive.Id)),
                (_, _) => new ViewFacetAvailability.Unavailable(
                    ViewFacetUnavailableReason.Retired())),
        ]);
        Assert.Throws<InvalidOperationException>(
            () => invalidActiveRegistry.Discover(
                RootTarget(ViewFacetRootKind.PackageCapable),
                ThrowingFacts.Instance));
    }

    [Fact]
    public void Tombstone_PreservesApplicabilityAndReturnsRetired()
    {
        ViewFacetRegistration.Tombstone tombstone = Tombstone(
            Descriptor(
                "root.retired",
                StructuralSubjectKind.Root,
                100),
            AppliesToPackageRoot);
        ViewFacetRegistry registry = Registry([tombstone]);

        ViewFacetOption option = Assert.Single(
            registry.Discover(
                RootTarget(ViewFacetRootKind.PackageCapable),
                ThrowingFacts.Instance));
        ViewFacetAvailability.Unavailable unavailable =
            Assert.IsType<ViewFacetAvailability.Unavailable>(
                option.Availability);
        Assert.Equal(
            ViewFacetUnavailabilityKind.Retired,
            unavailable.Reason.Kind);
        Assert.Empty(
            registry.Discover(
                RootTarget(ViewFacetRootKind.NonPackage),
                ThrowingFacts.Instance));
        Assert.IsType<ViewFacetResolution.Inapplicable>(
            registry.Resolve(
                "root.retired",
                RootTarget(ViewFacetRootKind.NonPackage),
                ThrowingFacts.Instance));
    }

    [Fact]
    public void StaticDiscovery_DoesNotExecuteOrAcquire()
    {
        var sentinels = new NoWorkSentinels();
        ViewFacetDescriptor descriptor =
            Descriptor("library.static", StructuralSubjectKind.Library, 100);
        var registration = new ViewFacetRegistration.Active(
            descriptor,
            descriptor.Summary,
            AppliesToLibrary,
            new ViewFacetExecutionBinding(
                descriptor.Id,
                new TestExecutionTarget(descriptor.Id)),
            (_, _) =>
            {
                sentinels.TouchAll();
                return ViewFacetAvailability.Available.Instance;
            });
        ViewFacetRegistry registry = Registry([registration]);

        Assert.Equal(
            registry.Descriptors,
            Enum.GetValues<StructuralSubjectKind>()
                .SelectMany(kind => registry.Discover(kind)));
        Assert.All(
            registry.Registrations,
            registration => Assert.NotNull(registration.Descriptor));
        sentinels.AssertUntouched();
    }

    [Fact]
    public void TargetDiscovery_PreservesOrderAndFailureEvidence()
    {
        var evidence = new TestDiagnosticEvidence("decode failed");
        ViewFacetUnavailableReason capabilityAbsent =
            ViewFacetUnavailableReason.CapabilityAbsent(
                "The required analysis capability is absent.");
        ViewFacetDescriptor available =
            Descriptor("library.available", StructuralSubjectKind.Library, 100);
        ViewFacetDescriptor empty =
            Descriptor("library.empty", StructuralSubjectKind.Library, 200);
        ViewFacetDescriptor unavailable =
            Descriptor("library.unavailable", StructuralSubjectKind.Library, 300);
        ViewFacetDescriptor retired =
            Descriptor("library.retired", StructuralSubjectKind.Library, 400);
        ViewFacetDescriptor failed =
            Descriptor("library.failed", StructuralSubjectKind.Library, 500);
        ViewFacetDescriptor inapplicable =
            Descriptor("root.inapplicable", StructuralSubjectKind.Root, 100);
        var sentinels = new NoWorkSentinels();
        var inapplicableRegistration = new ViewFacetRegistration.Active(
            inapplicable,
            inapplicable.Summary,
            AppliesToPackageRoot,
            new ViewFacetExecutionBinding(
                inapplicable.Id,
                new TestExecutionTarget(inapplicable.Id)),
            (_, _) =>
            {
                sentinels.TouchAll();
                return ViewFacetAvailability.Available.Instance;
            });
        ViewFacetRegistry registry = Registry(
        [
            Active(failed, AppliesToLibrary),
            Tombstone(retired, AppliesToLibrary),
            Active(unavailable, AppliesToLibrary),
            Active(empty, AppliesToLibrary),
            Active(available, AppliesToLibrary),
            inapplicableRegistration,
        ]);
        ViewFacetAvailabilitySnapshot facts = new(
        [
            Fact(available, ViewFacetAvailability.Available.Instance),
            Fact(empty, ViewFacetAvailability.Available.Instance),
            Fact(
                unavailable,
                new ViewFacetAvailability.Unavailable(capabilityAbsent)),
            Fact(
                failed,
                new ViewFacetAvailability.Failed(
                    "The facet could not be prepared.",
                    evidence)),
        ]);

        ViewFacetOption[] options =
        [
            .. registry.Discover(LibraryTarget(), facts),
        ];

        Assert.Equal(
            [
                available.Id,
                empty.Id,
                unavailable.Id,
                retired.Id,
                failed.Id,
            ],
            options.Select(option => option.Descriptor.Id));
        Assert.IsType<ViewFacetAvailability.Available>(
            options[0].Availability);
        Assert.IsType<ViewFacetAvailability.Available>(
            options[1].Availability);
        Assert.Same(
            capabilityAbsent,
            Assert.IsType<ViewFacetAvailability.Unavailable>(
                options[2].Availability).Reason);
        Assert.Equal(
            ViewFacetUnavailabilityKind.Retired,
            Assert.IsType<ViewFacetAvailability.Unavailable>(
                options[3].Availability).Reason.Kind);
        Assert.Same(
            evidence,
            Assert.IsType<ViewFacetAvailability.Failed>(
                options[4].Availability).Evidence);
        sentinels.AssertUntouched();
    }

    [Fact]
    public void Lookup_DistinguishesEveryOutcome()
    {
        var evidence = new TestDiagnosticEvidence("query failed");
        ViewFacetUnavailableReason capabilityAbsent =
            ViewFacetUnavailableReason.CapabilityAbsent(
                "The required capability is absent.");
        ViewFacetDescriptor available =
            Descriptor("library.available", StructuralSubjectKind.Library, 100);
        ViewFacetDescriptor unavailable =
            Descriptor("library.unavailable", StructuralSubjectKind.Library, 200);
        ViewFacetDescriptor failed =
            Descriptor("library.failed", StructuralSubjectKind.Library, 300);
        ViewFacetDescriptor wrongSubject =
            Descriptor("root.wrong-subject", StructuralSubjectKind.Root, 100);
        var sentinels = new NoWorkSentinels();
        var wrongSubjectRegistration = new ViewFacetRegistration.Active(
            wrongSubject,
            wrongSubject.Summary,
            AppliesToPackageRoot,
            new ViewFacetExecutionBinding(
                wrongSubject.Id,
                new TestExecutionTarget(wrongSubject.Id)),
            (_, _) =>
            {
                sentinels.TouchAll();
                return ViewFacetAvailability.Available.Instance;
            });
        ViewFacetRegistry registry = Registry(
        [
            Active(available, AppliesToLibrary),
            Active(unavailable, AppliesToLibrary),
            Active(failed, AppliesToLibrary),
            wrongSubjectRegistration,
        ]);
        ViewFacetAvailabilitySnapshot facts = new(
        [
            Fact(available, ViewFacetAvailability.Available.Instance),
            Fact(
                unavailable,
                new ViewFacetAvailability.Unavailable(capabilityAbsent)),
            Fact(
                failed,
                new ViewFacetAvailability.Failed(
                    "The facet could not be prepared.",
                    evidence)),
        ]);
        ViewFacetTarget target = LibraryTarget();

        Assert.True(
            registry.TryGetDescriptor(
                "library.available",
                out ViewFacetDescriptor? knownDescriptor));
        Assert.Same(available, knownDescriptor);
        Assert.False(
            registry.TryGetDescriptor(
                "library.unknown",
                out ViewFacetDescriptor? validUnknownDescriptor));
        Assert.Null(validUnknownDescriptor);
        Assert.False(
            registry.TryGetDescriptor(
                "library.unknown\n",
                out ViewFacetDescriptor? invalidUnknownDescriptor));
        Assert.Null(invalidUnknownDescriptor);
        Assert.IsType<ViewFacetResolution.Available>(
            registry.Resolve("library.available", target, facts));
        ViewFacetResolution.Unavailable unavailableResult =
            Assert.IsType<ViewFacetResolution.Unavailable>(
                registry.Resolve("library.unavailable", target, facts));
        Assert.Same(capabilityAbsent, unavailableResult.Reason);
        ViewFacetResolution.Failed failedResult =
            Assert.IsType<ViewFacetResolution.Failed>(
                registry.Resolve("library.failed", target, facts));
        Assert.Same(evidence, failedResult.Evidence);
        Assert.IsType<ViewFacetResolution.Inapplicable>(
            registry.Resolve(
                "root.wrong-subject",
                target,
                ThrowingFacts.Instance));
        Assert.IsType<ViewFacetResolution.Unknown>(
            registry.Resolve(
                "library.unknown",
                target,
                ThrowingFacts.Instance));
        Assert.IsType<ViewFacetResolution.Unknown>(
            registry.Resolve(
                "library.unknown\n",
                target,
                ThrowingFacts.Instance));
        sentinels.AssertUntouched();
    }

    [Fact]
    public void RootApplicability_PartitionsPackageAndNonPackageFacets()
    {
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        ViewFacetTarget package =
            RootTarget(ViewFacetRootKind.PackageCapable);
        ViewFacetTarget nonPackage =
            RootTarget(ViewFacetRootKind.NonPackage);
        ViewFacetAvailabilitySnapshot facts = AllAvailable(registry);

        Assert.Equal(
            [
                "root.package-overview",
                "root.package-dependencies",
            ],
            registry.Discover(package, facts)
                .Select(option => option.Descriptor.Id.Value));
        Assert.Equal(
            ["root.overview"],
            registry.Discover(nonPackage, facts)
                .Select(option => option.Descriptor.Id.Value));
        Assert.IsType<ViewFacetResolution.Inapplicable>(
            registry.Resolve("root.overview", package, ThrowingFacts.Instance));
        Assert.IsType<ViewFacetResolution.Inapplicable>(
            registry.Resolve(
                "root.package-overview",
                nonPackage,
                ThrowingFacts.Instance));
        Assert.IsType<ViewFacetResolution.Inapplicable>(
            registry.Resolve(
                "root.package-dependencies",
                nonPackage,
                ThrowingFacts.Instance));
    }

    [Fact]
    public void InitialInspectionLensInventory_MatchesContract()
    {
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        ExpectedFacet[] expected =
        [
            new("root.package-overview", StructuralSubjectKind.Root, "Overview",
                "Package identity, selected target, assets, and summary facts.",
                100, ViewFacetRole.PackageOverview, ViewFacetRootKind.PackageCapable),
            new("root.package-dependencies", StructuralSubjectKind.Root, "Dependencies",
                "Declared package dependencies for the selected target framework.",
                200, null, ViewFacetRootKind.PackageCapable),
            new("root.overview", StructuralSubjectKind.Root, "Overview",
                "Coordinate identity, selected target, and available structural subjects.",
                300, ViewFacetRole.RootOverview, ViewFacetRootKind.NonPackage),
            new("library.references", StructuralSubjectKind.Library, "References",
                "Direct assembly references for the active Library.",
                100, ViewFacetRole.LibraryReferences),
            new("library.integrations", StructuralSubjectKind.Library, "Integrations",
                "Framework and ecosystem integrations found in the active Library.",
                200),
            new("library.opportunities", StructuralSubjectKind.Library, "Opportunities",
                "Framework and ecosystem integrations the active Library could adopt.",
                300),
            new("library.analysis", StructuralSubjectKind.Library, "Analysis",
                "Static analysis findings and code characteristics for the active Library.",
                400),
            new("library.metadata", StructuralSubjectKind.Library, "Metadata",
                "Physical ECMA-335 metadata and PE structure for the active Library.",
                500),
            new("type.api", StructuralSubjectKind.Type, "API",
                "API shape and member inventory for the active Type.",
                100, ViewFacetRole.TypeApi),
            new("type.metadata", StructuralSubjectKind.Type, "Metadata",
                "Metadata records and attributes for the active Type.",
                200),
            new("type.source", StructuralSubjectKind.Type, "Source",
                "Source or decompiled code for the active Type.",
                300),
            new("member.overview", StructuralSubjectKind.Member, "Overview",
                "Signature, documentation, and overload context for the active Member.",
                100, ViewFacetRole.MemberOverview),
            new("member.call-graph", StructuralSubjectKind.Member, "Call graph",
                "Incoming and outgoing calls for the active Member.",
                200),
            new("member.facts", StructuralSubjectKind.Member, "Facts",
                "Metadata, IL, safety, and analysis facts for the active Member.",
                300),
            new("member.source", StructuralSubjectKind.Member, "Source",
                "Source or decompiled code for the active Member.",
                400),
            new("member.annotated-source", StructuralSubjectKind.Member, "Annotated source",
                "Source for the active Member with product analysis annotations.",
                500),
        ];

        Assert.Equal(
            expected.Select(item => item.Descriptor),
            registry.Descriptors.Select(descriptor => new DescriptorShape(
                descriptor.Id.Value,
                descriptor.Kind,
                descriptor.Title,
                descriptor.Summary,
                descriptor.Order,
                descriptor.Role)));
        Assert.Equal(
            new (string Id, InspectionViewFacetExecution Target)[]
            {
                ("root.package-overview",
                    InspectionViewFacetExecution.PackageOverview),
                ("root.package-dependencies",
                    InspectionViewFacetExecution.PackageDependencies),
                ("root.overview",
                    InspectionViewFacetExecution.RootOverview),
                ("library.references",
                    InspectionViewFacetExecution.LibraryReferences),
                ("library.integrations",
                    InspectionViewFacetExecution.LibraryIntegrations),
                ("library.opportunities",
                    InspectionViewFacetExecution.LibraryOpportunities),
                ("library.analysis",
                    InspectionViewFacetExecution.LibraryAnalysis),
                ("library.metadata",
                    InspectionViewFacetExecution.LibraryMetadata),
                ("type.api",
                    InspectionViewFacetExecution.TypeApi),
                ("type.metadata",
                    InspectionViewFacetExecution.TypeMetadata),
                ("type.source",
                    InspectionViewFacetExecution.TypeSource),
                ("member.overview",
                    InspectionViewFacetExecution.MemberOverview),
                ("member.call-graph",
                    InspectionViewFacetExecution.MemberCallGraph),
                ("member.facts",
                    InspectionViewFacetExecution.MemberFacts),
                ("member.source",
                    InspectionViewFacetExecution.MemberSource),
                ("member.annotated-source",
                    InspectionViewFacetExecution.MemberAnnotatedSource),
            },
            registry.ActiveBindings.Select(binding => (
                binding.Id.Value,
                Assert.IsType<InspectionViewFacetExecution>(
                    binding.Target))));

        ViewFacetAvailabilitySnapshot facts = AllAvailable(registry);
        foreach (ExpectedFacet item in expected)
        {
            ViewFacetRegistration registration = Assert.Single(
                registry.Registrations,
                candidate => candidate.Descriptor.Id.Value == item.Id);
            Assert.Equal(item.Summary, registration.Purpose);
            foreach (ViewFacetTarget target in Targets())
            {
                bool expectedApplicability =
                    target.Subject.Kind == item.Kind
                    && (item.Kind != StructuralSubjectKind.Root
                        || target.RootKind == item.RootKind);
                Assert.Equal(expectedApplicability, registration.Applies(target));
                ViewFacetResolution resolution = registry.Resolve(
                    item.Id,
                    target,
                    expectedApplicability ? facts : ThrowingFacts.Instance);
                Assert.Equal(
                    expectedApplicability,
                    resolution is ViewFacetResolution.Available);
                if (!expectedApplicability)
                {
                    Assert.IsType<ViewFacetResolution.Inapplicable>(resolution);
                }
            }
        }
    }

    static ViewFacetRegistry Registry(
        IEnumerable<ViewFacetRegistration> registrations)
    {
        ViewFacetRegistration[] registrationArray = [.. registrations];
        return new(
            registrationArray,
            registrationArray
                .OfType<ViewFacetRegistration.Active>()
                .Select(registration => registration.Binding));
    }

    static ViewFacetRegistration.Active Active(
        ViewFacetDescriptor descriptor,
        Func<ViewFacetTarget, bool> applies) =>
        new(
            descriptor,
            descriptor.Summary,
            applies,
            new ViewFacetExecutionBinding(
                descriptor.Id,
                new TestExecutionTarget(descriptor.Id)),
            (_, facts) => facts.Get(descriptor.Id));

    static ViewFacetRegistration.Tombstone Tombstone(
        ViewFacetDescriptor descriptor,
        Func<ViewFacetTarget, bool> applies) =>
        new(descriptor, descriptor.Summary, applies);

    static ViewFacetDescriptor Descriptor(
        string id,
        StructuralSubjectKind kind,
        int order) =>
        new(
            new ViewFacetId(id),
            kind,
            id,
            $"Purpose for {id}.",
            order);

    static ViewFacetAvailabilityFact Fact(
        ViewFacetDescriptor descriptor,
        ViewFacetAvailability availability) =>
        new(descriptor.Id, availability);

    static ViewFacetAvailabilitySnapshot AllAvailable(
        ViewFacetRegistry registry) =>
        new(
            registry.Descriptors.Select(descriptor =>
                new ViewFacetAvailabilityFact(
                    descriptor.Id,
                    ViewFacetAvailability.Available.Instance)));

    static string Id(ViewFacetId id) => id.Value;

    static bool AppliesToPackageRoot(ViewFacetTarget target) =>
        target.Subject.Kind == StructuralSubjectKind.Root
        && target.RootKind == ViewFacetRootKind.PackageCapable;

    static bool AppliesToLibrary(ViewFacetTarget target) =>
        target.Subject.Kind == StructuralSubjectKind.Library;

    static ViewFacetTarget RootTarget(ViewFacetRootKind rootKind) =>
        ViewFacetTarget.ForRoot(
            StructuralSubjectIdentity.ForRoot(
                rootKind == ViewFacetRootKind.PackageCapable
                    ? Coordinate()
                    : PlatformCoordinate()));

    static ViewFacetTarget LibraryTarget() =>
        ViewFacetTarget.ForSubject(
            StructuralSubjectIdentity.ForAllLibraries(Coordinate()));

    static IEnumerable<ViewFacetTarget> Targets()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate();
        StructuralSubjectIdentity.LibrarySubject library =
            StructuralSubjectIdentity.ForLibrary(Library(coordinate));
        StructuralSubjectIdentity.TypeSubject type =
            StructuralSubjectIdentity.ForType(
                library,
                TypeName("Sample", "Widget"));
        yield return ViewFacetTarget.ForRoot(
            StructuralSubjectIdentity.ForRoot(coordinate));
        yield return ViewFacetTarget.ForRoot(
            StructuralSubjectIdentity.ForRoot(PlatformCoordinate()));
        yield return ViewFacetTarget.ForSubject(library);
        yield return ViewFacetTarget.ForSubject(type);
        yield return ViewFacetTarget.ForSubject(
            StructuralSubjectIdentity.ForMember(
                type,
                new MemberAnchor(
                    "Run()",
                    "Sample.Widget.Run()",
                    MemberAnchor.ComputeFingerprint("Sample.Widget.Run()"),
                    "Sample.Widget",
                    "Run")));
    }

    static RealizedMemberCoordinate.Package Coordinate() =>
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

    sealed record TestExecutionTarget(ViewFacetId Id);

    sealed class ThrowingFacts : IViewFacetAvailabilityFacts
    {
        public static ThrowingFacts Instance { get; } = new();

        public ViewFacetAvailability Get(ViewFacetId id) =>
            throw new InvalidOperationException(
                $"Availability was consulted for '{id.Value}'.");
    }

    sealed class NoWorkSentinels
    {
        bool _alias;
        bool _artifactOpenOrAcquisition;
        bool _cache;
        bool _dynamicProvider;
        bool _execution;
        bool _filesystem;
        bool _network;

        public void TouchAll()
        {
            _execution = true;
            _artifactOpenOrAcquisition = true;
            _cache = true;
            _alias = true;
            _dynamicProvider = true;
            _filesystem = true;
            _network = true;
        }

        public void AssertUntouched()
        {
            Assert.False(_execution);
            Assert.False(_artifactOpenOrAcquisition);
            Assert.False(_cache);
            Assert.False(_alias);
            Assert.False(_dynamicProvider);
            Assert.False(_filesystem);
            Assert.False(_network);
        }
    }

    sealed record DescriptorShape(
        string Id,
        StructuralSubjectKind Kind,
        string Title,
        string Summary,
        int Order,
        ViewFacetRole? Role);

    sealed record ExpectedFacet(
        string Id,
        StructuralSubjectKind Kind,
        string Title,
        string Summary,
        int Order,
        ViewFacetRole? Role = null,
        ViewFacetRootKind? RootKind = null)
    {
        public DescriptorShape Descriptor =>
            new(Id, Kind, Title, Summary, Order, Role);
    }
}
