using DotnetInspector.Fixtures;

namespace ILInspector.Analysis.Tests;

public sealed class ArrayPoolOwnershipFlowTests
{
    static string CallerPath =>
        FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath();

    [Fact]
    public void ScopedOwnershipFlowRetainsTheRentBoundary()
    {
        int root = MethodToken("RentAndReturnThroughHelper");

        LibraryBodyIndex index = LibraryBodyIndex.Open(
            CallerPath,
            LibraryBodyAnalysisFeatures.OwnershipFlow,
            bodyScope: new HashSet<int> { root });

        ArrayPoolOwnershipMethodEvidence evidence =
            Assert.Single(index.ArrayPoolOwnership);
        Assert.Equal(root, evidence.Method.MetadataToken);
        ArrayPoolRentOwnership rent = Assert.Single(evidence.Rents);
        ArrayPoolOwnershipUse use = Assert.Single(rent.Uses);
        Assert.True(rent.IsComplete);
        Assert.Equal(ArrayPoolOwnershipUseKind.Forwarded, use.Kind);
        Assert.Equal(0, use.CalleeParameterIndex);
        Assert.Equal(
            "ReturnRentedArray",
            Assert.IsType<DirectCall>(use.Call).Callee.Name);
    }

    [Theory]
    [InlineData(
        "ReturnRentedArray",
        ArrayPoolOwnershipUseKind.ReturnedToPool)]
    [InlineData(
        "ForwardRentedArray",
        ArrayPoolOwnershipUseKind.Forwarded)]
    [InlineData(
        "StoreRentedArray",
        ArrayPoolOwnershipUseKind.Stored)]
    [InlineData(
        "ReturnRentedArrayToCaller",
        ArrayPoolOwnershipUseKind.ReturnedToCaller)]
    public void FullOwnershipFlowClassifiesParameterEffects(
        string methodName,
        ArrayPoolOwnershipUseKind expected)
    {
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            CallerPath,
            LibraryBodyAnalysisFeatures.OwnershipFlow);

        ArrayPoolOwnershipMethodEvidence evidence =
            index.ArrayPoolOwnership.Single(candidate =>
                candidate.Method.Name == methodName);
        ArrayPoolParameterOwnership parameter =
            Assert.Single(evidence.Parameters);
        ArrayPoolOwnershipUse use = Assert.Single(parameter.Uses);
        Assert.True(evidence.IsComplete);
        Assert.True(parameter.IsComplete);
        Assert.Equal(0, parameter.ParameterIndex);
        Assert.Equal(expected, use.Kind);
    }

    [Fact]
    public void MethodEvidenceDoesNotRunOwnershipFlow()
    {
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            CallerPath,
            LibraryBodyAnalysisFeatures.MethodEvidence);

        Assert.Empty(index.ArrayPoolOwnership);
    }

    [Fact]
    public void AddressTakenRentIsRetainedAsIncomplete()
    {
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            CallerPath,
            LibraryBodyAnalysisFeatures.OwnershipFlow);

        ArrayPoolOwnershipMethodEvidence evidence =
            index.ArrayPoolOwnership.Single(candidate =>
                candidate.Method.Name == "RentAndTakeAddress");
        ArrayPoolRentOwnership rent = Assert.Single(evidence.Rents);
        Assert.False(rent.IsComplete);
        Assert.Empty(rent.Uses);
    }

    static int MethodToken(string methodName) =>
        LibraryBodyIndex.Open(
                CallerPath,
                LibraryBodyAnalysisFeatures.MethodEvidence)
            .Methods.Single(method => method.Name == methodName)
            .MetadataToken;
}
