namespace ILInspector.Decompiler.Tests;

public class BodyShapeFidelitySummaryTests
{
    [Fact]
    public void RepeatedCauseSites_ShowFirstLocationOnce()
    {
        var summary = BodyShapeSearch.FidelityCauseSummary([
            Cause("same", 4), Cause("same", 12), Cause("next", 16),
        ]);
        Assert.Equal("DEC0009 [state-machine-type-name] at IL_0004: same; DEC0009 [state-machine-type-name] at IL_0010: next", summary);
    }

    [Fact]
    public void DistinctCauses_AreBoundedInProducerOrder()
    {
        var summary = BodyShapeSearch.FidelityCauseSummary([
            Cause("first", 1), Cause("second", 2), Cause("third", 3), Cause("fourth", 4),
        ]);
        Assert.Contains("first", summary);
        Assert.Contains("second", summary);
        Assert.Contains("third", summary);
        Assert.DoesNotContain("fourth", summary);
        Assert.EndsWith("See member Fidelity Causes for omitted details.", summary);
    }

    [Fact]
    public void LongCause_IsTruncatedWithFullCensusGuidance()
    {
        var summary = BodyShapeSearch.FidelityCauseSummary([Cause(new string('x', 1000), 1)]);
        Assert.StartsWith("DEC0009 [state-machine-type-name] at IL_0001:", summary);
        Assert.Equal(240, summary.IndexOf(';'));
        Assert.EndsWith("...; See member Fidelity Causes for omitted details.", summary);
    }

    [Fact]
    public void EmptyCensus_HasNoSummary()
        => Assert.Empty(BodyShapeSearch.FidelityCauseSummary([]));

    static DecompilerFidelityCause Cause(string reason, int offset)
        => new("DEC0009", DecompilerFidelityLocation.AtIlOffset(offset), "Call", "Call M", reason,
            DecompilerFidelityDiscriminators.StateMachineTypeName, null);
}
