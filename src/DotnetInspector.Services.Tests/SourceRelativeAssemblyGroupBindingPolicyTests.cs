using DotnetInspector.Fixtures;
using ILInspector.Metadata;

namespace DotnetInspector.Services.Tests;

public class SourceRelativeAssemblyGroupBindingPolicyTests
{
    [Fact]
    public void Select_ContinuationDoesNotChangeSeedFallback()
    {
        var fallback = NamedDescriptor("Fallback");
        var owner = NamedDescriptor("Owner");
        var shared = NamedDescriptor("Shared");
        var dependency = NamedDescriptor("Dependency");
        var missing = new SelectionPolicy(_ => AssemblyBindingSelection.NameNotOwned());
        var selecting = new SelectionPolicy(request =>
            AssemblyBindingSelection.Found(
                request.Target is AssemblyBindingTarget.AssemblyReference
                    { Identity.Name: "Shared" } ? shared : dependency));
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [(fallback, (IAssemblyBindingPolicy)missing), (owner, selecting)]);
        AssemblyBindingRequest seedProbe = Request(dependency, shared);
        Assert.IsType<AssemblyBindingSelection.Missing>(group.Select(seedProbe).Selection);
        AssemblyBindingPolicyVersion version = group.Version;

        var selected = Selected(group, Request(shared, owner));
        var repeated = Selected(group, Request(shared, owner));
        var continued = Selected(group, Request(dependency, selected.Occurrence));

