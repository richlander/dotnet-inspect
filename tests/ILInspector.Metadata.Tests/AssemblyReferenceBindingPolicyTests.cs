using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public class AssemblyReferenceBindingPolicyTests
{
    static readonly AssemblyReferenceIdentity Reference =
        new("Dependency", new Version(1, 2, 3, 4), "neutral", "0102030405060708");

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
    public void CoreLibrary_RequiresExplicitCandidates()
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
    public void CoreLibrary_ProbesExplicitCandidatesInOrderAndSnapshotsMiss()
    {
        var first = new AssemblyReferenceIdentity(
            "System.Runtime",
            Version: null,
            Culture: null,
            PublicKeyToken: null);
        var second = new AssemblyReferenceIdentity(
            "System.Private.CoreLib",
            Version: null,
            Culture: null,
            PublicKeyToken: null);
        var resolver = new RecordingResolver((_, _) => null);
        var policy = new AssemblyReferenceBindingPolicy(
            resolver,
            [first, second]);
        AssemblyBindingRequest request =
            Request(AssemblyBindingTarget.CoreLibrary());

        Assert.IsType<AssemblyBindingSelection.Missing>(policy.Select(request));
        Assert.IsType<AssemblyBindingSelection.Missing>(policy.Select(request));
        Assert.Equal([first, second], resolver.Requests.Select(r => r.Identity));
    }

    static AssemblyBindingRequest Request(AssemblyBindingTarget target) =>
        new(
            target,
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Any);

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
}
