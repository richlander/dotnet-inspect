using System.Collections.Generic;

// Tests for the type-level confirmation fallback (issue #2264): when a small allocating method is
// inlined, its GCAllocationTick leaf frame belongs to the inliner and the IL-offset site-join misses
// it, but the allocated type is still realized-hot and can confirm the finding by type.
public class TypeConfirmationTests
{
    [Fact]
    public void CanonicalTypeSignature_ReconcilesStaticAngleAndRuntimeBacktickForms()
    {
        var staticForm = ProgramSupport.CanonicalTypeSignature("System.Func<string, System.Lazy<int>>");
        var runtimeForm = ProgramSupport.CanonicalTypeSignature("System.Func`2[System.String,System.Lazy`1[System.Int32]]");

        Assert.Equal(staticForm, runtimeForm);
    }

    [Fact]
    public void CanonicalTypeSignature_DistinguishesDifferentTypeArguments()
    {
        var a = ProgramSupport.CanonicalTypeSignature("System.Func<string, System.Lazy<int>>");
        var b = ProgramSupport.CanonicalTypeSignature("System.Func<string, System.Lazy<long>>");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ApplyTypeConfirmation_MarksUnobservedCandidate_WhenPredictedTypeIsRealizedHot()
    {
        var candidate = CandidateWithType(1, "Aspire.ColorGenerator.GetColorIndex(string)", "System.Func<string, System.Lazy<int>>");
        var result = new CorrelationResult();
        result.Candidates.Add(candidate);
        // Runtime reports the reflection (backtick) form; site-join saw no frame for this method.
        result.RecordTypeVolume("System.Func`2[System.String,System.Lazy`1[System.Int32]]", 700_000_000);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.True(candidate.TypeConfirmed);
        Assert.Equal(700_000_000, candidate.TypeConfirmedBytes);
        Assert.False(candidate.TypeConfirmedAmbiguous);
        Assert.Equal("type-hot", candidate.Status);
    }

    [Fact]
    public void ApplyTypeConfirmation_MarksAmbiguous_WhenMultipleSitesSharePredictedType()
    {
        var one = CandidateWithType(1, "Fixture.A.M()", "System.Func<string, System.Lazy<int>>");
        var two = CandidateWithType(2, "Fixture.B.M()", "System.Func<string, System.Lazy<int>>");
        var result = new CorrelationResult();
        result.Candidates.Add(one);
        result.Candidates.Add(two);
        result.RecordTypeVolume("System.Func`2[System.String,System.Lazy`1[System.Int32]]", 500_000_000);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.True(one.TypeConfirmedAmbiguous);
        Assert.True(two.TypeConfirmedAmbiguous);
        Assert.Equal(2, one.TypeConfirmedSiteCount);
        Assert.Equal("type-hot-ambiguous", one.Status);
    }

    [Fact]
    public void ApplyTypeConfirmation_LeavesColdCandidateCold_WhenTypeNotRealized()
    {
        var candidate = CandidateWithType(1, "Fixture.A.M()", "System.Func<string, System.Lazy<int>>");
        var result = new CorrelationResult();
        result.Candidates.Add(candidate);
        result.RecordTypeVolume("System.String", 900_000_000); // a different type is hot

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.False(candidate.TypeConfirmed);
        Assert.Equal("cold-for-this-workload", candidate.Status);
    }

    [Fact]
    public void CanonicalTypeSignature_DistinguishesArraysFromScalarsAndRanks()
    {
        var scalar = ProgramSupport.CanonicalTypeSignature("int");
        var array = ProgramSupport.CanonicalTypeSignature("int[]");
        var runtimeArray = ProgramSupport.CanonicalTypeSignature("System.Int32[]");
        var jagged = ProgramSupport.CanonicalTypeSignature("int[][]");
        var rank2 = ProgramSupport.CanonicalTypeSignature("int[,]");

        Assert.Equal(array, runtimeArray);       // static alias array == runtime array
        Assert.NotEqual(scalar, array);          // scalar != array
        Assert.NotEqual(array, jagged);          // [] != [][]
        Assert.NotEqual(array, rank2);           // [] != [,]
    }

    [Fact]
    public void CanonicalTypeSignature_DistinguishesLeafNameNamespaceCollisions()
    {
        Assert.NotEqual(
            ProgramSupport.CanonicalTypeSignature("A.Foo"),
            ProgramSupport.CanonicalTypeSignature("B.Foo"));
    }

    [Fact]
    public void CanonicalTypeSignature_PreservesCompilerGeneratedNames()
    {
        // A closure/state-machine name must not collapse to its parent type (previously
        // Enumerable+<>c__DisplayClass18_0 canonicalized to Enumerable), and two distinct closures
        // in the same parent must stay distinct.
        var parent = ProgramSupport.CanonicalTypeSignature("System.Linq.Enumerable");
        var closure = ProgramSupport.CanonicalTypeSignature("System.Linq.Enumerable+<>c__DisplayClass18_0");
        var otherClosure = ProgramSupport.CanonicalTypeSignature("System.Linq.Enumerable+<>c__DisplayClass19_0");

        Assert.NotEqual(parent, closure);
        Assert.NotEqual(closure, otherClosure);
    }

    [Fact]
    public void CanonicalTypeSignature_PreservesNestedTypeAfterGenericArguments()
    {
        // A nested type following a generic parent must not be dropped (previously
        // Dictionary`2[K,V]+KeyCollection canonicalized to Dictionary<K,V>).
        Assert.NotEqual(
            ProgramSupport.CanonicalTypeSignature("System.Collections.Generic.Dictionary`2[K,V]+KeyCollection"),
            ProgramSupport.CanonicalTypeSignature("System.Collections.Generic.Dictionary`2[K,V]"));
    }

    [Fact]
    public void CanonicalTypeSignature_PreservesGenericArgumentOrderAndSeparators()
    {
        // The argument separator must be preserved so different splits do not collide.
        Assert.NotEqual(
            ProgramSupport.CanonicalTypeSignature("Func<AB, C>"),
            ProgramSupport.CanonicalTypeSignature("Func<A, BC>"));
    }

    [Fact]
    public void CanonicalTypeSignature_PreservesUnboundGenericArity()
    {
        var arity2 = ProgramSupport.CanonicalTypeSignature("System.String<,>");
        var arity0 = ProgramSupport.CanonicalTypeSignature("System.String<>");
        var arity3 = ProgramSupport.CanonicalTypeSignature("System.String<,,>");

        Assert.NotEqual(arity2, arity0);
        Assert.NotEqual(arity2, arity3);
    }

    [Fact]
    public void CanonicalTypeSignature_ReconcilesArrayOfGenericAcrossForms()
    {
        Assert.Equal(
            ProgramSupport.CanonicalTypeSignature("Entry<System.String,System.Object>[]"),
            ProgramSupport.CanonicalTypeSignature("Entry`2[System.String,System.Object][]"));
    }

    [Fact]
    public void ApplyTypeConfirmation_DoesNotConfirmColdSite_WhenTypeAlreadySiteObserved()
    {
        // A hot site-observed candidate explains the String volume; a cold same-type site must not
        // steal that credit as a "unique" type-hot confirmation.
        var observed = CandidateWithType(1, "Fixture.Hot.M()", "System.String");
        observed.AllocationHits = 1;
        observed.AllocationBytes = 900_000_000;
        var cold = CandidateWithType(2, "Fixture.Cold.M()", "System.String");
        var result = new CorrelationResult();
        result.Candidates.Add(observed);
        result.Candidates.Add(cold);
        result.RecordTypeVolume("System.String", 900_000_000);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.False(cold.TypeConfirmed);
        Assert.Equal("cold-for-this-workload", cold.Status);
    }

    [Fact]
    public void ApplyTypeConfirmation_DoesNotConfirm_BelowVolumeFloor()
    {
        var candidate = CandidateWithType(1, "Fixture.A.M()", "System.Func<string, System.Lazy<int>>");
        var result = new CorrelationResult();
        result.Candidates.Add(candidate);
        result.RecordTypeVolume("System.Func`2[System.String,System.Lazy`1[System.Int32]]", ProgramSupport.TypeConfirmMinBytes - 1);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.False(candidate.TypeConfirmed);
    }

    [Fact]
    public void ApplyTypeConfirmation_DoesNotConfirm_WhenTooManySitesShareType()
    {
        var result = new CorrelationResult();
        for (int i = 0; i < ProgramSupport.TypeConfirmMaxSites + 1; i++)
            result.Candidates.Add(CandidateWithType(i + 1, $"Fixture.T{i}.M()", "System.String"));
        result.RecordTypeVolume("System.String", 900_000_000);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.All(result.Candidates, c => Assert.False(c.TypeConfirmed));
    }

    static AllocationCandidate CandidateWithType(int id, string method, string predictedType)
    {
        string methodKey = method[..method.IndexOf('(')];
        int lastDot = methodKey.LastIndexOf('.');
        string stackKey = lastDot < 0 ? methodKey : $"{methodKey[..lastDot]}::{methodKey[(lastDot + 1)..]}";
        return new(
            id,
            "library",
            "/tmp/Fixture.dll",
            "Fixture",
            null,
            0x06000001,
            0x0010,
            method,
            methodKey,
            stackKey,
            "Delegate",
            predictedType,
            null,
            false,
            "Always",
            "Escapes",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
    }
}
