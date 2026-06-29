using System.Collections.Generic;

using ILInspector.AnalysisHarness;

namespace ILInspector.Analysis.Tests;

public class PrecisionRecallTests
{
    static string SelfAssembly =>
        System.IO.Path.Combine(System.AppContext.BaseDirectory, "ILInspector.Analysis.dll");

    [Fact]
    public void Candidates_RankLoopHighFirstByReach()
    {
        var candidates = PrecisionRecall.Candidates(SelfAssembly);
        Assert.NotEmpty(candidates);
        // The first loop+high candidate must be ranked ahead of any non-(loop+high).
        int firstLoopHigh = candidates.ToList().FindIndex(c => c.InLoop && c.Confidence == "high");
        int firstOther = candidates.ToList().FindIndex(c => !(c.InLoop && c.Confidence == "high"));
        if (firstLoopHigh >= 0 && firstOther >= 0)
            Assert.True(firstLoopHigh < firstOther);
    }

    [Fact]
    public void CheckRecall_PresentReference_Passes()
    {
        // Build a reference from the assembly's own top loop+high site, so it must be present.
        var top = PrecisionRecall.Candidates(SelfAssembly).FirstOrDefault(c => c.InLoop && c.Confidence == "high");
        Assert.NotNull(top); // the anchor must exist, or the test is vacuous
        var reference = new[] { new PaydirtReference("X", top.Type, top.Method, top.Signature, top.Shape) };
        Assert.True(PrecisionRecall.CheckRecall(SelfAssembly, reference).Passed);
    }

    [Fact]
    public void CheckRecall_AbsentReference_IsRegression()
    {
        var reference = new[] { new PaydirtReference("X", "NoSuchType", "NoSuchMethod", "", "box-value-type") };
        var result = PrecisionRecall.CheckRecall(SelfAssembly, reference);
        Assert.False(result.Passed);
        Assert.Single(result.Missing);
    }

    [Fact]
    public void Sample_RespectsTopCount()
        => Assert.True(PrecisionRecall.Sample(SelfAssembly, 3).Candidates.Count <= 3);
}
