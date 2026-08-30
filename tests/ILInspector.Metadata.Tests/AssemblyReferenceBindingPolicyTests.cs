using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public class AssemblyReferenceBindingPolicyTests
{
    static readonly AssemblyReferenceIdentity Reference =
        new("Dependency", new Version(1, 2, 3, 4), "neutral", "0102030405060708");

    [Fact]
    public void MetadataIdentityEquivalence_NormalizesCaseAndNeutralCulture()
    {
        Assert.True(Reference.IsEquivalentTo(new AssemblyReferenceIdentity(
            "dependency",
            new Version(1, 2, 3, 4),
            Culture: null,
            PublicKeyToken: "0102030405060708".ToUpperInvariant())));
        Assert.False(Reference.IsEquivalentTo(Reference with
        {
            Version = new Version(2, 0, 0, 0),
        }));
    }

    [Fact]
    public void Select_SnapshotsResolverAnswer()
    {
        var resolver = new RecordingResolver((_, _) => null);
        var policy = new AssemblyReferenceBindingPolicy(resolver);
        var firstRequest = Request(AssemblyBindingTarget.Reference(Reference));
        var equivalentRequest = Request(AssemblyBindingTarget.Reference(Reference));

        AssemblyBindingSelection first = policy.Select(firstRequest);
        AssemblyBindingSelection second = policy.Select(equivalentRequest);

        Assert.Same(first, second);
        Assert.IsType<AssemblyBindingSelection.Missing>(first);
        Assert.Equal(Reference, Assert.Single(resolver.Requests).Identity);
    }

    [Fact]
    public void Select_MapsRecoverableResolverFailuresToUnavailable()
    {
        Exception[] failures =
        [
            new IOException("unavailable"),
            new UnauthorizedAccessException("unavailable"),
            new BadImageFormatException("unavailable"),
            new InvalidOperationException("unavailable"),
            new NotSupportedException("unavailable"),
            new ArgumentException("unavailable"),
        ];

        foreach (Exception failure in failures)
        {
            var policy = new AssemblyReferenceBindingPolicy(
                new RecordingResolver((_, _) => throw failure));
            var unavailable =
                Assert.IsType<AssemblyBindingSelection.Unavailable>(
                    policy.Select(
                        Request(
                            AssemblyBindingTarget.Reference(Reference))));

            Assert.Equal(
                AssemblyBindingFailureKind.CandidateUnavailable,
                unavailable.Failure.Kind);
        }
    }

    [Fact]
    public void Select_PreservesMetadataAdmissionFailuresAcrossCachedRequests()
    {
        Exception[] failures =
        [
            new UnsupportedMetadataFormatException(),
            new MalformedMetadataRootException(
                MetadataRootMalformedReason.InvalidSignature),
        ];

        foreach (Exception failure in failures)
        {
            var policy = new AssemblyReferenceBindingPolicy(
                new RecordingResolver((_, _) => throw failure));
            AssemblyBindingRequest request = Request(
                AssemblyBindingTarget.Reference(Reference));

            Exception first = Assert.Throws(
                failure.GetType(),
                () => policy.Select(request));
            Exception second = Assert.Throws(
                failure.GetType(),
                () => policy.Select(request));

            Assert.Same(failure, first);
            Assert.Same(failure, second);
        }
    }

    [Fact]
    public void CoreLibrary_IsUnavailableThroughReferenceResolverAdapter()
    {
        var policy = new AssemblyReferenceBindingPolicy(
            new RecordingResolver((_, _) => null));

        var unavailable = Assert.IsType<AssemblyBindingSelection.Unavailable>(
            policy.Select(Request(AssemblyBindingTarget.CoreLibrary())));

        Assert.Equal(
            AssemblyBindingFailureKind.UnsupportedScope,
            unavailable.Failure.Kind);
    }

    [Fact]
    public void Select_PreservesBindingPolicyShadows()
    {
        ResolvedAssemblyReference selected = Descriptor(
            AssemblyResolutionProvenance.Designated("selected"));
        ResolvedAssemblyReference shadow = Descriptor(
            AssemblyResolutionProvenance.Platform(
                "platform",
                frameworkVersion: null,
                "shadow"));
        var resolver = new RecordingResolverPolicy(
            AssemblyBindingSelection.Found(selected, [shadow]));
        var policy = new AssemblyReferenceBindingPolicy(resolver);

        var result = Assert.IsType<AssemblyBindingSelection.Selected>(
            policy.Select(
                Request(AssemblyBindingTarget.Reference(Reference))));

        Assert.Same(selected, result.Assembly);
        Assert.Same(shadow, Assert.Single(result.ShadowedAssemblies));
        Assert.Equal(1, resolver.SelectionCount);
        Assert.Equal(0, resolver.ResolutionCount);
    }

    [Fact]
    public void Select_PreservesBindingPolicyAmbiguity()
    {
        ResolvedAssemblyReference first = Descriptor(
            AssemblyResolutionProvenance.Designated("first"));
        ResolvedAssemblyReference second = Descriptor(
            AssemblyResolutionProvenance.Designated("second"));
        var resolver = new RecordingResolverPolicy(
            AssemblyBindingSelection.Multiple([first, second]));
        var policy = new AssemblyReferenceBindingPolicy(resolver);

        var result = Assert.IsType<AssemblyBindingSelection.Ambiguous>(
            policy.Select(
                Request(AssemblyBindingTarget.Reference(Reference))));

        Assert.Equal([first, second], result.Assemblies);
        Assert.Equal(1, resolver.SelectionCount);
        Assert.Equal(0, resolver.ResolutionCount);
    }

    [Fact]
    public void NoResolverPolicy_NeverSelectsAnyBindingTarget()
    {
        Assert.IsType<AssemblyBindingSelection.Missing>(
            NoResolverAssemblyBindingPolicy.Instance.Select(
                Request(AssemblyBindingTarget.Reference(Reference))));
        var unavailable = Assert.IsType<AssemblyBindingSelection.Unavailable>(
            NoResolverAssemblyBindingPolicy.Instance.Select(
                Request(AssemblyBindingTarget.CoreLibrary())));

        Assert.Equal(
            AssemblyBindingFailureKind.UnsupportedScope,
            unavailable.Failure.Kind);
        Assert.Same(
            NoResolverAssemblyBindingPolicy.Instance.Version,
            NoResolverAssemblyBindingPolicy.Instance.Version);
    }

    static AssemblyBindingRequest Request(AssemblyBindingTarget target) =>
        new(
            target,
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Any);

    static ResolvedAssemblyReference Descriptor(
        AssemblyResolutionProvenance provenance) =>
        ResolvedAssemblyReference.Create(
            Reference,
            path: null,
            () => throw new InvalidOperationException(
                "The adapter must not open binding descriptors."),
            provenance);

    sealed class RecordingResolver(
        Func<AssemblyReferenceIdentity, AssemblyResolutionScope, ResolvedAssemblyReference?> resolve)
        : IAssemblyReferenceResolver
    {
        public List<(AssemblyReferenceIdentity Identity, AssemblyResolutionScope Scope)> Requests { get; } = [];

        public ResolvedAssemblyReference? Resolve(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope)
        {
            Requests.Add((identity, scope));
            return resolve(identity, scope);
        }
    }

    sealed class RecordingResolverPolicy(
        AssemblyBindingSelection selection)
        : IAssemblyReferenceResolver, IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();
        public int ResolutionCount { get; private set; }
        public int SelectionCount { get; private set; }

        public ResolvedAssemblyReference? Resolve(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope)
        {
            ResolutionCount++;
            return null;
        }

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request)
        {
            SelectionCount++;
            return selection;
        }
    }
}