        Assert.Same(dependency, continued.Assembly);
        Assert.Equal(selected.Occurrence.Lineage, repeated.Occurrence.Lineage);
        Assert.NotEqual(AssemblyBindingLineage.Seed, selected.Occurrence.Lineage);
        Assert.Same(version, selected.Occurrence.Lineage.Version);
        Assert.Same(version, group.Version);
        Assert.IsType<AssemblyBindingSelection.Missing>(group.Select(seedProbe).Selection);
    }

    [Fact]
    public void Select_CanonicalRootUsesItsConfiguredResolver()
    {
        var first = NamedDescriptor("First");
        var peer = NamedDescriptor("Peer");
        var firstDependency = NamedDescriptor("FirstDependency");
        var peerDependency = NamedDescriptor("PeerDependency");
        var firstPolicy = new SelectionPolicy(_ => AssemblyBindingSelection.Found(firstDependency));
        var peerPolicy = new SelectionPolicy(_ => AssemblyBindingSelection.Found(peerDependency));
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [(first, (IAssemblyBindingPolicy)firstPolicy), (peer, peerPolicy)]);

        var selectedPeer = Selected(group, Request(peer, first));
        var continued = Selected(group, Request(peerDependency, selectedPeer.Occurrence));

        Assert.Same(peer, selectedPeer.Assembly);
        Assert.Same(peerDependency, continued.Assembly);
        Assert.Equal(0, firstPolicy.SelectionCount);
        Assert.Equal(1, peerPolicy.SelectionCount);
    }

    [Fact]
    public void Select_NestedGroupPreservesDelegatedContinuation()
    {
        var fallback = NamedDescriptor("Fallback");
        var owner = NamedDescriptor("Owner");
        var shared = NamedDescriptor("Shared");
        var dependency = NamedDescriptor("Dependency");
        var missing = new SelectionPolicy(_ => AssemblyBindingSelection.NameNotOwned());
        var selecting = new SelectionPolicy(request =>
            AssemblyBindingSelection.Found(
                request.Target is AssemblyBindingTarget.AssemblyReference
                    { Identity.Name: "Shared" } ? shared : dependency));
        var inner = new SourceRelativeAssemblyGroupBindingPolicy(
            [(fallback, (IAssemblyBindingPolicy)missing), (owner, selecting)]);
        var outer = new SourceRelativeAssemblyGroupBindingPolicy(
            [(fallback, (IAssemblyBindingPolicy)missing), (owner, inner)]);

        var selected = Selected(outer, Request(shared, owner));
        var repeated = Selected(outer, Request(shared, owner));
        var continued = Selected(outer, Request(dependency, selected.Occurrence));

        Assert.Same(dependency, continued.Assembly);
        Assert.Equal(selected.Occurrence.Lineage, repeated.Occurrence.Lineage);
        Assert.Same(outer.Version, continued.Occurrence.Lineage.Version);
        Assert.IsType<AssemblyBindingSelection.Missing>(
            outer.Select(Request(dependency, shared)).Selection);
    }

    [Fact]
    public void Select_AmbiguityAndShadowsDoNotEstablishRoutes()
    {
        var fallback = NamedDescriptor("Fallback");
        var owner = NamedDescriptor("Owner");
        var first = NamedDescriptor("FirstCandidate");
        var second = NamedDescriptor("SecondCandidate");
        var active = NamedDescriptor("Active");
        var probe = NamedDescriptor("Probe");
        var missing = new SelectionPolicy(_ => AssemblyBindingSelection.NameNotOwned());
        var selecting = new SelectionPolicy(request =>
            request.Target is AssemblyBindingTarget.AssemblyReference
                { Identity.Name: "Active" }
                ? AssemblyBindingSelection.Found(active, [first])
                : AssemblyBindingSelection.Multiple([first, second]));
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [(fallback, (IAssemblyBindingPolicy)missing), (owner, selecting)]);

        Assert.IsType<AssemblyBindingSelection.Ambiguous>(
            group.Select(Request(probe, owner)).Selection);
        var selected = Selected(group, Request(active, owner));
        Assert.Same(first, Assert.Single(selected.ShadowedAssemblies));
        foreach (var inactive in new[] { first, second })
        {
            Assert.IsType<AssemblyBindingSelection.Missing>(
                group.Select(Request(probe, inactive)).Selection);
        }
    }

    [Fact]
    public void Select_RejectsForeignAndStaleContinuations()
    {
        var owner = NamedDescriptor("Owner");
        var candidate = NamedDescriptor("Candidate");
        var policy = new SelectionPolicy(_ => AssemblyBindingSelection.Found(candidate));
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [(owner, (IAssemblyBindingPolicy)policy)]);
        var foreignGroup = new SourceRelativeAssemblyGroupBindingPolicy(
            [(owner, (IAssemblyBindingPolicy)policy)]);
        var selected = Selected(group, Request(candidate, owner));
        AssemblyBindingRequest continuation = Request(candidate, selected.Occurrence);
        var foreign = Assert.IsType<AssemblyBindingSelection.Rejected>(
            foreignGroup.Select(continuation).Selection);
        Assert.Equal(AssemblyBindingFailureKind.InvalidBindingOrigin, foreign.Failure.Kind);

        AssemblyBindingPolicyVersion version = group.Version;
        policy.Replace(_ => AssemblyBindingSelection.Found(candidate));

        Assert.NotSame(version, group.Version);
        var stale = Assert.IsType<AssemblyBindingSelection.Rejected>(
            group.Select(continuation).Selection);
        Assert.Equal(AssemblyBindingFailureKind.InvalidBindingOrigin, stale.Failure.Kind);
        var fresh = Selected(group, Request(candidate, owner));
        Assert.NotEqual(selected.Occurrence.Lineage, fresh.Occurrence.Lineage);
        Assert.Same(group.Version, fresh.Occurrence.Lineage.Version);
    }

    [Fact]
    public void Select_IntrinsicCacheSeparatesSharedDescriptorLineages()
    {
        var first = NamedDescriptor("First");
        var second = NamedDescriptor("Second");
        var shared = Descriptor(typeof(SourceRelativeAssemblyGroupBindingPolicyTests).Assembly.Location);
        var firstCore = Descriptor(typeof(object).Assembly.Location);
        var secondCore = Descriptor(typeof(object).Assembly.Location);
        var firstPolicy = new SelectionPolicy(request =>
            AssemblyBindingSelection.Found(
                request.Target is AssemblyBindingTarget.AssemblyReference reference
                    && reference.Identity.Name == shared.Identity.Name ? shared : firstCore));
        var secondPolicy = new SelectionPolicy(request =>
            AssemblyBindingSelection.Found(
                request.Target is AssemblyBindingTarget.AssemblyReference reference
                    && reference.Identity.Name == shared.Identity.Name ? shared : secondCore));
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [(first, (IAssemblyBindingPolicy)firstPolicy), (second, secondPolicy)]);
        var firstShared = Selected(group, Request(shared, first)).Occurrence;
        var secondShared = Selected(group, Request(shared, second)).Occurrence;

        for (int repeat = 0; repeat < 2; repeat++)
        {
            Assert.Same(firstCore, SelectCore(firstShared).Assembly);
            Assert.Same(secondCore, SelectCore(secondShared).Assembly);
        }
        Assert.Equal(2, firstPolicy.SelectionCount);
        Assert.Equal(2, secondPolicy.SelectionCount);

        AssemblyBindingSelection.Selected SelectCore(AssemblyBindingOccurrence occurrence) =>
            Selected(group, new AssemblyBindingRequest(
                AssemblyBindingTarget.CoreLibrary(),
                AssemblyBindingOrigin.FromOccurrence(occurrence),
                AssemblyResolutionScope.Any));
    }

    [Fact]
    public void Select_WarmIntrinsicCacheObservesDelegateVersionChange()
    {
        var owner = Descriptor(typeof(SourceRelativeAssemblyGroupBindingPolicyTests).Assembly.Location);
        var firstCore = Descriptor(typeof(object).Assembly.Location);
        var secondCore = Descriptor(typeof(object).Assembly.Location);
        var policy = new SelectionPolicy(_ => AssemblyBindingSelection.Found(firstCore));
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [(owner, (IAssemblyBindingPolicy)policy)]);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.CoreLibrary(),
            AssemblyBindingOrigin.FromAssembly(owner),
            AssemblyResolutionScope.Any);
        Assert.Same(firstCore, Selected(group, request).Assembly);
        Assert.Same(firstCore, Selected(group, request).Assembly);
        Assert.Equal(1, policy.SelectionCount);
        AssemblyBindingPolicyVersion version = group.Version;

        policy.Replace(_ => AssemblyBindingSelection.Found(secondCore));

        Assert.NotSame(version, group.Version);
        Assert.Same(secondCore, Selected(group, request).Assembly);
        Assert.Equal(2, policy.SelectionCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Select_ForeignSnapshotEscapesBeforePayloadComposition(bool intrinsic)
    {
        var owner = Descriptor(typeof(SourceRelativeAssemblyGroupBindingPolicyTests).Assembly.Location);
        var candidate = NamedDescriptor("Candidate");
        var foreign = new AssemblyBindingSelectionSnapshot(
            new AssemblyBindingPolicyVersion(),
            AssemblyBindingSelection.Found(candidate));
        var policy = new ForeignSnapshotPolicy(foreign);
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [(owner, (IAssemblyBindingPolicy)policy)]);
        var request = new AssemblyBindingRequest(
            intrinsic ? AssemblyBindingTarget.CoreLibrary()
                : AssemblyBindingTarget.Reference(candidate.Identity),
            AssemblyBindingOrigin.FromAssembly(owner),
            AssemblyResolutionScope.Any);

        Assert.Same(foreign, group.Select(request));
        Assert.Equal(1, policy.SelectionCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtractApiSurface_SharedForwarderRetainsBothResolverContexts(
        bool interfaceFirst)
    {
        string consumerPath = FixtureCatalog.ServicesRouteLearningConsumer.AssemblyPath();
        ResolvedAssemblyReference classConsumer = Descriptor(consumerPath);
        ResolvedAssemblyReference interfaceConsumer = Descriptor(consumerPath);
        ResolvedAssemblyReference middle = Descriptor(
            FixtureCatalog.ServicesRouteLearningConsumer.AssetPath("middle"));
        ResolvedAssemblyReference classBase = Descriptor(
            FixtureCatalog.ServicesRouteLearningConsumer.AssetPath("base"));
        ResolvedAssemblyReference interfaceBase = Descriptor(
            FixtureCatalog.ServicesRouteLearningInterfaceBase.AssemblyPath());
        var classPolicy = new AssemblyReferenceBindingPolicy(
            new FixtureResolver(consumerPath, middle, classBase));
        var interfacePolicy = new AssemblyReferenceBindingPolicy(
            new FixtureResolver(consumerPath, middle, interfaceBase));
        var group = new SourceRelativeAssemblyGroupBindingPolicy(
            [
                (classConsumer, (IAssemblyBindingPolicy)classPolicy),
                (interfaceConsumer, (IAssemblyBindingPolicy)interfacePolicy),
            ]);
        AssemblyBindingPolicyVersion version = group.Version;
        using var catalog = new TypeResolutionCatalog();
        (ResolvedAssemblyReference Assembly, TypeParameterTypeKind Kind)[] requests =
            [
                (classConsumer, TypeParameterTypeKind.ReferenceType),
                (interfaceConsumer, TypeParameterTypeKind.NeitherReferenceNorValue),
            ];
        if (interfaceFirst)
            Array.Reverse(requests);

        for (int repeat = 0; repeat < 2; repeat++)
        {
            foreach (var request in requests)
            {
                ApiSurface surface = Assert.IsType<
                    ResolutionAwareApiSurfaceOutcome.Read>(
                        catalog.ExtractApiSurface(request.Assembly, group)).Surface;
                ApiType consumer = Assert.Single(
                    surface.Types,
                    static type => type.FullName
                        == "DotnetInspector.Services.RouteLearning.Consumer`1");

                Assert.Equal(
                    request.Kind,
                    Assert.Single(consumer.TypeParameters).TypeKind);
                Assert.Empty(surface.InspectionFailures);
                Assert.Same(version, group.Version);
            }
        }
    }

    static ResolvedAssemblyReference Descriptor(string path) =>
        ResolvedAssemblyReference.CreateFromPath(
            path,
            AssemblyResolutionProvenance.Local("resolver-lineage fixture"));

    static ResolvedAssemblyReference NamedDescriptor(string name) =>
        ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity(name, new Version(1, 0, 0, 0), null, null),
            name + ".dll",
            static () => throw new InvalidOperationException(
                "Descriptor-only selection must not open an assembly."),
            AssemblyResolutionProvenance.Local("resolver-lineage selection"));

    static AssemblyBindingRequest Request(
        ResolvedAssemblyReference target,
        ResolvedAssemblyReference origin) =>
        new(
            AssemblyBindingTarget.Reference(target.Identity),
            AssemblyBindingOrigin.FromAssembly(origin),
            AssemblyResolutionScope.Any);

    static AssemblyBindingRequest Request(
        ResolvedAssemblyReference target,
        AssemblyBindingOccurrence origin) =>
        new(
            AssemblyBindingTarget.Reference(target.Identity),
            AssemblyBindingOrigin.FromOccurrence(origin),
            AssemblyResolutionScope.Any);

    static AssemblyBindingSelection.Selected Selected(
        IAssemblyBindingPolicy policy,
        AssemblyBindingRequest request) =>
        Assert.IsType<AssemblyBindingSelection.Selected>(policy.Select(request).Selection);

    sealed class SelectionPolicy(
        Func<AssemblyBindingRequest, AssemblyBindingSelection> select) : IAssemblyBindingPolicy
    {
        Func<AssemblyBindingRequest, AssemblyBindingSelection> _select = select;
        public AssemblyBindingPolicyVersion Version { get; private set; } = new();
        internal int SelectionCount { get; private set; }

        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request)
        {
            SelectionCount++;
            return new(Version, _select(request));
        }

        internal void Replace(Func<AssemblyBindingRequest, AssemblyBindingSelection> replacement)
        {
            _select = replacement;
            Version = new();
        }
    }

    sealed class ForeignSnapshotPolicy(
        AssemblyBindingSelectionSnapshot snapshot) : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();
        internal int SelectionCount { get; private set; }

        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request)
        {
            SelectionCount++;
            return snapshot;
        }
    }

    sealed class FixtureResolver(
        string consumerPath,
        ResolvedAssemblyReference middle,
        ResolvedAssemblyReference implementation) : IAssemblyReferenceResolver
    {
        readonly AssemblyDependencyResolver _fallback = new(
            new AssemblyDependencyResolutionOptions(consumerPath)
            {
                PreferImplementationAssemblies = true,
                AllowPlatformAssemblyVersionRollForward = true,
            });

        public ResolvedAssemblyReference? Resolve(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope)
        {
            if (identity.Name == middle.Identity.Name)
                return middle;
            if (identity.Name == implementation.Identity.Name)
                return implementation;
            return _fallback.Resolve(identity, scope);
        }
    }
}
