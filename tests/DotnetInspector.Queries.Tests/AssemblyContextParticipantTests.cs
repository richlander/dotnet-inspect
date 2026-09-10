using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextParticipantTests
{
    [Fact]
    public void OccurrenceRootedParticipant_TranslatesSeedAndContinuation()
    {
        ResolvedAssemblyReference root = Descriptor("Root");
        ResolvedAssemblyReference dependency = Descriptor("Dependency");
        var inner = new RecordingPolicy();
        AssemblyBindingOccurrence rootOccurrence =
            new TestLineage(inner.Version).Issue(root);
        AssemblyBindingOccurrence dependencyOccurrence =
            new TestLineage(inner.Version).Issue(dependency);
        inner.Selection = AssemblyBindingSelection.FoundOccurrence(
            dependencyOccurrence);
        var participant = new AssemblyContextParticipant(
            rootOccurrence,
            inner);

        Assert.NotSame(inner.Version, participant.BindingPolicy.Version);
        var first = Assert.IsType<AssemblyBindingSelection.Selected>(
            participant.BindingPolicy.Select(
                    Request(AssemblyBindingOrigin.FromAssembly(root)))
                .Selection);
        Assert.Same(
            rootOccurrence,
            Assert.IsType<AssemblyBindingOrigin.RequestingAssembly>(
                    inner.LastRequest!.Origin)
                .Occurrence);

        participant.BindingPolicy.Select(
            Request(
                AssemblyBindingOrigin.FromOccurrence(
                    first.Occurrence)));
        Assert.Same(
            dependencyOccurrence,
            Assert.IsType<AssemblyBindingOrigin.RequestingAssembly>(
                    inner.LastRequest!.Origin)
                .Occurrence);
    }

    [Fact]
    public void OccurrenceRootedParticipant_RejectsAnotherSeed()
    {
        ResolvedAssemblyReference root = Descriptor("Root");
        ResolvedAssemblyReference other = Descriptor("Other");
        var inner = new RecordingPolicy();
        var participant = new AssemblyContextParticipant(
            new TestLineage(inner.Version).Issue(root),
            inner);

        AssemblyBindingSelectionSnapshot snapshot =
            participant.BindingPolicy.Select(
                Request(AssemblyBindingOrigin.FromAssembly(other)));

        var rejected =
            Assert.IsType<AssemblyBindingSelection.Rejected>(
                snapshot.Selection);
        Assert.Equal(
            AssemblyBindingFailureKind.InvalidBindingOrigin,
            rejected.Failure.Kind);
        Assert.Equal(0, inner.SelectionCount);
    }

    [Fact]
    public void OccurrenceRootedParticipants_ComposeUnderOneRoutingVersion()
    {
        ResolvedAssemblyReference root = Descriptor("Root");
        ResolvedAssemblyReference dependency = Descriptor("Dependency");
        var inner = new RecordingPolicy();
        var lineage = new TestLineage(inner.Version);
        AssemblyBindingOccurrence rootOccurrence = lineage.Issue(root);
        AssemblyBindingOccurrence dependencyOccurrence =
            lineage.Issue(dependency);
        inner.Selection = AssemblyBindingSelection.NotFound();
        var rootRoute = new AssemblyContextParticipant(
            rootOccurrence,
            inner);
        var dependencyRoute = new AssemblyContextParticipant(
            dependencyOccurrence,
            inner);
        var policy =
            SourceRelativeAssemblyGroupBindingPolicy.CreateRoutingOnly(
                [
                    (rootRoute.Assembly, rootRoute.BindingPolicy),
                    (dependencyRoute.Assembly,
                        dependencyRoute.BindingPolicy),
                ]);
        var rootParticipant = new AssemblyContextParticipant(
            root,
            policy);
        var dependencyParticipant = new AssemblyContextParticipant(
            dependency,
            policy);

        policy.Select(
            Request(
                AssemblyBindingOrigin.FromAssembly(dependency)));

        Assert.Same(
            dependencyOccurrence,
            Assert.IsType<AssemblyBindingOrigin.RequestingAssembly>(
                    inner.LastRequest!.Origin)
                .Occurrence);
        Assert.Same(
            rootParticipant.BindingPolicy.Version,
            dependencyParticipant.BindingPolicy.Version);
    }

    [Fact]
    public void OccurrenceRootedParticipant_PreservesRoutingCompositionContinuation()
    {
        ResolvedAssemblyReference fallback = Descriptor("Fallback");
        ResolvedAssemblyReference owner = Descriptor("Owner");
        ResolvedAssemblyReference selected = Descriptor(
            "Platform.Library",
            AssemblyResolutionProvenance.Designated("selected overlay"));
        ResolvedAssemblyReference dependency = Descriptor("Dependency");
        var missing = new SelectingPolicy(_ =>
            AssemblyBindingSelection.NameNotOwned());
        var selecting = new SelectingPolicy(request =>
            request.Target is AssemblyBindingTarget.AssemblyReference
                { Identity.Name: "Platform.Library" }
                ? AssemblyBindingSelection.RequireComposition(
                    AssemblyBindingCandidateDomain.Create([selected]))
                : AssemblyBindingSelection.Found(dependency));
        IAssemblyBindingPolicy routing =
            SourceRelativeAssemblyGroupBindingPolicy.CreateRoutingOnly(
                [
                    (fallback, (IAssemblyBindingPolicy)missing),
                    (owner, (IAssemblyBindingPolicy)selecting),
                ]);
        IAssemblyBindingPolicy participant =
            new AssemblyContextParticipant(
                AssemblyBindingOccurrence.Seed(owner),
                routing)
                .BindingPolicy;
        var outer = new SourceRelativeAssemblyGroupBindingPolicy(
            [
                (fallback, participant),
                (owner, participant),
            ]);

        var first = Assert.IsType<AssemblyBindingSelection.Selected>(
            outer.Select(
                    Request(
                        selected,
                        AssemblyBindingOrigin.FromAssembly(owner)))
                .Selection);
        var continued = Assert.IsType<AssemblyBindingSelection.Selected>(
            outer.Select(
                    Request(
                        dependency,
                        AssemblyBindingOrigin.FromOccurrence(
                            first.Occurrence)))
                .Selection);

        Assert.Same(selected, first.Assembly);
        Assert.Same(dependency, continued.Assembly);
    }

    [Fact]
    public void OccurrenceRootedParticipant_PreservesGlobalCompositionContinuation()
    {
        ResolvedAssemblyReference owner = Descriptor("Owner");
        ResolvedAssemblyReference selected = Descriptor(
            "Selected",
            AssemblyResolutionProvenance.Designated("selected overlay"));
        ResolvedAssemblyReference dependency = Descriptor("Dependency");
        var selecting = new SelectingPolicy(request =>
            request.Target is AssemblyBindingTarget.AssemblyReference
                { Identity.Name: "Selected" }
                ? AssemblyBindingSelection.RequireComposition(
                    AssemblyBindingCandidateDomain.Create([selected]))
                : AssemblyBindingSelection.Found(dependency));
        IAssemblyBindingPolicy routing =
            SourceRelativeAssemblyGroupBindingPolicy.CreateRoutingOnly(
                [(owner, (IAssemblyBindingPolicy)selecting)]);
        IAssemblyBindingPolicy participant =
            new AssemblyContextParticipant(
                AssemblyBindingOccurrence.Seed(owner),
                routing)
                .BindingPolicy;
        var outer = new SourceRelativeAssemblyGroupBindingPolicy(
            [(owner, participant)]);

        var first = Assert.IsType<AssemblyBindingSelection.Selected>(
            outer.Select(
                    Request(
                        selected,
                        AssemblyBindingOrigin.Global()))
                .Selection);
        var continued = Assert.IsType<AssemblyBindingSelection.Selected>(
            outer.Select(
                    Request(
                        dependency,
                        AssemblyBindingOrigin.FromOccurrence(
                            first.Occurrence)))
                .Selection);

        Assert.Same(selected, first.Assembly);
        Assert.Same(dependency, continued.Assembly);
    }

    static AssemblyBindingRequest Request(
        AssemblyBindingOrigin origin) =>
        new(
            AssemblyBindingTarget.Reference(
                new AssemblyReferenceIdentity(
                    "Dependency",
                    new Version(1, 0, 0, 0),
                    null,
                    null)),
            origin,
            AssemblyResolutionScope.Any);

    static AssemblyBindingRequest Request(
        ResolvedAssemblyReference target,
        AssemblyBindingOrigin origin) =>
        new(
            AssemblyBindingTarget.Reference(target.Identity),
            origin,
            AssemblyResolutionScope.Any);

    static ResolvedAssemblyReference Descriptor(
        string name,
        AssemblyResolutionProvenance? provenance = null) =>
        ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity(
                name,
                new Version(1, 0, 0, 0),
                null,
                null),
            path: null,
            static () => new MemoryStream(),
            provenance ?? AssemblyResolutionProvenance.Local("test"));

    sealed class RecordingPolicy : IAssemblyBindingPolicy
    {
        internal AssemblyBindingSelection Selection { get; set; } =
            AssemblyBindingSelection.NotFound();
        internal AssemblyBindingRequest? LastRequest { get; private set; }
        internal int SelectionCount { get; private set; }

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            LastRequest = request;
            SelectionCount++;
            return new(Version, Selection);
        }
    }

    sealed class SelectingPolicy(
        Func<AssemblyBindingRequest, AssemblyBindingSelection> select)
        : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request) =>
            new(Version, select(request));
    }

    sealed record TestLineage(
        AssemblyBindingPolicyVersion Version)
        : AssemblyBindingLineage(Version)
    {
        internal AssemblyBindingOccurrence Issue(
            ResolvedAssemblyReference assembly) =>
            CreateOccurrence(assembly);
    }
}
