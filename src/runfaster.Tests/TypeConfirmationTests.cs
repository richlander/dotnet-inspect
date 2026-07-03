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
