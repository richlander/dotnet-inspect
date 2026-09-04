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
    public void AssemblyReferenceBindingPolicy_NullRemainsUndifferentiated()
    {
        var resolver = new RecordingResolver((_, _) => null);
        var policy = new AssemblyReferenceBindingPolicy(resolver);
        AssemblyBindingPolicyVersion version = policy.Version;
        var firstRequest = Request(AssemblyBindingTarget.Reference(Reference));
        var equivalentRequest = Request(AssemblyBindingTarget.Reference(Reference));

        AssemblyBindingSelectionSnapshot first = policy.Select(firstRequest);
        AssemblyBindingSelectionSnapshot second =
            policy.Select(equivalentRequest);

        Assert.Same(first, second);
        Assert.Same(version, first.Version);
        Assert.Equal(
            AssemblyBindingMissDisposition.Undifferentiated,
            Assert.IsType<AssemblyBindingSelection.Missing>(first.Selection)
                .Disposition);
        Assert.Equal(Reference, Assert.Single(resolver.Requests).Identity);
        Assert.Same(version, policy.Version);
    }

    [Fact]
    public void AssemblyBindingMissDisposition_FactoriesExposeClosedArms()
    {
        Assert.Equal(
            AssemblyBindingMissDisposition.Undifferentiated,
            Assert.IsType<AssemblyBindingSelection.Missing>(
                AssemblyBindingSelection.NotFound()).Disposition);
        Assert.Equal(
            AssemblyBindingMissDisposition.NoNameOwner,
            Assert.IsType<AssemblyBindingSelection.Missing>(
                AssemblyBindingSelection.NameNotOwned()).Disposition);
        Assert.Equal(
            AssemblyBindingMissDisposition.NameOwnedNoMatch,
            Assert.IsType<AssemblyBindingSelection.Missing>(
                AssemblyBindingSelection.NameOwnedButNoMatch()).Disposition);
        Assert.Empty(
            typeof(AssemblyBindingSelection.Missing).GetConstructors());
    }

    [Fact]
    public void AssemblyBindingSelectionSnapshot_RequiresBothComponents()
    {
        var version = new AssemblyBindingPolicyVersion();
        AssemblyBindingSelection selection =
            AssemblyBindingSelection.NameNotOwned();

        Assert.Throws<ArgumentNullException>(
            () => new AssemblyBindingSelectionSnapshot(null!, selection));
        Assert.Throws<ArgumentNullException>(
            () => new AssemblyBindingSelectionSnapshot(version, null!));
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
                            AssemblyBindingTarget.Reference(Reference)))
                        .Selection);

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
        var resolver = new RecordingResolver((_, _) => null);
        var policy = new AssemblyReferenceBindingPolicy(resolver);

        var unavailable = Assert.IsType<AssemblyBindingSelection.Unavailable>(
            policy.Select(Request(AssemblyBindingTarget.CoreLibrary()))
                .Selection);

        Assert.Equal(
            AssemblyBindingFailureKind.UnsupportedScope,
            unavailable.Failure.Kind);
        Assert.Empty(resolver.Requests);
    }

    [Fact]
    public void AssemblyReferenceBindingPolicy_PreservesDelegatedSelection()
    {
        ResolvedAssemblyReference assembly = Descriptor(
            AssemblyResolutionProvenance.Designated("selected"));
        AssemblyBindingSelection referenceSelection =
            AssemblyBindingSelection.Found(assembly);
        AssemblyBindingSelection coreSelection =
            AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.UnsupportedScope));
        var resolver = new RecordingResolverPolicy(
            request => request.Target
                    is AssemblyBindingTarget.AssemblyReference
                ? referenceSelection
                : coreSelection);
        var policy = new AssemblyReferenceBindingPolicy(resolver);
        AssemblyBindingRequest referenceRequest =
            Request(AssemblyBindingTarget.Reference(Reference));
        AssemblyBindingRequest coreRequest =
            Request(AssemblyBindingTarget.CoreLibrary());

        AssemblyBindingSelectionSnapshot first =
            policy.Select(referenceRequest);
        AssemblyBindingSelectionSnapshot second =
            policy.Select(referenceRequest);
        AssemblyBindingSelectionSnapshot core = policy.Select(coreRequest);

        Assert.Same(referenceSelection, first.Selection);
        Assert.Same(referenceSelection, second.Selection);
        Assert.Same(coreSelection, core.Selection);
        Assert.Same(resolver.Version, first.Version);
        Assert.Same(resolver.Version, second.Version);
        Assert.Same(resolver.Version, core.Version);
        Assert.Equal(
            [referenceRequest, referenceRequest, coreRequest],
            resolver.Requests);
        Assert.Equal(3, resolver.SelectionCount);
        Assert.Equal(0, resolver.ResolutionCount);
    }

    [Fact]
    public void Select_PreservesBindingPolicyIntrinsicSelection()
    {
        ResolvedAssemblyReference selected = Descriptor(
            AssemblyResolutionProvenance.Platform(
                "platform",
                frameworkVersion: null,
                "intrinsic"));
        var resolver = new RecordingResolverPolicy(
            _ => AssemblyBindingSelection.Found(selected));
        var policy = new AssemblyReferenceBindingPolicy(resolver);

        var result = Assert.IsType<AssemblyBindingSelection.Selected>(
            policy.Select(Request(AssemblyBindingTarget.CoreLibrary()))
                .Selection);

        Assert.Same(selected, result.Assembly);
        Assert.Equal(1, resolver.SelectionCount);
        Assert.Equal(0, resolver.ResolutionCount);
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
            _ => AssemblyBindingSelection.Found(selected, [shadow]));
        var policy = new AssemblyReferenceBindingPolicy(resolver);

        var result = Assert.IsType<AssemblyBindingSelection.Selected>(
            policy.Select(
                Request(AssemblyBindingTarget.Reference(Reference)))
                .Selection);

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
            _ => AssemblyBindingSelection.Multiple([first, second]));
        var policy = new AssemblyReferenceBindingPolicy(resolver);

        var result = Assert.IsType<AssemblyBindingSelection.Ambiguous>(
            policy.Select(
                Request(AssemblyBindingTarget.Reference(Reference)))
                .Selection);

        Assert.Equal([first, second], result.Assemblies);
        Assert.Equal(1, resolver.SelectionCount);
        Assert.Equal(0, resolver.ResolutionCount);
    }

    [Fact]
    public void StructuredPolicy_ExposesCurrentDelegateVersion()
    {
        var resolver = new RecordingResolverPolicy(
            _ => AssemblyBindingSelection.NotFound());
        var policy = new AssemblyReferenceBindingPolicy(resolver);

        Assert.Same(resolver.Version, policy.Version);

        resolver.AdvanceVersion();

        Assert.Same(resolver.Version, policy.Version);
    }

    [Fact]
    public void StructuredPolicy_ExceptionPropagatesUnchanged()
    {
        var expected = new IOException("delegate failure");
        var resolver = new RecordingResolverPolicy(_ => throw expected);
        var policy = new AssemblyReferenceBindingPolicy(resolver);

        Exception actual = Assert.Throws<IOException>(
            () => policy.Select(
                Request(AssemblyBindingTarget.Reference(Reference))));

        Assert.Same(expected, actual);
        Assert.Equal(1, resolver.SelectionCount);
        Assert.Equal(0, resolver.ResolutionCount);
    }

    [Fact]
    public void ValidateForRequest_RejectsMissForIntrinsicTarget()
    {
        AssemblyBindingRequest request =
            Request(AssemblyBindingTarget.CoreLibrary());

        var rejected = Assert.IsType<AssemblyBindingSelection.Rejected>(
            AssemblyBindingSelection.ValidateForRequest(
                request,
                AssemblyBindingSelection.NameNotOwned()));

        Assert.Equal(
            AssemblyBindingFailureKind.InvalidPolicyResult,
            rejected.Failure.Kind);
    }

    [Fact]
    public void ValidateForRequest_RejectsNullPolicyResult()
    {
        AssemblyBindingRequest request =
            Request(AssemblyBindingTarget.Reference(Reference));

        var rejected = Assert.IsType<AssemblyBindingSelection.Rejected>(
            AssemblyBindingSelection.ValidateForRequest(
                request,
                selection: null));

        Assert.Equal(
            AssemblyBindingFailureKind.InvalidPolicyResult,
            rejected.Failure.Kind);
    }

    [Fact]
    public void ValidateForRequest_PreservesNonMissingSelectionKinds()
    {
        AssemblyBindingRequest request =
            Request(AssemblyBindingTarget.Reference(Reference));
        ResolvedAssemblyReference first = Descriptor(
            AssemblyResolutionProvenance.Designated("first"));
        ResolvedAssemblyReference second = Descriptor(
            AssemblyResolutionProvenance.Designated("second"));
        AssemblyBindingSelection[] selections =
        [
            AssemblyBindingSelection.NameNotOwned(),
            AssemblyBindingSelection.Found(first),
            AssemblyBindingSelection.Multiple([first, second]),
            AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.CandidateUnavailable)),
            AssemblyBindingSelection.Invalid(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.InvalidPolicyResult)),
        ];

        foreach (AssemblyBindingSelection selection in selections)
        {
            Assert.Same(
                selection,
                AssemblyBindingSelection.ValidateForRequest(
                    request,
                    selection));
        }
    }

    [Fact]
    public void StructuredPolicy_ForwardsSelectionBeforeFinalValidation()
    {
        AssemblyBindingSelection missing =
            AssemblyBindingSelection.NameNotOwned();
        var resolver = new RecordingResolverPolicy(_ => missing);
        var policy = new AssemblyReferenceBindingPolicy(resolver);
        AssemblyBindingRequest request =
            Request(AssemblyBindingTarget.CoreLibrary());

        AssemblyBindingSelectionSnapshot snapshot = policy.Select(request);

        Assert.Same(missing, snapshot.Selection);
        Assert.Same(resolver.LastSnapshot, snapshot);
        var rejected = Assert.IsType<AssemblyBindingSelection.Rejected>(
            AssemblyBindingSelection.ValidateForRequest(
                request,
                snapshot.Selection));
        Assert.Equal(
            AssemblyBindingFailureKind.InvalidPolicyResult,
            rejected.Failure.Kind);
        Assert.Equal(1, resolver.SelectionCount);
        Assert.Equal(0, resolver.ResolutionCount);
    }

    [Fact]
    public void NoResolverAssemblyBindingPolicy_ReportsNoNameOwner()
    {
        var missing =
            Assert.IsType<AssemblyBindingSelection.Missing>(
                NoResolverAssemblyBindingPolicy.Instance.Select(
                    Request(AssemblyBindingTarget.Reference(Reference)))
                    .Selection);
        var unavailable = Assert.IsType<AssemblyBindingSelection.Unavailable>(
            NoResolverAssemblyBindingPolicy.Instance.Select(
                Request(AssemblyBindingTarget.CoreLibrary())).Selection);

        Assert.Equal(
            AssemblyBindingMissDisposition.NoNameOwner,
            missing.Disposition);
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
        Func<AssemblyBindingRequest, AssemblyBindingSelection> select)
        : IAssemblyReferenceResolver, IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; private set; } =
            new();
        public List<AssemblyBindingRequest> Requests { get; } = [];
        public int ResolutionCount { get; private set; }
        public int SelectionCount { get; private set; }
        public AssemblyBindingSelectionSnapshot? LastSnapshot
        {
            get;
            private set;
        }

        public ResolvedAssemblyReference? Resolve(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope)
        {
            ResolutionCount++;
            return null;
        }

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            LastSnapshot = new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());
            return LastSnapshot;

            AssemblyBindingSelection SelectCore()
            {
                SelectionCount++;
                Requests.Add(request);
                return select(request);

            }
        }

        public void AdvanceVersion() =>
            Version = new AssemblyBindingPolicyVersion();
    }

}
