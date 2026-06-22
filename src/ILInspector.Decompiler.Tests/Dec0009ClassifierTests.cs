using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public class Dec0009ClassifierTests
{
    static readonly FidelityRemarks.Remark ReadOnlyArrayRemark = new(
        DiagnosticIds.UnrepresentableMetadataName,
        -1,
        "Call compiler-generated read-only array helper",
        "references an unrepresentable metadata name (<>z__ReadOnlyArray)");

    [Fact]
    public void ReadOnlyArrayHelper_IsGeneratedInternalNonActionable()
    {
        var category = Dec0009Classifier.Classify(
            "Library.Type::M",
            [ReadOnlyArrayRemark]);

        Assert.Equal("compiler-generated read-only array helper", category);
        Assert.Equal(
            Dec0009Classifier.GeneratedInternalDisposition,
            Dec0009Classifier.DispositionForCategory(category));
    }

    [Fact]
    public void OtherGeneratedNames_StillRequireTriage()
    {
        var category = Dec0009Classifier.Classify(
            "Library.<>c__DisplayClass0_0::<M>b__0",
            [new FidelityRemarks.Remark(
                DiagnosticIds.UnrepresentableMetadataName,
                -1,
                "DisplayClass",
                "references an unrepresentable metadata name (<>c__DisplayClass0_0)")]);

        Assert.Equal("compiler-generated display class", category);
        Assert.Equal(
            Dec0009Classifier.NeedsTriageDisposition,
            Dec0009Classifier.DispositionForCategory(category));
    }
}
