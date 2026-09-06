using DotnetInspector.Inspectors;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

public class CallerBindingPolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedDependencyRetainsBothSelectingContexts(bool reverse)
    {
        var target = Descriptor("Target");
        var left = Descriptor("Left");
        var right = Descriptor("Right");
        var shared = Descriptor("Shared");
        var leftDependency = Descriptor("Dependency");
        var rightDependency = Descriptor("Dependency");
        var fallback = new SelectionPolicy(_ => AssemblyBindingSelection.NameNotOwned());
        var leftPolicy = Context(shared, leftDependency);
        var rightPolicy = Context(shared, rightDependency);
        int factoryCalls = 0;
        var policy = new ApiMemberAnalysisInspection.CallerBindingPolicy(
            target,
            [target, left, right],
            assembly =>
            {
                factoryCalls++;
                return ReferenceEquals(assembly, left) ? leftPolicy
                    : ReferenceEquals(assembly, right) ? rightPolicy
                    : fallback;
            });
        AssemblyBindingPolicyVersion version = policy.Version;
        Assert.NotSame(fallback.Version, version);
        Assert.NotSame(leftPolicy.Version, version);
        Assert.NotSame(rightPolicy.Version, version);
        var roots = new[] { (left, leftDependency), (right, rightDependency) };
        if (reverse)
            Array.Reverse(roots);

        foreach (var (root, dependency) in roots)
        {
            var selected = Selected(policy.Select(Request(root, shared)));
            var repeated = Selected(policy.Select(Request(root, shared)));
            var continued = Selected(policy.Select(
                Request(selected.Occurrence, dependency)));

            Assert.Same(shared, selected.Assembly);
            Assert.Equal(selected.Occurrence.Lineage, repeated.Occurrence.Lineage);
            Assert.Same(version, selected.Occurrence.Lineage.Version);
            Assert.Same(dependency, continued.Assembly);
            Assert.Same(version, policy.Version);
        }

        Assert.IsType<AssemblyBindingSelection.Missing>(
            policy.Select(Request(shared, leftDependency)).Selection);
        Assert.Equal(3, factoryCalls);
    }

    [Fact]
    public void NestedPolicyContinuationIsPreserved()
    {
        var target = Descriptor("Target");
        var owner = Descriptor("Owner");
        var shared = Descriptor("Shared");
        var dependency = Descriptor("Dependency");
        var missing = new SelectionPolicy(_ => AssemblyBindingSelection.NameNotOwned());
        var inner = new SourceRelativeAssemblyGroupBindingPolicy(
            [(target, (IAssemblyBindingPolicy)missing), (owner, Context(shared, dependency))]);
        var policy = new ApiMemberAnalysisInspection.CallerBindingPolicy(
            target, [owner], _ => inner);

        var selected = Selected(policy.Select(Request(owner, shared)));
        var continued = Selected(policy.Select(Request(selected.Occurrence, dependency)));

        Assert.Same(dependency, continued.Assembly);
        Assert.NotSame(inner.Version, policy.Version);
    }

    [Fact]
    public void SelectedParticipantUsesItsConfiguredContext()
    {
        var target = Descriptor("Target");
        var peer = Descriptor("Peer");
        var dependency = Descriptor("Dependency");
        var selecting = new SelectionPolicy(_ => AssemblyBindingSelection.Found(peer));
        var peerPolicy = new SelectionPolicy(_ => AssemblyBindingSelection.Found(dependency));
        var policy = new ApiMemberAnalysisInspection.CallerBindingPolicy(
            target, [peer], assembly => ReferenceEquals(assembly, peer)
                ? peerPolicy : selecting);

        var selected = Selected(policy.Select(Request(target, peer)));
        var continued = Selected(policy.Select(Request(selected.Occurrence, dependency)));

        Assert.Same(peer, selected.Assembly);
        Assert.Same(dependency, continued.Assembly);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForeignSnapshotRetiresStateBeforeInterpretingPayload(
        bool delegateVersionChanges)
    {
        var target = Descriptor("Target");
        var candidate = Descriptor("Candidate");
        var inner = new SelectionPolicy(_ => AssemblyBindingSelection.Found(candidate));
        int factoryCalls = 0;
        var policy = new ApiMemberAnalysisInspection.CallerBindingPolicy(
            target, [], _ => { factoryCalls++; return inner; });
        var request = Request(target, candidate);
        var selected = Selected(policy.Select(request));
        AssemblyBindingPolicyVersion version = policy.Version;
        var foreign = new AssemblyBindingSelectionSnapshot(
            new AssemblyBindingPolicyVersion(),
            AssemblyBindingSelection.Found(candidate));
        inner.Override = () =>
        {
            if (delegateVersionChanges)
                inner.Version = foreign.Version;
            return foreign;
        };

        Assert.Same(foreign, policy.Select(request));
        Assert.NotSame(version, policy.Version);
        Assert.Equal(1, factoryCalls);
        var retired = Assert.IsType<AssemblyBindingSelection.Rejected>(
            policy.Select(Request(selected.Occurrence, candidate)).Selection);
        Assert.Equal(AssemblyBindingFailureKind.InvalidBindingOrigin, retired.Failure.Kind);

        inner.Override = null;
        AssemblyBindingPolicyVersion refreshed = policy.Version;
        var fresh = policy.Select(request);
        Assert.Same(refreshed, fresh.Version);
        Assert.Same(candidate, Selected(fresh).Assembly);
    }

    [Fact]
    public void CurrentVersionTracksDelegatedChangesAndRejectsOldOrigins()
    {
        var target = Descriptor("Target");
        var candidate = Descriptor("Candidate");
        var inner = new SelectionPolicy(_ => AssemblyBindingSelection.Found(candidate));
        var policy = new ApiMemberAnalysisInspection.CallerBindingPolicy(
            target, [], _ => inner);
        var selected = Selected(policy.Select(Request(target, candidate)));
        AssemblyBindingPolicyVersion original = policy.Version;

        inner.Version = new();
        AssemblyBindingPolicyVersion replacement = policy.Version;
        Assert.NotSame(original, replacement);
        var retired = Assert.IsType<AssemblyBindingSelection.Rejected>(
            policy.Select(Request(selected.Occurrence, candidate)).Selection);
        Assert.Equal(AssemblyBindingFailureKind.InvalidBindingOrigin, retired.Failure.Kind);

        var foreignPolicy = new ApiMemberAnalysisInspection.CallerBindingPolicy(
            target, [], _ => inner);
        var fresh = Selected(policy.Select(Request(target, candidate)));
        var foreign = Assert.IsType<AssemblyBindingSelection.Rejected>(
            foreignPolicy.Select(Request(fresh.Occurrence, candidate)).Selection);
        Assert.Equal(AssemblyBindingFailureKind.InvalidBindingOrigin, foreign.Failure.Kind);
    }

    [Fact]
    public void DelegatedSelectionIsNotReplacedByGroupCandidatePrecedence()
    {
        var target = Descriptor("Target");
        var selected = Descriptor("Target");
        var delegatePolicy = new SelectionPolicy(_ => AssemblyBindingSelection.Found(selected));
        var policy = new ApiMemberAnalysisInspection.CallerBindingPolicy(
            target, [], _ => delegatePolicy);

        Assert.Same(selected, Selected(policy.Select(Request(target, target))).Assembly);
    }

    [Fact]
    public void NullSnapshotRemainsInvalidPolicyResult()
    {
        var target = Descriptor("Target");
        var inner = new SelectionPolicy(_ => AssemblyBindingSelection.NameNotOwned())
        {
            Override = () => null!,
        };
        var policy = new ApiMemberAnalysisInspection.CallerBindingPolicy(
            target, [], _ => inner);

        var rejected = Assert.IsType<AssemblyBindingSelection.Rejected>(
            policy.Select(Request(target, target)).Selection);

        Assert.Equal(AssemblyBindingFailureKind.InvalidPolicyResult, rejected.Failure.Kind);
    }

    static SelectionPolicy Context(
        ResolvedAssemblyReference shared,
        ResolvedAssemblyReference dependency) =>
        new(request => AssemblyBindingSelection.Found(
            request.Target is AssemblyBindingTarget.AssemblyReference
                { Identity.Name: "Shared" } ? shared : dependency));

    static AssemblyBindingSelection.Selected Selected(
        AssemblyBindingSelectionSnapshot snapshot) =>
        Assert.IsType<AssemblyBindingSelection.Selected>(snapshot.Selection);

    static AssemblyBindingRequest Request(
        ResolvedAssemblyReference origin,
        ResolvedAssemblyReference target) =>
        new(
            AssemblyBindingTarget.Reference(target.Identity),
            AssemblyBindingOrigin.FromAssembly(origin),
            AssemblyResolutionScope.Any);

    static AssemblyBindingRequest Request(
        AssemblyBindingOccurrence origin,
        ResolvedAssemblyReference target) =>
        new(
            AssemblyBindingTarget.Reference(target.Identity),
            AssemblyBindingOrigin.FromOccurrence(origin),
            AssemblyResolutionScope.Any);

    static ResolvedAssemblyReference Descriptor(string name) =>
        ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity(name, new Version(1, 0, 0, 0), null, null),
            name + ".dll",
            static () => throw new InvalidOperationException(
                "Caller routing must not open descriptor-only fixtures."),
            AssemblyResolutionProvenance.Local("caller continuation test"));

    sealed class SelectionPolicy(
        Func<AssemblyBindingRequest, AssemblyBindingSelection> select)
        : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; set; } = new();
        internal Func<AssemblyBindingSelectionSnapshot>? Override { get; set; }

        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request) =>
            Override is { } callback
                ? callback()
                : new(Version, select(request));
    }
}
