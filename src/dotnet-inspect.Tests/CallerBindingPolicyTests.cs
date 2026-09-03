using DotnetInspector.Inspectors;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

public class CallerBindingPolicyTests
{
    [Fact]
    public void ConcurrentLearnedRoutesAreBothRetained()
    {
        ResolvedAssemblyReference defaultOwner =
            Descriptor("Default.Owner");
        ResolvedAssemblyReference firstOwner =
            Descriptor("First.Owner");
        ResolvedAssemblyReference secondOwner =
            Descriptor("Second.Owner");
        ResolvedAssemblyReference firstCandidate =
            Descriptor("First.Candidate");
        ResolvedAssemblyReference secondCandidate =
            Descriptor("Second.Candidate");
        using var stateCaptureBarrier = new Barrier(2);
        int stateCaptureCount = 0;
        int firstCandidateFactoryCalls = 0;
        int secondCandidateFactoryCalls = 0;
        var defaultPolicy = new FixedSelectionPolicy(
            AssemblyBindingSelection.NameNotOwned());
        var firstPolicy = new FixedSelectionPolicy(
            AssemblyBindingSelection.Found(firstCandidate));
        var secondPolicy = new FixedSelectionPolicy(
            AssemblyBindingSelection.Found(secondCandidate));
        var firstCandidatePolicy = new FixedSelectionPolicy(
            AssemblyBindingSelection.NameNotOwned());
        var secondCandidatePolicy = new FixedSelectionPolicy(
            AssemblyBindingSelection.NameNotOwned());
        var candidatePolicies = new Dictionary<
            AssemblyAcquisitionRegistration,
            FixedSelectionPolicy>(
                ReferenceEqualityComparer.Instance);
        candidatePolicies.Add(
            firstCandidate.Registration,
            firstCandidatePolicy);
        candidatePolicies.Add(
            secondCandidate.Registration,
            secondCandidatePolicy);
        var policy =
            new ApiMemberAnalysisInspection.CallerBindingPolicy(
                defaultOwner,
                [firstOwner, secondOwner],
                assembly =>
                {
                    if (ReferenceEquals(assembly, defaultOwner))
                        return defaultPolicy;
                    if (ReferenceEquals(assembly, firstOwner))
                        return firstPolicy;
                    if (ReferenceEquals(assembly, secondOwner))
                        return secondPolicy;
                    if (candidatePolicies.TryGetValue(
                            assembly.Registration,
                            out FixedSelectionPolicy? candidatePolicy))
                    {
                        if (ReferenceEquals(assembly, firstCandidate))
                        {
                            Interlocked.Increment(
                                ref firstCandidateFactoryCalls);
                        }
                        else if (ReferenceEquals(
                            assembly,
                            secondCandidate))
                        {
                            Interlocked.Increment(
                                ref secondCandidateFactoryCalls);
                        }
                        return candidatePolicy;
                    }
                    throw new InvalidOperationException(
                        $"Unexpected route for {assembly.Identity.Name}.");
                },
                () =>
                {
                    int capture = Interlocked.Increment(
                        ref stateCaptureCount);
                    if (capture <= 2
                        && !stateCaptureBarrier.SignalAndWait(
                            TimeSpan.FromSeconds(30)))
                    {
                        throw new TimeoutException(
                            "Concurrent route publication did not capture the same initial state.");
                    }
                });
        AssemblyBindingPolicyVersion version = policy.Version;

        AssemblyBindingSelection? firstSelection = null;
        AssemblyBindingSelection? secondSelection = null;
        Exception? firstFailure = null;
        Exception? secondFailure = null;
        var firstThread = new Thread(
            () =>
            {
                try
                {
                    firstSelection = policy.Select(
                        Request(firstOwner, firstCandidate.Identity));
                }
                catch (Exception ex)
                {
                    firstFailure = ex;
                }
            })
        {
            IsBackground = true,
        };
        var secondThread = new Thread(
            () =>
            {
                try
                {
                    secondSelection = policy.Select(
                        Request(secondOwner, secondCandidate.Identity));
                }
                catch (Exception ex)
                {
                    secondFailure = ex;
                }
            })
        {
            IsBackground = true,
        };

        firstThread.Start();
        secondThread.Start();

        Assert.True(firstThread.Join(TimeSpan.FromSeconds(30)));
        Assert.True(secondThread.Join(TimeSpan.FromSeconds(30)));
        Assert.Null(firstFailure);
        Assert.Null(secondFailure);

        Assert.Same(
            firstCandidate,
            Assert.IsType<AssemblyBindingSelection.Selected>(
                firstSelection).Assembly);
        Assert.Same(
            secondCandidate,
            Assert.IsType<AssemblyBindingSelection.Selected>(
                secondSelection).Assembly);

        AssemblyReferenceIdentity probe = Identity("Probe");
        Assert.IsType<AssemblyBindingSelection.Missing>(
            policy.Select(Request(firstCandidate, probe)));
        Assert.IsType<AssemblyBindingSelection.Missing>(
            policy.Select(Request(secondCandidate, probe)));

        Assert.IsType<AssemblyBindingSelection.Selected>(
            policy.Select(Request(firstOwner, firstCandidate.Identity)));
        Assert.IsType<AssemblyBindingSelection.Missing>(
            policy.Select(Request(firstCandidate, probe)));

        Assert.Same(version, policy.Version);
        Assert.Equal(0, defaultPolicy.CallCount);
        Assert.Equal(2, firstPolicy.CallCount);
        Assert.Equal(1, secondPolicy.CallCount);
        Assert.Equal(2, firstCandidatePolicy.CallCount);
        Assert.Equal(1, secondCandidatePolicy.CallCount);
        Assert.Equal(1, firstCandidateFactoryCalls);
        Assert.Equal(1, secondCandidateFactoryCalls);
    }

    static AssemblyBindingRequest Request(
        ResolvedAssemblyReference origin,
        AssemblyReferenceIdentity target) =>
        new(
            AssemblyBindingTarget.Reference(target),
            AssemblyBindingOrigin.FromAssembly(origin),
            AssemblyResolutionScope.Any);

    static ResolvedAssemblyReference Descriptor(string name)
    {
        string path = typeof(CallerBindingPolicyTests)
            .Assembly.Location;
        return ResolvedAssemblyReference.Create(
            Identity(name),
            path,
            () => File.OpenRead(path),
            AssemblyResolutionProvenance.Local(
                "caller binding route-state test"));
    }

    static AssemblyReferenceIdentity Identity(string name) =>
        new(
            name,
            new Version(1, 0, 0, 0),
            null,
            null);

    sealed class FixedSelectionPolicy(
        AssemblyBindingSelection selection)
        : IAssemblyBindingPolicy
    {
        int _callCount;

        internal int CallCount => Volatile.Read(ref _callCount);

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request)
        {
            Interlocked.Increment(ref _callCount);
            return selection;
        }
    }

}
