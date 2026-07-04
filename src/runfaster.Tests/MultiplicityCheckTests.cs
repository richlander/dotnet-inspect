using System.Collections.Generic;

// Tests for the multiplicity predict-vs-observe signal (issue #2286): the static multiplicity prior
// disambiguates a hot site's cause (intrinsic per-iteration churn vs call-frequency-driven volume)
// and flags loop sites predicted per-iteration but not exercised.
public class MultiplicityCheckTests
{
    [Fact]
    public void LoopMultiplicity_Observed_IsLoopConfirmed()
    {
        var c = Candidate(multiplicity: "Loop");
        c.AllocationHits = 3;
        c.AllocationBytes = 5_000_000;

        Assert.Equal("loop-confirmed", c.MultiplicityCheck);
    }

    [Fact]
    public void OnceMultiplicity_Observed_IsCallFrequency()
    {
        var c = Candidate(multiplicity: "Once");
        c.AllocationHits = 3;
        c.AllocationBytes = 5_000_000;

        Assert.Equal("call-frequency", c.MultiplicityCheck);
    }

    [Fact]
    public void ConditionalMultiplicity_Observed_IsCallFrequency()
    {
        var c = Candidate(multiplicity: "Conditional");
        c.AllocationHits = 1;
        c.AllocationBytes = 1_000_000;

        Assert.Equal("call-frequency", c.MultiplicityCheck);
    }

    [Fact]
    public void LoopMultiplicity_NotObserved_IsLoopCold()
    {
        var c = Candidate(multiplicity: "Loop"); // no runtime hits

        Assert.Equal("loop-cold", c.MultiplicityCheck);
    }

    [Fact]
    public void OnceMultiplicity_NotObserved_IsNull()
    {
        var c = Candidate(multiplicity: "Once"); // cold, not loop -> no signal

        Assert.Null(c.MultiplicityCheck);
    }

    [Fact]
    public void UnknownMultiplicity_IsNullRegardlessOfObservation()
    {
        var cold = Candidate(multiplicity: "Unknown");
        var hot = Candidate(multiplicity: "Unknown");
        hot.AllocationHits = 5;
        hot.AllocationBytes = 9_000_000;

        Assert.Null(cold.Multiplicity);
        Assert.Null(cold.MultiplicityCheck);
        Assert.Null(hot.MultiplicityCheck);
    }

    static AllocationCandidate Candidate(string multiplicity)
        => new(
            id: 1,
            source: "library",
            libraryPath: "/tmp/Fixture.dll",
            assemblyName: "Fixture",
            moduleVersionId: null,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            method: "Fixture.Type.M()",
            methodKey: "Fixture.Type.M",
            methodStackKey: "Fixture.Type::M",
            allocationKind: "Object",
            allocatedType: "Fixture.Allocated",
            detail: null,
            inLoop: multiplicity == "Loop",
            frequency: "Always",
            escape: "Escapes",
            confidence: null,
            fix: null,
            rootReach: null,
            pathKind: null,
            pathConfidence: null,
            postDominance: null,
            estimatedSizeBytes: null,
            sizeTier: null,
            escapeKind: null,
            churnedType: null,
            runtimeAllocationType: null,
            multiplicity: multiplicity);
}
