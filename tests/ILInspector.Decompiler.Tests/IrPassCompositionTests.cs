using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public sealed class IrPassCompositionTests
{
    [Fact]
    public void BuiltInPipelinesSatisfyTheirProfiles()
    {
        Assert.NotEmpty(IrPasses.Default);
        Assert.NotEmpty(IrPasses.Lowered);
        Assert.NotEmpty(IrPasses.CapturingLambdaPreparation);
        Assert.NotEmpty(IrPasses.CapturingLambdaCompletion);
        Assert.NotEmpty(IrPasses.ForIntermediateBody);
        Assert.NotEmpty(IrPasses.ForReconstruction<IteratorReconstructionPass>());
    }

    [Fact]
    public void MaterializationRequiresLateSlotsOnlyInlining()
    {
        var error = AssertInvalid(
            [new SlotMaterializationPass()],
            IrPassPipelineProfile.Partial);

        Assert.Contains(nameof(SlotMaterializationPass), error.Message);
        Assert.Contains(nameof(ExpressionInliningPass), error.Message);
    }

    [Fact]
    public void MaterializationMustPrecedeCoercionInsertion()
    {
        var error = AssertInvalid(
            [
                new ExpressionInliningPass(slotsOnly: true),
                new CoercionInsertionPass(),
                new SlotMaterializationPass(),
            ],
            IrPassPipelineProfile.Partial);

        Assert.Contains(
            $"{nameof(SlotMaterializationPass)} must precede {nameof(CoercionInsertionPass)}",
            error.Message);
    }

    [Fact]
    public void ResidualBindingRequiresMaterialization()
    {
        var error = AssertInvalid(
            [new CoercionInsertionPass(), new ResidualSlotBindingPass()],
            IrPassPipelineProfile.Partial);

        Assert.Contains(
            $"{nameof(ResidualSlotBindingPass)} requires {nameof(SlotMaterializationPass)}",
            error.Message);
    }

    [Fact]
    public void ResidualBindingMustImmediatelyFollowCoercionInsertion()
    {
        var error = AssertInvalid(
            [
                new ExpressionInliningPass(slotsOnly: true),
                new ProducerOnlySlotRetirementPass(),
                new SlotMaterializationPass(),
                new CoercionInsertionPass(),
                new ScalarSelfUpdatePass(),
                new ResidualSlotBindingPass(),
            ],
            IrPassPipelineProfile.Partial);

        Assert.Contains("must immediately follow", error.Message);
    }

    [Fact]
    public void CompleteProfileRequiresTheWholeStorageTail()
    {
        var error = AssertInvalid(
            [
                new ExpressionInliningPass(slotsOnly: true),
                new ProducerOnlySlotRetirementPass(),
                new SlotMaterializationPass(),
                new CoercionInsertionPass(),
            ],
            IrPassPipelineProfile.Complete);

        Assert.Contains(
            $"requires {nameof(ResidualSlotBindingPass)}",
            error.Message);
    }

    [Fact]
    public void DefiniteAssignmentMustBeTheFinalPass()
    {
        var error = AssertInvalid(
            [new DefiniteAssignmentPass(), new RecordingPass()],
            IrPassPipelineProfile.Partial);

        Assert.Contains("must be the final pass", error.Message);
    }

    [Fact]
    public void CapturingLambdaPreparationExcludesTheStorageTail()
    {
        var error = AssertInvalid(
            [new ExpressionInliningPass(slotsOnly: true)],
            IrPassPipelineProfile.CapturingLambdaPreparation);

        Assert.Contains("slots-only expression-inlining tail is prohibited", error.Message);
    }

    [Fact]
    public void IntermediateBodyRequiresMaterialization()
    {
        var error = AssertInvalid(
            [
                new ExpressionInliningPass(slotsOnly: true),
                new CoercionInsertionPass(),
            ],
            IrPassPipelineProfile.IntermediateBody);

        Assert.Contains("intermediate-body pipeline requires", error.Message);
    }

    [Fact]
    public void IntermediateBodyDefersResidualBinding()
    {
        var error = AssertInvalid(
            CompleteStorageTail(),
            IrPassPipelineProfile.IntermediateBody);

        Assert.Contains(
            $"{nameof(ResidualSlotBindingPass)} is prohibited",
            error.Message);
    }

    [Fact]
    public void ReconstructionRejectsEmissionStagePasses()
    {
        foreach (IIrPass pass in ReconstructionExclusions())
        {
            var error = AssertInvalid(
                [pass],
                IrPassPipelineProfile.Reconstruction,
                typeof(RecordingPass));

            Assert.Contains(pass.GetType().Name, error.Message);
        }
    }

    [Fact]
    public void ReconstructionRejectsItsRequestingPass()
    {
        var error = AssertInvalid(
            [new RecordingPass()],
            IrPassPipelineProfile.Reconstruction,
            typeof(RecordingPass));

        Assert.Contains("requesting pass", error.Message);
    }

    [Fact]
    public void InvalidCompositionRejectsBeforeTheFirstPassRuns()
    {
        var recorder = new RecordingPass();
        var function = EmptyFunction();

        Assert.Throws<InvalidOperationException>(
            () => IrPasses.Run(
                function,
                [recorder, new ResidualSlotBindingPass()]));

        Assert.False(recorder.Ran);
    }

    [Fact]
    public void UnrelatedCallerSuppliedPipelineRemainsValid()
    {
        var recorder = new RecordingPass();

        IrPasses.Run(EmptyFunction(), [recorder]);

        Assert.True(recorder.Ran);
    }

    static IEnumerable<IIrPass> ReconstructionExclusions()
    {
        yield return new ReferenceSlotTargetBindingPass();
        yield return new ReferenceCoalesceBindingPass();
        yield return new ReferenceConditionalBindingPass();
        yield return new PrimitiveJoinBindingPass();
        yield return new ProducerOnlySlotRetirementPass();
        yield return new SlotMaterializationPass();
        yield return new ResidualSlotBindingPass();
        yield return new PdbScopeEntryLocalPass();
        yield return new PdbLocalScopePass();
        yield return new CheckedIntegerOperandPass();
        yield return new ScalarSelfUpdatePass();
        yield return new DefiniteAssignmentPass();
    }

    static InvalidOperationException AssertInvalid(
        ImmutableArray<IIrPass> passes,
        IrPassPipelineProfile profile,
        Type? requestingPass = null)
        => Assert.Throws<InvalidOperationException>(
            () => IrPassComposition.Validate(
                "test pipeline",
                passes,
                profile,
                requestingPass));

    static ImmutableArray<IIrPass> CompleteStorageTail() =>
    [
        new ExpressionInliningPass(slotsOnly: true),
        new ProducerOnlySlotRetirementPass(),
        new SlotMaterializationPass(),
        new CoercionInsertionPass(),
        new ResidualSlotBindingPass(),
        new DefiniteAssignmentPass(),
    ];

    static IrFunction EmptyFunction()
    {
        var body = new BlockContainer();
        body.Add(new Block());
        return new IrFunction(
            "M",
            TypeRef.Definition("synthetic", "", "Holder"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);
    }

    sealed class RecordingPass : IIrPass
    {
        public string Name => "recording";

        public bool Ran { get; private set; }

        public void Run(IrFunction function, PassContext context) => Ran = true;
    }
}
