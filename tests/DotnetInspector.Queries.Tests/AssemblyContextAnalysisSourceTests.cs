using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextAnalysisSourceTests
{
    [Fact]
    public void BindingPolicyResolver_PreservesDelegatedNonSelectedResults()
    {
        AssemblyBindingSelection[] terminalResults =
        [
            AssemblyBindingSelection.NotFound(),
            AssemblyBindingSelection.NameNotOwned(),
            AssemblyBindingSelection.NameOwnedButNoMatch(),
            AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.CandidateUnavailable)),
            AssemblyBindingSelection.Invalid(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.InvalidPolicyResult)),
        ];

        foreach (AssemblyBindingSelection terminal in terminalResults)
        {
            var policy = new FixedPolicy(terminal);
            ResolvedAssemblyReference assembly =
                ResolvedAssemblyReference.Create(
                    new AssemblyReferenceIdentity(
                        "Root",
                        new Version(1, 0, 0, 0),
                        null,
                        null),
                    path: null,
                    openRead: () => new MemoryStream(),
                    AssemblyResolutionProvenance.Local("test"));
            using var workspace = new InspectionWorkspace();
            using AssemblyContextGroup group =
                workspace.CreateAssemblyContextGroup(
                    [new AssemblyContextParticipant(assembly, policy)]);
            var subject = new AssemblyContextSubject(assembly);
            IAssemblyReferenceResolver resolver =
                AssemblyContextAnalysisSource.Resolver(group, subject);
            var bindingPolicy =
                Assert.IsAssignableFrom<IAssemblyBindingPolicy>(resolver);
            ResolvedAssemblyReference retainedRoot = Descriptor();
            var request = new AssemblyBindingRequest(
                AssemblyBindingTarget.Reference(
                    new AssemblyReferenceIdentity(
                        "Dependency",
                        new Version(1, 0, 0, 0),
                        null,
                        null)),
                AssemblyBindingOrigin.FromAssembly(retainedRoot),
                AssemblyResolutionScope.Any);

            Assert.NotSame(policy.Version, bindingPolicy.Version);
            Assert.Same(terminal, bindingPolicy.Select(request).Selection);
            Assert.Same(
                assembly.Registration,
                Assert.IsType<AssemblyBindingOrigin.RequestingAssembly>(
                    policy.LastRequest!.Origin).Registration);
            Assert.Same(
                terminal,
                new AssemblyReferenceBindingPolicy(resolver)
                    .Select(request).Selection);
        }
    }

    [Fact]
    public void BindingPolicyResolver_RetainsSelectedDescriptorAndShadows()
    {
        ResolvedAssemblyReference root = Descriptor();
        ResolvedAssemblyReference selected = Descriptor();
        ResolvedAssemblyReference shadow = Descriptor();
        var policy = new FixedPolicy(
            AssemblyBindingSelection.Found(selected, [shadow]));
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(root, policy),
                    new AssemblyContextParticipant(selected, policy),
                    new AssemblyContextParticipant(shadow, policy),
                ]);
        var subject = new AssemblyContextSubject(root);
        var bindingPolicy = Assert.IsAssignableFrom<IAssemblyBindingPolicy>(
            AssemblyContextAnalysisSource.Resolver(group, subject));

        var retained = Assert.IsType<AssemblyBindingSelection.Selected>(
            bindingPolicy.Select(Request(root)).Selection);

        Assert.Same(
            selected.Registration,
            retained.Assembly.Registration);
        Assert.NotSame(selected, retained.Assembly);
        ResolvedAssemblyReference retainedShadow =
            Assert.Single(retained.ShadowedAssemblies);
        Assert.Same(
            shadow.Registration,
            retainedShadow.Registration);
        Assert.NotSame(shadow, retainedShadow);
    }

    [Fact]
    public void BindingPolicyResolver_RetainsAmbiguousDescriptors()
    {
        ResolvedAssemblyReference root = Descriptor();
        ResolvedAssemblyReference first = Descriptor();
        ResolvedAssemblyReference second = Descriptor();
        var policy = new FixedPolicy(
            AssemblyBindingSelection.Multiple([first, second]));
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(root, policy),
                    new AssemblyContextParticipant(first, policy),
                    new AssemblyContextParticipant(second, policy),
                ]);
        var subject = new AssemblyContextSubject(root);
        var bindingPolicy = Assert.IsAssignableFrom<IAssemblyBindingPolicy>(
            AssemblyContextAnalysisSource.Resolver(group, subject));

        var retained = Assert.IsType<AssemblyBindingSelection.Ambiguous>(
            bindingPolicy.Select(Request(root)).Selection);

        Assert.Equal(
            [first.Registration, second.Registration],
            retained.Assemblies.Select(
                assembly => assembly.Registration));
        Assert.DoesNotContain(first, retained.Assemblies);
        Assert.DoesNotContain(second, retained.Assemblies);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Facade_OwnsStableVersionAndSelectedContinuation(bool observing)
    {
        using var fixture = new FacadeFixture();
        fixture.Inner.Selection = AssemblyBindingSelection.Found(
            fixture.Selected);
        IAssemblyBindingPolicy policy = fixture.CreatePolicy(observing);
        AssemblyBindingPolicyVersion version = policy.Version;

        AssemblyBindingSelectionSnapshot first = policy.Select(
            Request(fixture.Root));
        AssemblyBindingSelectionSnapshot second = policy.Select(
            Request(fixture.Root));
        var selected = Assert.IsType<AssemblyBindingSelection.Selected>(
            first.Selection);

        Assert.NotSame(fixture.Inner.Version, version);
        Assert.NotSame(version, fixture.CreatePolicy(observing).Version);
        Assert.Same(version, first.Version);
        Assert.Same(version, second.Version);
        Assert.Same(version, policy.Version);
        Assert.Same(version, selected.Occurrence.Lineage.Version);
        Assert.Equal(
            selected.Occurrence.Lineage,
            Assert.IsType<AssemblyBindingSelection.Selected>(
                second.Selection).Occurrence.Lineage);
        Assert.Same(fixture.Selected.Registration, selected.Assembly.Registration);
        Assert.NotSame(fixture.Selected, selected.Assembly);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Facade_ForwardsForeignSnapshotBeforeDescriptorEffects(bool observing)
    {
        using var fixture = new FacadeFixture();
        fixture.Inner.Selection = AssemblyBindingSelection.Found(
            fixture.Selected,
            [fixture.Root]);
        fixture.Inner.SnapshotVersion = new();
        IAssemblyBindingPolicy policy = fixture.CreatePolicy(observing);
        AssemblyBindingPolicyVersion version = policy.Version;

        AssemblyBindingSelectionSnapshot snapshot = policy.Select(
            Request(fixture.Root));

        Assert.Same(fixture.Inner.LastSnapshot, snapshot);
        Assert.Same(fixture.Inner.Selection, snapshot.Selection);
        Assert.Equal(0, fixture.SelectedOpenCount);
        Assert.NotSame(version, policy.Version);
        Assert.NotSame(fixture.Inner.Version, policy.Version);

        fixture.Inner.SnapshotVersion = null;
        AssemblyBindingPolicyVersion refreshed = policy.Version;
        Assert.Same(refreshed, policy.Select(Request(fixture.Root)).Version);
        Assert.Same(refreshed, policy.Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Facade_RefreshesAfterActualDelegateChange(bool observing)
    {
        using var fixture = new FacadeFixture();
        IAssemblyBindingPolicy policy = fixture.CreatePolicy(observing);
        AssemblyBindingPolicyVersion first = policy.Version;
        fixture.Inner.BeforeSelection = () => fixture.Inner.Version = new();

        AssemblyBindingSelectionSnapshot foreign = policy.Select(
            Request(fixture.Root));
        AssemblyBindingPolicyVersion second = policy.Version;
        Assert.Same(fixture.Inner.LastSnapshot, foreign);
        Assert.NotSame(first, second);
        Assert.NotSame(fixture.Inner.Version, second);

        fixture.Inner.BeforeSelection = null;
        Assert.Same(second, policy.Select(Request(fixture.Root)).Version);
        fixture.Inner.Version = new();
        AssemblyBindingPolicyVersion third = policy.Version;
        Assert.NotSame(first, third);
        Assert.NotSame(second, third);
        Assert.Same(third, policy.Select(Request(fixture.Root)).Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Facade_ContinuesWithOriginalDelegatedOccurrence(bool observing)
    {
        using var fixture = new FacadeFixture();
        AssemblyBindingOccurrence delegated =
            new TestLineage(fixture.Inner.Version).Issue(fixture.Selected);
        fixture.Inner.Selection = AssemblyBindingSelection.FoundOccurrence(
            delegated);
        IAssemblyBindingPolicy policy = fixture.CreatePolicy(observing);
        var selected = Assert.IsType<AssemblyBindingSelection.Selected>(
            policy.Select(Request(fixture.Root)).Selection);
        fixture.Inner.BeforeSelection = () =>
        {
            fixture.Inner.Selection =
                fixture.Inner.LastRequest!.Origin
                    is AssemblyBindingOrigin.RequestingAssembly origin
                    && ReferenceEquals(origin.Occurrence, delegated)
                        ? AssemblyBindingSelection.Found(fixture.Root)
                        : AssemblyBindingSelection.NameNotOwned();
        };

        var continued = Assert.IsType<AssemblyBindingSelection.Selected>(
            policy.Select(
                new AssemblyBindingRequest(
                    AssemblyBindingTarget.Reference(fixture.Root.Identity),
                    AssemblyBindingOrigin.FromOccurrence(selected.Occurrence),
                    AssemblyResolutionScope.Any)).Selection);

        Assert.Same(fixture.Root.Registration, continued.Assembly.Registration);
        Assert.Same(
            delegated,
            Assert.IsType<AssemblyBindingOrigin.RequestingAssembly>(
                fixture.Inner.LastRequest!.Origin).Occurrence);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Facade_RejectsContinuationFromRetiredState(bool observing)
    {
        using var fixture = new FacadeFixture();
        fixture.Inner.Selection = AssemblyBindingSelection.Found(
            fixture.Selected);
        IAssemblyBindingPolicy policy = fixture.CreatePolicy(observing);
        var selected = Assert.IsType<AssemblyBindingSelection.Selected>(
            policy.Select(Request(fixture.Root)).Selection);
        fixture.Inner.Version = new();

        var rejected = Assert.IsType<AssemblyBindingSelection.Rejected>(
            policy.Select(
                new AssemblyBindingRequest(
                    AssemblyBindingTarget.Reference(fixture.Root.Identity),
                    AssemblyBindingOrigin.FromOccurrence(selected.Occurrence),
                    AssemblyResolutionScope.Any)).Selection);

        Assert.Equal(
            AssemblyBindingFailureKind.InvalidBindingOrigin,
            rejected.Failure.Kind);
        Assert.Equal(1, fixture.Inner.SelectionCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Facade_NullSnapshotRemainsInvalidAtMetadata(bool observing)
    {
        using var fixture = new FacadeFixture();
        fixture.Inner.ReturnNull = true;
        IAssemblyBindingPolicy policy = fixture.CreatePolicy(observing);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(fixture.Selected.Identity),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Any);
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            policy,
            roots: [],
            bindingRequests: [request],
            requests: []);

        var rejected = Assert.IsType<AssemblyBindingOutcome.Rejected>(
            context.Bind(request));
        Assert.Equal(
            AssemblyBindingFailureKind.InvalidPolicyResult,
            rejected.Failure.Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Facade_ForeignSnapshotPublishesNoMetadataContext(bool observing)
    {
        using var fixture = new FacadeFixture();
        fixture.Inner.Selection = AssemblyBindingSelection.Found(
            fixture.Selected);
        fixture.Inner.BeforeSelection = () => fixture.Inner.Version = new();
        IAssemblyBindingPolicy policy = fixture.CreatePolicy(observing);
        using var catalog = new TypeResolutionCatalog();
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(fixture.Selected.Identity),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Any);

        Assert.Throws<InvalidOperationException>(
            () => catalog.CreateContext(
                policy,
                roots: [],
                bindingRequests: [request],
                requests: []));
        Assert.Equal(0, fixture.SelectedOpenCount);
        Assert.Equal(1, fixture.Inner.SelectionCount);

        fixture.Inner.BeforeSelection = null;
        fixture.Inner.Selection = AssemblyBindingSelection.NameNotOwned();
        using TypeResolutionContext next = catalog.CreateContext(
            policy,
            roots: [],
            bindingRequests: [request],
            requests: []);
        Assert.IsType<AssemblyBindingOutcome.Missing>(next.Bind(request));
        Assert.Equal(2, fixture.Inner.SelectionCount);
    }

    [Fact]
    public void BindingPolicyResolver_LegacyResolveRejectsForeignSnapshot()
    {
        using var fixture = new FacadeFixture();
        fixture.Inner.Selection = AssemblyBindingSelection.Found(
            fixture.Selected);
        fixture.Inner.BeforeSelection = () => fixture.Inner.Version = new();
        IAssemblyReferenceResolver resolver =
            AssemblyContextAnalysisSource.Resolver(
                fixture.Group,
                new AssemblyContextSubject(fixture.Root));

        Assert.Throws<InvalidOperationException>(
            () => resolver.Resolve(
                fixture.Selected.Identity,
                AssemblyResolutionScope.Any));
        Assert.Equal(0, fixture.SelectedOpenCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BindingPolicyResolver_ValidatesCapturedGroupBeforePublication(
        bool foreignSnapshot)
    {
        using var fixture = new FacadeFixture();
        var resolver = AssemblyContextAnalysisSource.Resolver(
            fixture.Group,
            new AssemblyContextSubject(fixture.Root));
        if (foreignSnapshot)
            fixture.Inner.SnapshotVersion = new();

        resolver.Select(Request(fixture.Root));
        if (!foreignSnapshot)
            fixture.Inner.Version = new();

        Assert.Throws<InvalidOperationException>(
            resolver.ValidateForPublication);
    }

    static ResolvedAssemblyReference Descriptor() =>
        ResolvedAssemblyReference.CreateFromPath(
            typeof(AssemblyContextAnalysisSourceTests).Assembly.Location,
            AssemblyResolutionProvenance.Local("test"));

    static AssemblyBindingRequest Request(
        ResolvedAssemblyReference origin) =>
        new(
            AssemblyBindingTarget.Reference(
                new AssemblyReferenceIdentity(
                    "Dependency",
                    new Version(1, 0, 0, 0),
                    null,
                    null)),
            AssemblyBindingOrigin.FromAssembly(origin),
            AssemblyResolutionScope.Any);

    sealed class FixedPolicy(AssemblyBindingSelection selection)
        : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; set; } = new();
        internal AssemblyBindingPolicyVersion? SnapshotVersion { get; set; }
        internal AssemblyBindingSelection Selection { get; set; } = selection;
        internal AssemblyBindingSelectionSnapshot? LastSnapshot { get; private set; }
        internal AssemblyBindingRequest? LastRequest { get; private set; }
        internal Action? BeforeSelection { get; set; }
        internal bool ReturnNull { get; set; }
        internal int SelectionCount { get; private set; }

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            LastRequest = request;
            SelectionCount++;
            BeforeSelection?.Invoke();
            LastSnapshot = ReturnNull
                ? null
                : new AssemblyBindingSelectionSnapshot(
                    SnapshotVersion ?? Version,
                    Selection);
            return LastSnapshot!;
        }
    }

    sealed record TestLineage(AssemblyBindingPolicyVersion PolicyVersion)
        : AssemblyBindingLineage(PolicyVersion)
    {
        internal AssemblyBindingOccurrence Issue(
            ResolvedAssemblyReference assembly) => CreateOccurrence(assembly);
    }

    sealed class FacadeFixture : IDisposable
    {
        readonly InspectionWorkspace _workspace = new();

        internal FacadeFixture()
        {
            Selected = ResolvedAssemblyReference.Create(
                Root.Identity,
                path: null,
                openRead: () =>
                {
                    SelectedOpenCount++;
                    return File.OpenRead(
                        typeof(AssemblyContextAnalysisSourceTests)
                            .Assembly.Location);
                },
                AssemblyResolutionProvenance.Local("test"));
            Group = _workspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(Root, Inner),
                    new AssemblyContextParticipant(Selected, Inner),
                ]);
        }

        internal FixedPolicy Inner { get; } = new(
            AssemblyBindingSelection.NameNotOwned());
        internal ResolvedAssemblyReference Root { get; } = Descriptor();
        internal ResolvedAssemblyReference Selected { get; }
        internal AssemblyContextGroup Group { get; }
        internal int SelectedOpenCount { get; private set; }

        internal IAssemblyBindingPolicy CreatePolicy(bool observing) =>
            observing
                ? new AssemblyContextSourceQuery
                    .CancellationObservingBindingPolicy(Inner)
                : Assert.IsAssignableFrom<IAssemblyBindingPolicy>(
                    AssemblyContextAnalysisSource.Resolver(
                        Group,
                        new AssemblyContextSubject(Root)));

        public void Dispose()
        {
            Group.Dispose();
            _workspace.Dispose();
        }
    }
}
